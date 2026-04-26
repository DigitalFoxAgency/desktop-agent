using AgentDesktop.Domain;

namespace AgentDesktop.Application.Chat;

/// <summary>
/// Per-conversation registry of pending
/// <see cref="Runtime.IDelegationCallbacks.AskUserAsync"/> questions.
/// The chat surface routes the next user message into the registered
/// completion source when one is pending; otherwise a new chat turn
/// runs through the top-level agent. See research.md R18 "Chat
/// routing during a pending question".
/// </summary>
public interface IConversationQuestionState
{
    /// <summary>
    /// Register a pending question. Returns a Task that will be
    /// completed by the next call to <see cref="AnswerPending"/>
    /// for the same conversation. Cancellation through the supplied
    /// token unregisters the pending question and faults the task.
    /// </summary>
    Task<string> RegisterPendingAsync(ConversationId conversationId, string question, CancellationToken ct);

    /// <summary>True if the conversation currently has an outstanding question.</summary>
    bool HasPending(ConversationId conversationId);

    /// <summary>
    /// Resolve the pending question for the given conversation with
    /// the user's answer. Returns true if a question was pending
    /// (and was resolved); false if the conversation had no pending
    /// question and the message should be routed as a new chat turn
    /// instead.
    /// </summary>
    bool AnswerPending(ConversationId conversationId, string answer);
}
