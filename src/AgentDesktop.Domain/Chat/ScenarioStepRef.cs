namespace AgentDesktop.Domain.Chat;

/// <summary>
/// Lightweight reference to a scenario step that produced a message.
/// Stored on <see cref="Message"/> so the chat UI can correlate
/// agent messages with scenario progress without loading the full
/// scenario aggregate.
/// </summary>
public readonly record struct ScenarioStepRef(ScenarioId ScenarioId, int StepIndex);
