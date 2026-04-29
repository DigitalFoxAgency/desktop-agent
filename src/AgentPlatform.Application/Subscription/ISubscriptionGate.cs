namespace AgentPlatform.Application.Subscription;

public interface ISubscriptionGate
{
    Task<SubscriptionDecision> CheckAsync(Guid tenantId, CancellationToken cancellationToken);
}

public sealed record SubscriptionDecision(bool Allowed, string? Reason);
