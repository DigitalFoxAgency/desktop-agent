using AgentPlatform.Domain.Runs;

namespace AgentPlatform.Application.Runs;

public interface IPhaseRunRepository
{
    Task<PhaseRunWithRun?> GetAsync(Guid tenantId, Guid phaseRunId, CancellationToken cancellationToken);

    Task UpdateAsync(PhaseRun phase, CancellationToken cancellationToken);
}

public sealed record PhaseRunWithRun(PhaseRun Phase, WorkflowRun Run);
