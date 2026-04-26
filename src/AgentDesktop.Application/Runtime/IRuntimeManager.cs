using AgentDesktop.Application.Chat;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using AgentDesktop.Domain.Policies;

namespace AgentDesktop.Application.Runtime;

/// <summary>
/// Lifecycle and dispatch boundary for the local agent runtime.
///
/// <para><b>Architectural model</b>: modules are self-orchestrating
/// (research.md R18). The platform delegates whole operations via
/// <see cref="DelegateAsync"/> and the module pushes events back
/// through <see cref="IDelegationCallbacks"/>. The platform never
/// drives a module's internal step ordering.</para>
///
/// <para>See <c>contracts/IRuntimeManager.md</c> for the full
/// behavioural contract (state machine, sandbox enforcement,
/// streaming + delegation guarantees).</para>
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
    /// Delegate the full execution of an operation to the owning
    /// module. The module runs its own internal pipeline; progress,
    /// confirmation requests, and human-handoff requests flow back
    /// through <paramref name="callbacks"/>. Returns when the
    /// module's pipeline finishes.
    /// </summary>
    Task<DelegationResult> DelegateAsync(
        ModuleId moduleId,
        string operationId,
        IReadOnlyDictionary<string, object?> inputs,
        ConversationId conversationId,
        IDelegationCallbacks callbacks,
        CancellationToken ct);

    /// <summary>
    /// Direct invocation of a module-internal skill (US4 advanced
    /// path only). NOT used to compose multi-step flows. Throws if
    /// <see cref="Status"/> is not <see cref="RuntimeStatus.Ready"/>.
    /// </summary>
    Task<SkillInvocationResult> InvokeSkillAsync(
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct);

    /// <summary>
    /// Run one turn of the **top-level agent** (router-only).
    /// Given the user's latest message and the conversation
    /// history, the agent decides whether to (a) reply in plain
    /// text or (b) call its <c>delegate_operation</c> tool to
    /// hand off to a module. The agent's tool surface is fixed
    /// at <c>list_modules</c> + <c>delegate_operation</c>; it
    /// never asks the user clarifying questions itself — that
    /// is the module's job during delegation (see
    /// <see cref="IDelegationCallbacks.AskUserAsync"/>).
    /// Throws if Status is not Ready.
    /// </summary>
    IAsyncEnumerable<MessageChunk> RunChatTurnAsync(
        ConversationId conversationId,
        IReadOnlyList<Message> history,
        string userMessage,
        CancellationToken ct);
}

/// <summary>Final result of a delegation. Carries any outputs the module wants to surface.</summary>
public sealed record DelegationResult(
    bool Succeeded,
    IReadOnlyDictionary<string, object?> Outputs,
    string? Error = null);

/// <summary>Result of a single direct skill invocation (US4 advanced path).</summary>
public sealed record SkillInvocationResult(
    bool Succeeded,
    IReadOnlyDictionary<string, object?> Outputs,
    string? Error = null);

/// <summary>
/// Callback channel the running module uses to push events back to
/// the platform. The runtime adapter implements the platform side
/// of this and forwards it into the module process.
/// </summary>
public interface IDelegationCallbacks
{
    /// <summary>Module → platform: progress text. Surfaced verbatim in chat.</summary>
    Task EmitProgressAsync(string text, CancellationToken ct);

    /// <summary>
    /// Module → platform: a dangerous action the module is about
    /// to perform. The platform routes through the policy engine,
    /// surfaces the user's prompt, and returns Confirmed (true) /
    /// Declined (false). The module MUST honour the answer.
    /// </summary>
    Task<bool> RequestConfirmationAsync(DangerousAction action, CancellationToken ct);

    /// <summary>
    /// Module → platform: a step that requires the human (Discovery
    /// call, dashboard work, content approval). Completes when the
    /// user marks the step done; the module then resumes.
    /// </summary>
    Task RequestHumanHandoffAsync(string stepName, string instructions, CancellationToken ct);

    /// <summary>
    /// Module → platform: ask the user a free-text question and
    /// await their reply. Surfaces the question as an agent
    /// message in the chat; the platform routes the user's next
    /// chat input to this task as the answer. The module pauses
    /// until the answer arrives. No timeout at MVP; cancellation
    /// flows through the supplied <see cref="CancellationToken"/>.
    /// </summary>
    Task<string> AskUserAsync(string question, CancellationToken ct);
}
