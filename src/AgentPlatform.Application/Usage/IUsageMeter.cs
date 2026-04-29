using AgentPlatform.Domain.Audit;

namespace AgentPlatform.Application.Usage;

public interface IUsageMeter
{
    Task RecordAsync(UsageLedgerEntry entry, CancellationToken cancellationToken);

    Task<UsageSummary> SummariseAsync(Guid tenantId, DateTimeOffset since, CancellationToken cancellationToken);

    Task<long> GetRunCostCentsAsync(Guid workflowRunId, CancellationToken cancellationToken);
}

public sealed record UsageSummary(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    decimal CostUsd);
