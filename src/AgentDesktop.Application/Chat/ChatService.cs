using System.Runtime.CompilerServices;
using System.Text;
using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Application.Subscription;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Application.Chat;

/// <summary>
/// Default <see cref="IChatService"/>. Orchestrates the
/// repository, runtime, and subscription gate. See
/// <c>contracts/IChatService.md</c> for the behavioural rules
/// this implementation honours.
/// </summary>
public sealed class ChatService : IChatService
{
    private const string DefaultDraftTitle = "New conversation";
    private const int AutoTitleMaxChars = 60;

    private readonly IChatRepository _repository;
    private readonly IRuntimeManager _runtime;
    private readonly ISubscriptionGate _subscription;
    private readonly IClock _clock;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        IChatRepository repository,
        IRuntimeManager runtime,
        ISubscriptionGate subscription,
        IClock clock,
        ILogger<ChatService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _runtime = runtime;
        _subscription = subscription;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Conversation> StartConversationAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var conversation = new Conversation(
            ConversationId.New(),
            DefaultDraftTitle,
            createdAt: now,
            lastActivityAt: now);

        await _repository.AddAsync(conversation, ct).ConfigureAwait(false);
        return conversation;
    }

    public async Task<Conversation> GetConversationAsync(ConversationId id, CancellationToken ct)
    {
        var convo = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        if (convo is null)
        {
            throw new InvalidOperationException($"Conversation {id} not found.");
        }

        return convo;
    }

    public IAsyncEnumerable<Conversation> ListConversationsAsync(CancellationToken ct) =>
        _repository.ListAsync(ct);

    public IAsyncEnumerable<MessageChunk> SendMessageAsync(
        ConversationId conversationId,
        string body,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("Message body cannot be empty.", nameof(body));
        }

        return SendMessageCoreAsync(conversationId, body, ct);
    }

    private async IAsyncEnumerable<MessageChunk> SendMessageCoreAsync(
        ConversationId conversationId,
        string body,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var convo = await _repository.GetAsync(conversationId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Conversation {conversationId} not found.");

        // FR-023: subscription gate must be honoured before any agent work.
        if (!_subscription.AgentCapabilitiesEnabled)
        {
            var status = _subscription.CurrentStatus;
            _logger.LogInformation(
                "Refusing chat send for conversation {ConversationId}: subscription status is {Status}.",
                conversationId,
                status);
            var systemMessage = await PersistSystemMessageAsync(
                convo,
                $"Agent is unavailable ({status}). Please re-validate your subscription to continue.",
                ct).ConfigureAwait(false);
            yield return new MessageChunk(systemMessage.Id, systemMessage.Body, IsFinal: true);
            yield break;
        }

        var userMessage = new Message(
            id: MessageId.New(),
            conversationId: conversationId,
            index: convo.Messages.Count,
            author: MessageAuthor.User,
            body: body,
            createdAt: _clock.UtcNow);

        await _repository.AppendMessageAsync(userMessage, ct).ConfigureAwait(false);
        convo.Append(userMessage);

        // Auto-title the conversation from the first user message.
        if (string.Equals(convo.Title, DefaultDraftTitle, StringComparison.Ordinal))
        {
            var derived = DeriveTitle(body);
            await _repository.UpdateMetadataAsync(conversationId, derived, _clock.UtcNow, ct).ConfigureAwait(false);
            convo.Rename(derived);
        }

        var agentMessageId = MessageId.New();
        var bodyBuilder = new StringBuilder();
        var sawFinal = false;

        var failureMessage = (string?)null;
        var historySnapshot = convo.Messages;

        await foreach (var chunk in StreamRuntimeChunksAsync(conversationId, historySnapshot, body, ct).ConfigureAwait(false))
        {
            // Synthesise an id if the runtime didn't supply one (defensive).
            var stableId = chunk.MessageId.Value == Guid.Empty ? agentMessageId : chunk.MessageId;
            agentMessageId = stableId;

            bodyBuilder.Append(chunk.DeltaText);
            yield return new MessageChunk(stableId, chunk.DeltaText, chunk.IsFinal);

            if (chunk.IsFinal)
            {
                sawFinal = true;
                break;
            }
        }

        if (!sawFinal)
        {
            // Cancellation or runtime degradation mid-stream — finalise gracefully.
            failureMessage = ct.IsCancellationRequested
                ? "Reply cancelled."
                : "Runtime stopped mid-reply; partial reply preserved.";
            _logger.LogWarning(
                "Stream ended without IsFinal chunk for conversation {ConversationId}: {Reason}.",
                conversationId,
                failureMessage);
            yield return new MessageChunk(agentMessageId, string.Empty, IsFinal: true);
        }

        var assembled = bodyBuilder.ToString();
        // Don't persist empty-or-whitespace agent messages — the runtime may
        // legitimately end with empty chunks (final marker) and we never want
        // a blank bubble in history.
        if (!string.IsNullOrWhiteSpace(assembled))
        {
            var agentMessage = new Message(
                id: agentMessageId,
                conversationId: conversationId,
                index: convo.Messages.Count,
                author: MessageAuthor.Agent,
                body: assembled,
                createdAt: _clock.UtcNow);

            await _repository.AppendMessageAsync(agentMessage, ct).ConfigureAwait(false);
            convo.Append(agentMessage);
        }

        if (failureMessage is not null)
        {
            await PersistSystemMessageAsync(convo, failureMessage, ct).ConfigureAwait(false);
        }
    }

    private async IAsyncEnumerable<MessageChunk> StreamRuntimeChunksAsync(
        ConversationId conversationId,
        IReadOnlyList<Message> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // FakeRuntimeManager (or the real one) might fail mid-stream.
        // We catch and surface a System message in the calling method.
        await foreach (var chunk in _runtime.RunChatTurnAsync(conversationId, history, userMessage, ct).ConfigureAwait(false))
        {
            yield return chunk;
        }
    }

    private async Task<Message> PersistSystemMessageAsync(Conversation convo, string text, CancellationToken ct)
    {
        var message = new Message(
            id: MessageId.New(),
            conversationId: convo.Id,
            index: convo.Messages.Count,
            author: MessageAuthor.System,
            body: text,
            createdAt: _clock.UtcNow);
        await _repository.AppendMessageAsync(message, ct).ConfigureAwait(false);
        convo.Append(message);
        return message;
    }

    private static string DeriveTitle(string firstMessage)
    {
        var trimmed = firstMessage.Trim();
        if (trimmed.Length <= AutoTitleMaxChars)
        {
            return trimmed;
        }

        return string.Create(
            AutoTitleMaxChars + 1,
            trimmed,
            (span, src) =>
            {
                src.AsSpan(0, AutoTitleMaxChars).CopyTo(span);
                span[AutoTitleMaxChars] = '…';
            });
    }
}
