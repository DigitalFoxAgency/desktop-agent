using System.Collections.Concurrent;
using AgentDesktop.Domain;

namespace AgentDesktop.Application.Chat;

/// <summary>
/// In-process registry of pending delegation questions.
/// Lifetime: one per process. Thread-safe.
/// </summary>
public sealed class InMemoryConversationQuestionState : IConversationQuestionState
{
    private readonly ConcurrentDictionary<ConversationId, PendingEntry> _pending = new();

    public Task<string> RegisterPendingAsync(ConversationId conversationId, string question, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(question);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = ct.Register(() =>
        {
            if (_pending.TryRemove(conversationId, out _))
            {
                tcs.TrySetCanceled(ct);
            }
        });

        var entry = new PendingEntry(question, tcs, registration);
        if (!_pending.TryAdd(conversationId, entry))
        {
            throw new InvalidOperationException(
                $"Conversation {conversationId} already has a pending question.");
        }

        return tcs.Task;
    }

    public bool HasPending(ConversationId conversationId) =>
        _pending.ContainsKey(conversationId);

    public bool AnswerPending(ConversationId conversationId, string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        if (!_pending.TryRemove(conversationId, out var entry))
        {
            return false;
        }

        entry.Registration.Dispose();
        return entry.Completion.TrySetResult(answer);
    }

    private sealed record PendingEntry(
        string Question,
        TaskCompletionSource<string> Completion,
        CancellationTokenRegistration Registration);
}
