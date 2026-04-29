namespace AgentPlatform.Domain.Tenants;

public sealed class UserRole
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Role Role { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
}
