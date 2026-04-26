using AgentDesktop.Application.Chat;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Application.Runtime;

/// <summary>
/// Lifecycle and dispatch boundary for the local agent runtime. The
/// platform talks to OpenClaw + NemoClaw exclusively through this
/// interface — see <c>contracts/IRuntimeManager.md</c> for the full
/// behavioural contract (state machine, sandbox enforcement,
/// streaming guarantees).
/// </summary>
public interface IRuntimeManager : IAsyncDisposable
{
    /// <summary>Current observed status of the runtime.</summary>
    RuntimeStatus Status { get; }

    /// <summary>Push notifications when <see cref="Status"/> changes.</summary>
    IObservable<RuntimeStatus> StatusChanged { get; }

    /// <summary>Idempotently install the runtime if missing. Transitions <see cref="Status"/> through Installing.</summary>
    Task EnsureInstalledAsync(CancellationToken ct);

    /// <summary>Start the runtime. Transitions Status through Starting → Ready (or Degraded).</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>Stop the runtime cleanly. Transitions Status to Stopped.</summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Invoke a single skill on the runtime. Throws if Status is not
    /// <see cref="RuntimeStatus.Ready"/> — the exception names the
    /// current status. All side effects MUST run inside the sandbox.
    /// </summary>
    Task<SkillInvocationResult> InvokeSkillAsync(
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct);

    /// <summary>Stream agent reply chunks for a chat turn.</summary>
    IAsyncEnumerable<MessageChunk> StreamChatAsync(
        ConversationId conversationId,
        IReadOnlyList<Message> history,
        string userMessage,
        CancellationToken ct);
}

/// <summary>Result of a single skill invocation through the runtime.</summary>
public sealed record SkillInvocationResult(
    bool Succeeded,
    IReadOnlyDictionary<string, object?> Outputs,
    string? Error = null);
