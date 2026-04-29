using AgentPlatform.Domain.Audit;

namespace AgentPlatform.Application.Abstractions;

public interface IAuditLog
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> QueryAsync(
        Guid tenantId,
        string? category,
        DateTimeOffset since,
        int limit,
        CancellationToken cancellationToken);
}
