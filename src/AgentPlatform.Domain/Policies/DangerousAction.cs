namespace AgentPlatform.Domain.Policies;

public sealed class DangerousAction
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid PhaseRunId { get; init; }
    public ActionClassification Classification { get; init; }
    public string Summary { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string? CommandLine { get; set; }
    public DateTimeOffset DetectedAt { get; init; }
}
