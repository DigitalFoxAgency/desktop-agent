using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Domain.Runs;

public sealed class Assignment
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid PhaseRunId { get; init; }
    public Role RequiredRole { get; init; }
    public Guid? AssignedUserId { get; set; }
    public AssignmentState State { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}

public enum AssignmentState
{
    Waiting = 0,
    Assigned = 1,
    Accepted = 2,
    Reassigned = 3,
    Released = 4,
}
