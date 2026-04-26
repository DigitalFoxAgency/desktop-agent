using System.Net.Http.Json;
using AgentDesktop.Application.Subscription;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Identity;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Infrastructure.Subscription;

/// <summary>
/// Calls the <c>AgentDesktop.Api</c> subscription endpoint. The
/// upstream contract is a single POST <c>/v1/subscription/validate</c>
/// returning <c>{ status, accountId, validatedAt }</c>.
/// </summary>
public sealed class HttpSubscriptionValidator : ISubscriptionValidator
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpSubscriptionValidator> _logger;

    public HttpSubscriptionValidator(HttpClient httpClient, ILogger<HttpSubscriptionValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<UserAccount> ValidateAsync(string sessionToken, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sessionToken);

        var request = new ValidateRequest(sessionToken);
        var response = await _httpClient
            .PostAsJsonAsync("/v1/subscription/validate", request, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<ValidateResponse>(cancellationToken: ct)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException("Subscription endpoint returned an empty body.");

        var status = payload.Status switch
        {
            "Active" => SubscriptionStatus.Active,
            "Grace" => SubscriptionStatus.Grace,
            "Expired" => SubscriptionStatus.Expired,
            "Revoked" => SubscriptionStatus.Revoked,
            _ => SubscriptionStatus.Unknown,
        };

        _logger.LogInformation(
            "Subscription validated for account {AccountId}: {Status}.",
            payload.AccountId,
            status);

        return new UserAccount(
            new AccountId(payload.AccountId),
            email: "user@unknown",
            subscriptionStatus: status,
            lastValidatedAt: payload.ValidatedAt);
    }

    private sealed record ValidateRequest(string SessionToken);

    private sealed record ValidateResponse(string Status, string AccountId, DateTimeOffset ValidatedAt);
}
