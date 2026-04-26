# Contract: `IRuntimeManager`

**Project**: `AgentDesktop.Application` (`Runtime/IRuntimeManager.cs`)
**Implementations**: `ProcessRuntimeManager` (Infrastructure, real),
`FakeRuntimeManager` (tests).

## Interface

```csharp
public interface IRuntimeManager : IAsyncDisposable
{
    RuntimeStatus Status { get; }
    IObservable<RuntimeStatus> StatusChanged { get; }

    Task EnsureInstalledAsync(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);

    Task<SkillInvocationResult> InvokeSkillAsync(
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct);

    IAsyncEnumerable<MessageChunk> StreamChatAsync(
        ConversationId conversationId,
        IReadOnlyList<Message> history,
        string userMessage,
        CancellationToken ct);
}
```

## Behavioural contract

1. The manager MUST drive `Status` through the documented state
   machine (`NotInstalled → Installing → Starting → Ready →
   Degraded → Stopped`); illegal transitions MUST throw.
2. `InvokeSkillAsync` MUST refuse if `Status != Ready` and MUST
   surface the current status in the exception.
3. All side-effecting work MUST happen inside the sandbox layer.
   The manager MUST refuse to dispatch a skill whose host process
   has escaped the sandbox (verifiable by the sandbox's exit
   status). FR-017.
4. The fake implementation MUST be deterministic and configurable:
   tests can pre-program responses for a given `(moduleId, skillId)`
   and observe the exact invocation arguments.
5. Disposal MUST stop child processes, flush logs, and transition
   `Status` to `Stopped`.

## Required tests

- The fake honours the documented state machine.
- `InvokeSkillAsync` throws a typed exception when the runtime is
  not `Ready`; the exception names the current status.
- `StreamChatAsync` yields chunks in order and finalises exactly
  once.
- Disposal twice is a no-op (idempotent).
- Contract test asserts SC-007 by running the full Application
  suite with `FakeRuntimeManager` only.
