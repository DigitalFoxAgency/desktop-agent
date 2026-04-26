using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Application.Chat;

/// <summary>
/// Persistence port for chat aggregates. Application owns the
/// interface; Infrastructure provides a SQLite-backed adapter.
/// </summary>
public interface IChatRepository
{
    /// <summary>Persist a new conversation. Throws if id already exists.</summary>
    Task AddAsync(Conversation conversation, CancellationToken ct);

    /// <summary>Load a conversation with all messages. Returns <c>null</c> if not found.</summary>
    Task<Conversation?> GetAsync(ConversationId id, CancellationToken ct);

    /// <summary>List all conversations ordered by most recent activity first.</summary>
    IAsyncEnumerable<Conversation> ListAsync(CancellationToken ct);

    /// <summary>Append a single message to an existing conversation.</summary>
    Task AppendMessageAsync(Message message, CancellationToken ct);

    /// <summary>
    /// Update the conversation's title and last-activity timestamp.
    /// Used after auto-titling from the first user message.
    /// </summary>
    Task UpdateMetadataAsync(ConversationId id, string title, DateTimeOffset lastActivityAt, CancellationToken ct);
}
