using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Runs;

namespace AgentPlatform.Api.Endpoints;

public static class RunEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/api/runs").RequireAuthorization();

        group.MapPost("/", async (StartRunDto body, IRequestTenantContext ctx, WorkflowRunService runs, CancellationToken ct) =>
        {
            if (body is null) { return Results.BadRequest(new { error = "Body required." }); }
            var inputs = body.Inputs ?? new Dictionary<string, string?>();
            var result = await runs.StartAsync(new StartRunRequest(
                ctx.TenantId, ctx.UserId, body.ModuleId, body.WorkflowId, inputs), ct);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new { error = result.Error });
            }
            return Results.Ok(new { runId = result.RunId, phaseRunId = result.PhaseRunId, assignedUserId = result.AssignedUserId });
        });

        group.MapGet("/", async (IRequestTenantContext ctx, WorkflowRunService runs, CancellationToken ct, int? limit) =>
        {
            var list = await runs.ListAsync(ctx.TenantId, limit ?? 50, ct);
            return Results.Ok(list.Select(r => new
            {
                r.Id,
                r.ModuleId,
                r.WorkflowId,
                r.Status,
                r.StartedAt,
                r.CompletedAt,
            }));
        });

        group.MapGet("/{id:guid}", async (Guid id, IRequestTenantContext ctx, WorkflowRunService runs, CancellationToken ct) =>
        {
            var run = await runs.GetAsync(ctx.TenantId, id, ct);
            if (run is null) { return Results.NotFound(); }
            return Results.Ok(new
            {
                run.Id,
                run.ModuleId,
                run.WorkflowId,
                run.Status,
                run.StartedAt,
                run.CompletedAt,
                Phases = run.Phases.OrderBy(p => p.Order).Select(p => new { p.Id, p.PhaseId, p.Order, p.Status }),
            });
        });

        return app;
    }

    public sealed record StartRunDto(string ModuleId, string WorkflowId, Dictionary<string, string?>? Inputs);
}
