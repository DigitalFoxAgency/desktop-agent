using AgentPlatform.Application.Bridge;

namespace AgentPlatform.Application.Runs;

public interface IPhaseSessionService
{
    Task<OpenPhaseSessionResult> OpenAsync(Guid tenantId, Guid userId, Guid phaseRunId, CancellationToken cancellationToken);

    Task CloseAsync(Guid phaseRunId, CancellationToken cancellationToken);

    PhaseSessionHandle? GetActive(Guid phaseRunId);

    /// <summary>Returns liveness diagnostics for an active phase session, or null if the phase isn't active right now. Lets the UI render a "last activity X ago" badge and surfaces silent stalls.</summary>
    PhaseSessionDiagnostics? GetDiagnostics(Guid phaseRunId);
}

public sealed record PhaseSessionDiagnostics(
    Guid PhaseRunId,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastEventAt,
    string LastEventKind,
    int IdleSeconds,
    bool IsStalled);

public sealed record OpenPhaseSessionResult(
    bool Succeeded,
    PhaseSessionHandle? Session,
    string? Error)
{
    public static OpenPhaseSessionResult Success(PhaseSessionHandle session) => new(true, session, null);
    public static OpenPhaseSessionResult Failure(string error) => new(false, null, error);
}

public sealed class PhaseSessionHandle
{
    public required Guid PhaseRunId { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid WorkflowRunId { get; init; }
    public required string ContainerId { get; init; }
    public required string WorkingDir { get; init; }
    public required IBridgeChannel Channel { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    // Mutable liveness — updated by PhaseSessionService.PumpUsageAsync as
    // bridge events flow in. Drives /api/phases/{id}/diagnostics + idle stall
    // detection.
    public DateTimeOffset LastEventAt { get; set; }
    public string LastEventKind { get; set; } = "opened";
}
