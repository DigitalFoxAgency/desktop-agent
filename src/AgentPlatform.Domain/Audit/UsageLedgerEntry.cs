namespace AgentPlatform.Domain.Audit;

public sealed class UsageLedgerEntry
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid WorkflowRunId { get; init; }
    public Guid PhaseRunId { get; init; }
    public string Model { get; set; } = string.Empty;
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long CacheCreationTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTimeOffset RecordedAt { get; init; }
}
