using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Application.Chat;

/// <summary>
/// The single entry point for everything chat-related. The UI never
/// talks to the model provider, runtime, or repository directly — it
/// talks to <see cref="IChatService"/>. See
/// <c>contracts/IChatService.md</c> for the behavioural contract.
/// </summary>
public interface IChatService
{
    /// <summary>Create and persist a fresh, empty conversation.</summary>
    Task<Conversation> StartConversationAsync(CancellationToken ct);

    /// <summary>Load a conversation by id, including all messages.</summary>
    Task<Conversation> GetConversationAsync(ConversationId id, CancellationToken ct);

    /// <summary>List all conversations ordered by most recent activity first.</summary>
    IAsyncEnumerable<Conversation> ListConversationsAsync(CancellationToken ct);

    /// <summary>
    /// Append the user's message and stream the agent's reply.
    /// First chunk MUST arrive within SC-002's budget on a healthy
    /// connection; the final chunk is signalled with
    /// <see cref="MessageChunk.IsFinal"/> = <c>true</c> exactly once.
    /// </summary>
    IAsyncEnumerable<MessageChunk> SendMessageAsync(
        ConversationId conversationId,
        string body,
        CancellationToken ct);
}

/// <summary>One incremental token (or final completion marker) of an agent reply.</summary>
public readonly record struct MessageChunk(MessageId MessageId, string DeltaText, bool IsFinal);
