namespace AgentPlatform.Domain.Audit;

public sealed class AuditEntry
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? SubjectType { get; set; }
    public string? SubjectId { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; init; }
}
