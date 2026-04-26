using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AgentDesktop.Application.Chat;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>
/// In-memory chat repository for tests. Concurrency-safe; records
/// call order. Tracks metadata + messages separately so each
/// <see cref="GetAsync"/> reconstructs a fresh aggregate — the
/// service is the sole owner of the live aggregate state, mirroring
/// what the real SQLite-backed repo does.
/// </summary>
public sealed class FakeChatRepository : IChatRepository
{
    private readonly ConcurrentDictionary<ConversationId, ConversationMetadata> _metadata = new();
    private readonly ConcurrentDictionary<ConversationId, List<Message>> _messagesById = new();
    private readonly object _lock = new();

    public List<string> Calls { get; } = new();

    public Task AddAsync(Conversation conversation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        lock (_lock) { Calls.Add($"Add({conversation.Id})"); }

        var meta = new ConversationMetadata(
            conversation.Id,
            conversation.Title,
            conversation.CreatedAt,
            conversation.LastActivityAt);

        if (!_metadata.TryAdd(conversation.Id, meta))
        {
            throw new InvalidOperationException($"Conversation {conversation.Id} already exists.");
        }

        _messagesById[conversation.Id] = new List<Message>(conversation.Messages);
        return Task.CompletedTask;
    }

    public Task<Conversation?> GetAsync(ConversationId id, CancellationToken ct)
    {
        lock (_lock) { Calls.Add($"Get({id})"); }

        if (!_metadata.TryGetValue(id, out var meta))
        {
            return Task.FromResult<Conversation?>(null);
        }

        if (!_messagesById.TryGetValue(id, out var messages))
        {
            messages = new List<Message>();
        }

        var snapshot = messages.ToList();
        var convo = new Conversation(meta.Id, meta.Title, meta.CreatedAt, meta.LastActivityAt, snapshot);
        return Task.FromResult<Conversation?>(convo);
    }

    public async IAsyncEnumerable<Conversation> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        lock (_lock) { Calls.Add("List"); }

        var ids = _metadata.Values
            .OrderByDescending(m => m.LastActivityAt)
            .Select(m => m.Id)
            .ToList();

        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var convo = await GetAsync(id, ct).ConfigureAwait(false);
            if (convo is not null)
            {
                yield return convo;
            }
        }
    }

    public Task AppendMessageAsync(Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_lock) { Calls.Add($"Append({message.ConversationId},#{message.Index},{message.Author})"); }

        if (!_messagesById.TryGetValue(message.ConversationId, out var list))
        {
            throw new InvalidOperationException($"Conversation {message.ConversationId} not found.");
        }

        list.Add(message);

        if (_metadata.TryGetValue(message.ConversationId, out var meta))
        {
            _metadata[message.ConversationId] = meta with { LastActivityAt = message.CreatedAt };
        }

        return Task.CompletedTask;
    }

    public Task UpdateMetadataAsync(ConversationId id, string title, DateTimeOffset lastActivityAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(title);
        lock (_lock) { Calls.Add($"UpdateMetadata({id})"); }

        if (_metadata.TryGetValue(id, out var meta))
        {
            _metadata[id] = meta with
            {
                Title = title,
                LastActivityAt = lastActivityAt,
            };
        }

        return Task.CompletedTask;
    }

    private sealed record ConversationMetadata(
        ConversationId Id,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastActivityAt);
}
