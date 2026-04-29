namespace AgentPlatform.Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public string Plan { get; set; } = "trial";
    public DateTimeOffset? SubscriptionExpiresAt { get; set; }
    public long MonthlyTokenBudget { get; set; }
}
