using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Application.Scenarios;

/// <summary>
/// Executes a scenario step by step, surfacing progress via
/// <see cref="ScenarioEvent"/> values. See
/// <c>contracts/IScenarioRunner.md</c> for the behavioural contract.
/// </summary>
public interface IScenarioRunner
{
    IAsyncEnumerable<ScenarioEvent> RunAsync(
        ScenarioId scenarioId,
        IReadOnlyDictionary<string, object?> inputs,
        ConversationId conversationId,
        CancellationToken ct);
}

/// <summary>Discriminated union of events emitted by the runner.</summary>
public abstract record ScenarioEvent
{
    private ScenarioEvent()
    {
    }

    public sealed record StepStarted(int Index, ModuleId ModuleId, SkillId SkillId, string Description) : ScenarioEvent;

    public sealed record StepProgress(int Index, string Message) : ScenarioEvent;

    public sealed record StepConfirmationRequested(int Index, PolicyDecisionId DecisionId) : ScenarioEvent;

    public sealed record StepHumanHandoff(int Index, ModuleId ModuleId, SkillId SkillId, string Instructions) : ScenarioEvent;

    public sealed record StepCompleted(int Index, IReadOnlyDictionary<string, object?> Outputs) : ScenarioEvent;

    public sealed record StepFailed(int Index, string Error) : ScenarioEvent;

    public sealed record ScenarioCompleted(IReadOnlyDictionary<string, object?> FinalOutputs) : ScenarioEvent;

    public sealed record ScenarioCancelled(int LastStepIndex) : ScenarioEvent;

    public sealed record ScenarioFailed(int FailingStepIndex, string Error) : ScenarioEvent;
}
