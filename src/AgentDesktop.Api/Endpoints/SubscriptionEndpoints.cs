namespace AgentDesktop.Api.Endpoints;

/// <summary>
/// Subscription-validation endpoints. Implementations are intentionally
/// stub-shaped at MVP: the real upstream (Stripe / accounts service) is
/// out of scope for the desktop client itself; this surface exists so the
/// Desktop client has something concrete to call through HttpSubscriptionGate.
/// </summary>
public static class SubscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/subscription");

        group.MapPost("/validate", static (ValidateSubscriptionRequest request) =>
        {
            // MVP stub: any non-empty token is treated as Active.
            // Replace with real upstream validation when the accounts service exists.
            if (string.IsNullOrWhiteSpace(request.SessionToken))
            {
                return Results.BadRequest(new { error = "session_token_required" });
            }

            return Results.Ok(new ValidateSubscriptionResponse(
                Status: "Active",
                AccountId: "stub-account",
                ValidatedAt: DateTimeOffset.UtcNow));
        });

        return app;
    }
}

public sealed record ValidateSubscriptionRequest(string SessionToken);

public sealed record ValidateSubscriptionResponse(
    string Status,
    string AccountId,
    DateTimeOffset ValidatedAt);
