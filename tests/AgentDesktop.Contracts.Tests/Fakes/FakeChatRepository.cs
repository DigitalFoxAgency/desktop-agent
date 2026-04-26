using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using AgentDesktop.Application.Chat;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>In-memory chat repository for tests. Concurrency-safe; records call order.</summary>
public sealed class FakeChatRepository : IChatRepository
{
    private readonly ConcurrentDictionary<ConversationId, Conversation> _conversations = new();
    private readonly object _lock = new();
    public List<string> Calls { get; } = new();

    public Task AddAsync(Conversation conversation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        lock (_lock) { Calls.Add($"Add({conversation.Id})"); }
        if (!_conversations.TryAdd(conversation.Id, conversation))
        {
            throw new InvalidOperationException($"Conversation {conversation.Id} already exists.");
        }
        return Task.CompletedTask;
    }

    public Task<Conversation?> GetAsync(ConversationId id, CancellationToken ct)
    {
        lock (_lock) { Calls.Add($"Get({id})"); }
        _conversations.TryGetValue(id, out var convo);
        return Task.FromResult<Conversation?>(convo);
    }

    public async IAsyncEnumerable<Conversation> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        lock (_lock) { Calls.Add("List"); }
        var snapshot = _conversations.Values
            .OrderByDescending(c => c.LastActivityAt)
            .ToList();
        foreach (var c in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return c;
            await Task.Yield();
        }
    }

    public Task AppendMessageAsync(Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        lock (_lock) { Calls.Add($"Append({message.ConversationId},#{message.Index},{message.Author})"); }

        if (!_conversations.TryGetValue(message.ConversationId, out var convo))
        {
            throw new InvalidOperationException($"Conversation {message.ConversationId} not found.");
        }

        convo.Append(message);
        return Task.CompletedTask;
    }

    public Task UpdateMetadataAsync(ConversationId id, string title, DateTimeOffset lastActivityAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(title);
        lock (_lock) { Calls.Add($"UpdateMetadata({id})"); }
        if (_conversations.TryGetValue(id, out var convo))
        {
            convo.Rename(title);
        }
        return Task.CompletedTask;
    }
}
