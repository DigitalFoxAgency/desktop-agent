namespace AgentPlatform.Domain.Tenants;

public sealed class User
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastSignedInAt { get; set; }
}
