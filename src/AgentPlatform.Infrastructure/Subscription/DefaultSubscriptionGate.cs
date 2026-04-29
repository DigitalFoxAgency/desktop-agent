using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Subscription;
using AgentPlatform.Application.Tenants;
using AgentPlatform.Application.Usage;

namespace AgentPlatform.Infrastructure.Subscription;

public sealed class DefaultSubscriptionGate : ISubscriptionGate
{
    private readonly ITenantRepository _tenants;
    private readonly IUsageMeter _usage;
    private readonly IClock _clock;

    public DefaultSubscriptionGate(ITenantRepository tenants, IUsageMeter usage, IClock clock)
    {
        _tenants = tenants;
        _usage = usage;
        _clock = clock;
    }

    public async Task<SubscriptionDecision> CheckAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (tenant is null)
        {
            return new SubscriptionDecision(false, "Tenant not found.");
        }

        if (tenant.SubscriptionExpiresAt is { } exp && exp <= _clock.UtcNow)
        {
            return new SubscriptionDecision(false, "Subscription expired.");
        }

        if (tenant.MonthlyTokenBudget > 0)
        {
            var since = new DateTimeOffset(_clock.UtcNow.Year, _clock.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var summary = await _usage.SummariseAsync(tenantId, since, cancellationToken).ConfigureAwait(false);
            var consumed = summary.InputTokens + summary.OutputTokens;
            if (consumed >= tenant.MonthlyTokenBudget)
            {
                return new SubscriptionDecision(false, "Monthly token budget exhausted.");
            }
        }

        return new SubscriptionDecision(true, null);
    }
}
