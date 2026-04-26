namespace AgentDesktop.Domain.Chat;

/// <summary>
/// Lightweight reference to the running module delegation that
/// produced a message. Stored on <see cref="Message"/> so the chat
/// surface can correlate streamed messages with the operation in
/// flight without loading the full delegation state.
/// </summary>
public readonly record struct DelegationRef(ModuleId ModuleId, string OperationId);
