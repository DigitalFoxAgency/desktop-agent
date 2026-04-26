using AgentDesktop.Application.Subscription;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Identity;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>Programmable subscription gate. Tests set the status directly.</summary>
public sealed class FakeSubscriptionGate : ISubscriptionGate
{
    public SubscriptionStatus CurrentStatus { get; set; } = SubscriptionStatus.Active;

    public UserAccount? CurrentAccount { get; set; } =
        new UserAccount(new AccountId("test-account"), "user@example.test", SubscriptionStatus.Active);

    public Task<UserAccount> SignInAsync(string sessionToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessionToken);
        CurrentStatus = SubscriptionStatus.Active;
        CurrentAccount = new UserAccount(new AccountId("test-account"), "user@example.test", CurrentStatus);
        return Task.FromResult(CurrentAccount);
    }

    public Task<UserAccount> RevalidateAsync(CancellationToken ct)
    {
        if (CurrentAccount is null)
        {
            throw new InvalidOperationException("Not signed in.");
        }

        var refreshed = new UserAccount(
            CurrentAccount.AccountId,
            CurrentAccount.Email,
            CurrentStatus,
            DateTimeOffset.UtcNow);
        CurrentAccount = refreshed;
        return Task.FromResult(refreshed);
    }

    public Task SignOutAsync(CancellationToken ct)
    {
        CurrentStatus = SubscriptionStatus.Unknown;
        CurrentAccount = null;
        return Task.CompletedTask;
    }
}
