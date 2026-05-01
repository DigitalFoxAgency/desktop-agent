using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Policies;

namespace AgentPlatform.Api.Endpoints;

public static class ConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/confirmations").RequireAuthorization();

        group.MapPost("/{id:guid}/decide", DecideAsync);

        return app;
    }

    private static async Task<IResult> DecideAsync(
        Guid id,
        DecideDto body,
        IRequestTenantContext tenantContext,
        ConfirmationService service,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!tenantContext.TryGetTenantId(out var tenantId))
        {
            return Results.Unauthorized();
        }
        var result = await service.DecideAsync(tenantId, tenantContext.UserId, id, body.Confirmed, body.Note, cancellationToken).ConfigureAwait(false);
        return result switch
        {
            DecideResult.Ok or DecideResult.OkSessionGone => Results.Ok(new { id, body.Confirmed }),
            DecideResult.NotFound => Results.NotFound(),
            DecideResult.AlreadyDecided => Results.Conflict(new { error = "already decided" }),
            _ => Results.Problem("unknown decide outcome"),
        };
    }

    public sealed record DecideDto(bool Confirmed, string? Note);
}
