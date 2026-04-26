using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Secrets;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentDesktop.Application.Subscription;

/// <summary>
/// Default <see cref="ISubscriptionGate"/>. Holds the cached
/// account + status, calls the supplied
/// <see cref="ISubscriptionValidator"/> for online checks, persists
/// the session token via <see cref="ISecretStore"/>, and applies a
/// configurable offline grace window per FR-023.
/// </summary>
public sealed class SubscriptionGate : ISubscriptionGate
{
    private static readonly SecretKey SessionTokenKey = new("subscription", "session-token");

    private readonly ISubscriptionValidator _validator;
    private readonly ISecretStore _secrets;
    private readonly IClock _clock;
    private readonly ILogger<SubscriptionGate> _logger;
    private readonly TimeSpan _graceWindow;
    private DateTimeOffset? _lastSuccessfulValidation;

    public SubscriptionGate(
        ISubscriptionValidator validator,
        ISecretStore secrets,
        IClock clock,
        IOptions<SubscriptionGateOptions> options,
        ILogger<SubscriptionGate> logger)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _validator = validator;
        _secrets = secrets;
        _clock = clock;
        _logger = logger;
        _graceWindow = options.Value.GraceWindow;
    }

    public SubscriptionStatus CurrentStatus { get; private set; } = SubscriptionStatus.Unknown;
    public UserAccount? CurrentAccount { get; private set; }

    public async Task<UserAccount> SignInAsync(string sessionToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessionToken);

        var account = await _validator.ValidateAsync(sessionToken, ct).ConfigureAwait(false);
        await _secrets.SetAsync(SessionTokenKey, sessionToken, ct).ConfigureAwait(false);
        CurrentAccount = account;
        CurrentStatus = account.SubscriptionStatus;
        _lastSuccessfulValidation = _clock.UtcNow;
        return account;
    }

    public async Task<UserAccount> RevalidateAsync(CancellationToken ct)
    {
        var token = await _secrets.GetAsync(SessionTokenKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("No stored subscription session token.");

        try
        {
            var account = await _validator.ValidateAsync(token, ct).ConfigureAwait(false);
            CurrentAccount = account;
            CurrentStatus = account.SubscriptionStatus;
            _lastSuccessfulValidation = _clock.UtcNow;
            return account;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "Subscription validation failed (network); applying grace window of {Grace}.",
                _graceWindow);

            // Apply grace window: stay Active for up to _graceWindow since last success.
            if (_lastSuccessfulValidation is { } last
                && _clock.UtcNow - last <= _graceWindow
                && CurrentAccount is not null)
            {
                CurrentStatus = SubscriptionStatus.Grace;
                CurrentAccount = new UserAccount(
                    CurrentAccount.AccountId,
                    CurrentAccount.Email,
                    SubscriptionStatus.Grace,
                    CurrentAccount.LastValidatedAt);
                return CurrentAccount;
            }

            CurrentStatus = SubscriptionStatus.Unknown;
            throw;
        }
    }

    public async Task SignOutAsync(CancellationToken ct)
    {
        await _secrets.DeleteAsync(SessionTokenKey, ct).ConfigureAwait(false);
        CurrentAccount = null;
        CurrentStatus = SubscriptionStatus.Unknown;
        _lastSuccessfulValidation = null;
    }
}

/// <summary>
/// Boundary for the upstream subscription service. Production
/// adapter calls <c>AgentDesktop.Api</c>; tests inject a deterministic
/// fake.
/// </summary>
public interface ISubscriptionValidator
{
    Task<UserAccount> ValidateAsync(string sessionToken, CancellationToken ct);
}

/// <summary>Configuration for <see cref="SubscriptionGate"/>.</summary>
public sealed class SubscriptionGateOptions
{
    /// <summary>How long agent capabilities stay enabled after a failed online validation. Default: 7 days.</summary>
    public TimeSpan GraceWindow { get; set; } = TimeSpan.FromDays(7);
}
