using AgentPlatform.Application.Runs;
using AgentPlatform.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

public sealed class PostgresPhaseRunRepository : IPhaseRunRepository
{
    private readonly AgentPlatformDbContext _db;

    public PostgresPhaseRunRepository(AgentPlatformDbContext db) => _db = db;

    public async Task<PhaseRunWithRun?> GetAsync(Guid tenantId, Guid phaseRunId, CancellationToken cancellationToken)
    {
        var phase = await _db.PhaseRuns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == phaseRunId && p.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (phase is null)
        {
            return null;
        }

        var run = await _db.WorkflowRuns
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == phase.WorkflowRunId, cancellationToken)
            .ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        return new PhaseRunWithRun(phase, run);
    }

    public async Task UpdateAsync(PhaseRun phase, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phase);
        _db.PhaseRuns.Update(phase);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
