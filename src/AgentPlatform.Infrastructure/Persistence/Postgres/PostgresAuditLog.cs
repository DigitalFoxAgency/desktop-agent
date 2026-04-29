using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

public sealed class PostgresAuditLog : IAuditLog
{
    private readonly AgentPlatformDbContext _db;

    public PostgresAuditLog(AgentPlatformDbContext db) => _db = db;

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _db.AuditEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<AuditEntry>> QueryAsync(
        Guid tenantId,
        string? category,
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            limit = 100;
        }
        if (limit > 1000)
        {
            limit = 1000;
        }

        return QueryCoreAsync(tenantId, category, since, limit, cancellationToken);
    }

    private async Task<IReadOnlyList<AuditEntry>> QueryCoreAsync(
        Guid tenantId, string? category, DateTimeOffset since, int limit, CancellationToken cancellationToken)
    {
        var query = _db.AuditEntries
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.OccurredAt >= since);

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(e => e.Category == category);
        }

        var rows = await query
            .OrderByDescending(e => e.OccurredAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows;
    }
}
