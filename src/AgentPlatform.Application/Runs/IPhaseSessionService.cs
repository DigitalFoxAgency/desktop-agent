using AgentPlatform.Application.Bridge;

namespace AgentPlatform.Application.Runs;

public interface IPhaseSessionService
{
    Task<OpenPhaseSessionResult> OpenAsync(Guid tenantId, Guid userId, Guid phaseRunId, CancellationToken cancellationToken);

    Task CloseAsync(Guid phaseRunId, CancellationToken cancellationToken);

    PhaseSessionHandle? GetActive(Guid phaseRunId);
}

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
}
