using AgentPlatform.Application.Usage;
using AgentPlatform.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

public sealed class PostgresUsageMeter : IUsageMeter
{
    private readonly AgentPlatformDbContext _db;

    public PostgresUsageMeter(AgentPlatformDbContext db) => _db = db;

    public async Task RecordAsync(UsageLedgerEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _db.UsageLedger.Add(entry);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<UsageSummary> SummariseAsync(Guid tenantId, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var rows = await _db.UsageLedger
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.RecordedAt >= since)
            .GroupBy(_ => 1)
            .Select(g => new UsageSummary(
                g.Sum(e => e.InputTokens),
                g.Sum(e => e.OutputTokens),
                g.Sum(e => e.CacheReadTokens),
                g.Sum(e => e.CostUsd)))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows ?? new UsageSummary(0, 0, 0, 0m);
    }

    public async Task<long> GetRunCostCentsAsync(Guid workflowRunId, CancellationToken cancellationToken)
    {
        var totalUsd = await _db.UsageLedger
            .IgnoreQueryFilters()
            .Where(e => e.WorkflowRunId == workflowRunId)
            .SumAsync(e => (decimal?)e.CostUsd, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

        return (long)Math.Ceiling(totalUsd * 100m);
    }
}
