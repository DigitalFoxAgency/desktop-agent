using AgentDesktop.Domain;
using AgentDesktop.Domain.Identity;

namespace AgentDesktop.Application.Subscription;

/// <summary>
/// Source of truth for the current subscription state. The
/// production adapter calls <c>AgentDesktop.Api</c> over HTTPS; tests
/// inject a deterministic fake.
/// </summary>
public interface ISubscriptionGate
{
    /// <summary>Most recently observed status. Cheap; no I/O.</summary>
    SubscriptionStatus CurrentStatus { get; }

    /// <summary>Currently signed-in account, or <c>null</c> if not signed in.</summary>
    UserAccount? CurrentAccount { get; }

    /// <summary>
    /// Sign in with the given subscription session token. Validates the
    /// token against the upstream service, persists it via
    /// <see cref="Secrets.ISecretStore"/>, and updates
    /// <see cref="CurrentStatus"/> + <see cref="CurrentAccount"/>.
    /// </summary>
    Task<UserAccount> SignInAsync(string sessionToken, CancellationToken ct);

    /// <summary>Re-validate the cached session against the upstream service.</summary>
    Task<UserAccount> RevalidateAsync(CancellationToken ct);

    /// <summary>Forget the cached session and clear stored secrets.</summary>
    Task SignOutAsync(CancellationToken ct);

    /// <summary>True if the current state allows agent capabilities to run.</summary>
    bool AgentCapabilitiesEnabled => CurrentStatus is SubscriptionStatus.Active or SubscriptionStatus.Grace;
}
