using AgentPlatform.Application.Runs;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Runs;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

public sealed class PostgresWorkflowRunRepository : IWorkflowRunRepository
{
    private readonly AgentPlatformDbContext _db;

    public PostgresWorkflowRunRepository(AgentPlatformDbContext db) => _db = db;

    public async Task SaveNewRunAsync(
        WorkflowRun run,
        PhaseRun firstPhase,
        Assignment assignment,
        InboxItem? inboxItem,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(firstPhase);
        ArgumentNullException.ThrowIfNull(assignment);

        _db.WorkflowRuns.Add(run);
        _db.PhaseRuns.Add(firstPhase);
        _db.Assignments.Add(assignment);
        if (inboxItem is not null)
        {
            _db.InboxItems.Add(inboxItem);
        }
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<WorkflowRun?> GetAsync(Guid tenantId, Guid runId, CancellationToken cancellationToken)
    {
        return await _db.WorkflowRuns
            .IgnoreQueryFilters()
            .Include(r => r.Phases)
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == runId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WorkflowRun>> ListAsync(Guid tenantId, int limit, CancellationToken cancellationToken)
    {
        return await _db.WorkflowRuns
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<InboxItem>> ListInboxAsync(Guid tenantId, Guid userId, int limit, CancellationToken cancellationToken)
    {
        return await _db.InboxItems
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.UserId == userId && i.DismissedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .Take(limit <= 0 ? 50 : Math.Min(limit, 200))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
