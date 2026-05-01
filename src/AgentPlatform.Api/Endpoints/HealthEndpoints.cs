using AgentPlatform.Infrastructure.Persistence.Postgres;

namespace AgentPlatform.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Liveness: is the process up? Stays cheap on purpose so it's safe to
        // hit on a tight loop from a load balancer.
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }))
            .AllowAnonymous();

        // Readiness: can we actually serve requests? Pings Postgres; degrades
        // to 503 if the DB connection is down so orchestrators stop routing
        // traffic until it's healthy again.
        app.MapGet("/readyz", async (AgentPlatformDbContext db, CancellationToken ct) =>
        {
            try
            {
                var ok = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
                return ok
                    ? Results.Ok(new { status = "ready", db = "ok" })
                    : Results.Json(new { status = "not_ready", db = "unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "not_ready", db = "error", error = ex.GetType().Name }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();

        return app;
    }
}
