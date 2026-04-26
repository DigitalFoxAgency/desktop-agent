namespace AgentDesktop.Domain.Identity;

/// <summary>
/// In-memory representation of the current subscriber. Sensitive
/// material (subscription session, model API token) never lives on
/// this type — it is fetched on demand from <c>ISecretStore</c>.
/// </summary>
public sealed record UserAccount
{
    public UserAccount(
        AccountId accountId,
        string email,
        SubscriptionStatus subscriptionStatus,
        DateTimeOffset? lastValidatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(email);

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email cannot be empty.", nameof(email));
        }

        AccountId = accountId;
        Email = email;
        SubscriptionStatus = subscriptionStatus;
        LastValidatedAt = lastValidatedAt;
    }

    public AccountId AccountId { get; }
    public string Email { get; }
    public SubscriptionStatus SubscriptionStatus { get; }
    public DateTimeOffset? LastValidatedAt { get; }

    /// <summary>True if agent capabilities should be enabled for this account right now.</summary>
    public bool AgentCapabilitiesEnabled => SubscriptionStatus is SubscriptionStatus.Active or SubscriptionStatus.Grace;
}
