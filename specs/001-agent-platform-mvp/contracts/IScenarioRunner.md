# Contract: `IScenarioRunner`

**Project**: `AgentDesktop.Application` (`Scenarios/IScenarioRunner.cs`)
**Implementations**: `ScenarioRunner` (Application).

## Interface

```csharp
public interface IScenarioRegistry
{
    Task RefreshAsync(CancellationToken ct);
    IReadOnlyList<Scenario> GetAll();
    Scenario? Find(ScenarioId id);
}

public interface IScenarioRunner
{
    IAsyncEnumerable<ScenarioEvent> RunAsync(
        ScenarioId scenarioId,
        IReadOnlyDictionary<string, object?> inputs,
        ConversationId conversationId,
        CancellationToken ct);
}

public abstract record ScenarioEvent
{
    public sealed record StepStarted(int Index, ModuleId ModuleId, SkillId SkillId, string Description) : ScenarioEvent;
    public sealed record StepProgress(int Index, string Message) : ScenarioEvent;
    public sealed record StepConfirmationRequested(int Index, PolicyDecisionId DecisionId) : ScenarioEvent;
    public sealed record StepCompleted(int Index, IReadOnlyDictionary<string, object?> Outputs) : ScenarioEvent;
    public sealed record StepFailed(int Index, string Error) : ScenarioEvent;
    public sealed record ScenarioCompleted(IReadOnlyDictionary<string, object?> FinalOutputs) : ScenarioEvent;
    public sealed record ScenarioCancelled(int LastStepIndex) : ScenarioEvent;
    public sealed record ScenarioFailed(int FailingStepIndex, string Error) : ScenarioEvent;
}
```

## Behavioural contract

1. The runner MUST refuse to start a scenario whose `LoadStatus` is
   not `Loaded` (FR-008, FR-018).
2. Step inputs MUST be resolved by the binding rules in `data-model.md`
   (literal | scenario-input | prior-step-output). Forward references
   MUST be rejected at load time, never at runtime.
3. Each step MUST be evaluated by `IPolicyEngine` before its
   side-effecting work runs. Dangerous steps MUST emit a
   `StepConfirmationRequested` event and pause until the engine
   reports a decision.
4. On cancellation, the runner MUST emit `ScenarioCancelled` after
   the in-flight step settles (success, failure, or a clean abort).
   It MUST NOT start any subsequent step.
5. On step failure, the runner MUST emit `StepFailed` followed by
   `ScenarioFailed`; later steps MUST NOT execute.
6. Every event stream MUST end with exactly one terminal event:
   `ScenarioCompleted`, `ScenarioCancelled`, or `ScenarioFailed`.

## Required tests

- A linear three-step scenario emits the documented event sequence.
- Cancellation between steps emits `ScenarioCancelled` and does not
  start the next step.
- A failing step emits `StepFailed` then `ScenarioFailed` and stops.
- A scenario with a forward binding fails to load (caught by
  `IScenarioRegistry`, never reaches the runner).
- A dangerous step pauses on `StepConfirmationRequested` and only
  resumes after the policy engine resolves the decision.
