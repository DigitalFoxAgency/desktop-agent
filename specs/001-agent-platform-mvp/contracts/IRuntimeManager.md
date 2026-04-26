# Contract: `IRuntimeManager`

**Project**: `AgentDesktop.Application` (`Runtime/IRuntimeManager.cs`)
**Implementations**: `ProcessRuntimeManager` (Infrastructure, real),
`FakeRuntimeManager` (tests).
**Architectural model**: see `research.md` R18 — modules are
self-orchestrating. The platform delegates **whole operations** and
observes events the module emits; it never drives a module's
internal steps.

## Interface

```csharp
public interface IRuntimeManager : IAsyncDisposable
{
    RuntimeStatus Status { get; }
    IObservable<RuntimeStatus> StatusChanged { get; }

    Task EnsureInstalledAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Delegate the full execution of an operation to the owning
    /// module. The module runs its own internal pipeline and pushes
    /// progress / confirmation / handoff requests back through
    /// <paramref name="callbacks"/>. Returns when the module's
    /// pipeline finishes (success / failure / cancellation).
    /// </summary>
    Task<DelegationResult> DelegateAsync(
        ModuleId moduleId,
        string operationId,
        IReadOnlyDictionary<string, object?> inputs,
        ConversationId conversationId,
        IDelegationCallbacks callbacks,
        CancellationToken ct);

    /// <summary>
    /// Direct invocation of a module's internal skill (US4 advanced
    /// path only). NOT used to compose multi-step flows — that is
    /// the module's responsibility. Throws if Status != Ready.
    /// </summary>
    Task<SkillInvocationResult> InvokeSkillAsync(
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct);

    /// <summary>
    /// Stream agent reply chunks for a chat turn that does NOT
    /// require module delegation (e.g. routing-style intent
    /// classification, casual chat).
    /// </summary>
    IAsyncEnumerable<MessageChunk> StreamChatAsync(
        ConversationId conversationId,
        IReadOnlyList<Message> history,
        string userMessage,
        CancellationToken ct);
}

public sealed record DelegationResult(
    bool Succeeded,
    IReadOnlyDictionary<string, object?> Outputs,
    string? Error = null);

public interface IDelegationCallbacks
{
    /// <summary>Module → platform: progress text. Surfaced verbatim in chat.</summary>
    Task EmitProgressAsync(string text, CancellationToken ct);

    /// <summary>
    /// Module → platform: a dangerous action the module is about
    /// to perform. The platform routes through IPolicyEngine,
    /// surfaces the user's confirmation prompt, and returns
    /// true=Confirmed / false=Declined. The module MUST honour
    /// the answer.
    /// </summary>
    Task<bool> RequestConfirmationAsync(DangerousAction action, CancellationToken ct);

    /// <summary>
    /// Module → platform: a step that requires the human (Discovery
    /// call, dashboard work, content approval). The platform shows
    /// a hand-off dialog. Completes when the user marks the step
    /// done; the module then resumes.
    /// </summary>
    Task RequestHumanHandoffAsync(string stepName, string instructions, CancellationToken ct);
}
```

## Behavioural contract

### Status state machine

1. The manager MUST drive `Status` through the state machine
   `NotInstalled → Installing → Starting → Ready → Degraded → Stopped`.
   Illegal transitions throw.
2. `DelegateAsync`, `InvokeSkillAsync`, and `StreamChatAsync` all
   require `Status == Ready`. They throw a typed exception that
   names the current status when called from any other state.
3. Disposal MUST stop child processes, flush logs, transition
   `Status` to `Stopped`. Repeated dispose is a no-op.

### `DelegateAsync`

4. The platform MUST resolve `(moduleId, operationId)` against the
   loaded module registry **before** calling `DelegateAsync`;
   `DelegateAsync` itself trusts the inputs and routes them to
   the module.
5. The module receives the operation and the supplied inputs; the
   platform makes no assumption about the module's internal step
   ordering, intermediate state, or completion timing.
6. `IDelegationCallbacks.EmitProgressAsync` MAY be called any
   number of times (including zero) before the operation
   completes. Calls are **synchronous from the module's
   perspective**: the module awaits the call's completion before
   emitting the next event, so the platform can backpressure when
   needed.
7. `IDelegationCallbacks.RequestConfirmationAsync` MUST be
   awaited synchronously by the module — the module does NOT
   speculatively continue while the user thinks. Returning
   `false` (Declined) means the module MUST NOT perform the
   action.
8. `IDelegationCallbacks.RequestHumanHandoffAsync` MUST also be
   awaited synchronously. Completion of the task means the user
   has marked the step done; the module then resumes.
9. `DelegationResult.Succeeded == true` means the operation's
   pipeline completed successfully end-to-end. `Outputs` carries
   any final result the module wants to surface (e.g. deployed
   site URL).
10. Cancellation via the supplied `CancellationToken` MUST
    propagate to the running module. The module finalises
    in-flight work, returns a result with `Succeeded == false`,
    and the `DelegationResult.Error` describes the cancellation.
11. Side-effecting work MUST run inside the sandbox layer of the
    runtime. Bypass MUST be refused (FR-017).

### `InvokeSkillAsync` (US4 advanced path)

12. Single shot — no progress events, no callbacks. Returns when
    the skill completes.
13. Same Ready-only and sandbox-isolation constraints as
    `DelegateAsync`.

### `StreamChatAsync`

14. Yields chunks in order. Finalises with exactly one
    `IsFinal=true` chunk.
15. Does NOT delegate to a module. Used for routing-style chat,
    intent classification surfaces, or casual replies.

## Required tests (contract)

- The fake honours the documented state machine; illegal
  transitions throw.
- `InvokeSkillAsync` throws a typed exception whose message names
  the current `Status` when called outside `Ready`.
- `StreamChatAsync` yields chunks in order and finalises exactly
  once.
- `DelegateAsync`:
  - Routes `(moduleId, operationId)` to a programmable responder
    in the fake; the responder receives the supplied inputs and
    a working `IDelegationCallbacks`.
  - `EmitProgressAsync` calls flow through the callbacks in order.
  - `RequestConfirmationAsync` blocks the module until the
    callback returns; returning `false` causes the module to
    skip the action and continue with whatever next path it
    chooses.
  - `RequestHumanHandoffAsync` blocks the module until the
    callback's task completes.
  - Cancelling the supplied `CancellationToken` propagates;
    the module finalises and the result's `Succeeded` is `false`.
- `DisposeAsync` is idempotent.
- The Phase 7 contract test runs the same suite against
  `ProcessRuntimeManager` behind the `RequiresLiveRuntime` trait
  to verify the real runtime honours the contract.
