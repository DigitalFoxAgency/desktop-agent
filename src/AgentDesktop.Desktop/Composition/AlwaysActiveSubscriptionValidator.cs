using AgentDesktop.Application.Subscription;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Identity;

namespace AgentDesktop.Desktop.Composition;

/// <summary>
/// Demo-mode <see cref="ISubscriptionValidator"/>: any non-empty token
/// is treated as a valid Active subscription. Only wired when
/// <c>--fake-runtime</c> is set, mirroring the demo runtime's posture.
/// </summary>
internal sealed class AlwaysActiveSubscriptionValidator : ISubscriptionValidator
{
    public Task<UserAccount> ValidateAsync(string sessionToken, CancellationToken ct)
    {
        var account = new UserAccount(
            new AccountId("demo-account"),
            email: "demo@local",
            subscriptionStatus: SubscriptionStatus.Active,
            lastValidatedAt: DateTimeOffset.UtcNow);
        return Task.FromResult(account);
    }
}
