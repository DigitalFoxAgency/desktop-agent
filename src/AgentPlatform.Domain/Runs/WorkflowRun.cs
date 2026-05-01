namespace AgentPlatform.Domain.Runs;

public sealed class WorkflowRun
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid WorkflowDefId { get; init; }
    public string ModuleId { get; set; } = string.Empty;
    public string WorkflowId { get; set; } = string.Empty;
    public Guid StartedByUserId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RunStatus Status { get; set; }
    public string InputsJson { get; set; } = "{}";
    public string WorkingDirPath { get; set; } = string.Empty;
    public List<PhaseRun> Phases { get; init; } = [];
}

public sealed class PhaseRun
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid WorkflowRunId { get; init; }
    public Guid PhaseDefId { get; init; }
    public int Order { get; init; }
    public string PhaseId { get; set; } = string.Empty;
    public RunStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ContainerId { get; set; }
}

public enum RunStatus
{
    Pending = 0,
    Waiting = 1,
    Running = 2,
    Paused = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
}
