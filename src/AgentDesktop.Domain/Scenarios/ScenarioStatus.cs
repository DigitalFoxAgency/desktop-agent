namespace AgentDesktop.Domain.Scenarios;

/// <summary>Runtime state of a scenario as it executes (not persisted as part of the scenario definition).</summary>
public enum ScenarioStatus
{
    NotStarted = 0,
    Running = 1,
    AwaitingConfirmation = 2,
    Cancelling = 3,
    Completed = 4,
    Cancelled = 5,
    Failed = 6,
}
