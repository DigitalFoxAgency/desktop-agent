using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Runs;

namespace AgentPlatform.Api.Endpoints;

public static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/inbox", async (IRequestTenantContext ctx, IWorkflowRunRepository repo, CancellationToken ct, int? limit) =>
        {
            var items = await repo.ListInboxAsync(ctx.TenantId, ctx.UserId, limit ?? 50, ct);
            return Results.Ok(items.Select(i => new
            {
                i.Id,
                i.PhaseRunId,
                i.Title,
                i.Subtitle,
                Kind = i.Kind.ToString(),
                i.CreatedAt,
            }));
        }).RequireAuthorization();

        return app;
    }
}
