using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Runs;
using AgentPlatform.Domain.Tenants;

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
            var inputs = body.Inputs ?? [];
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

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignDto body, IRequestTenantContext ctx, WorkflowRunService runs, CancellationToken ct) =>
        {
            if (body is null) { return Results.BadRequest(new { error = "Body required." }); }
            if (!ctx.Roles.Contains(Role.Admin)) { return Results.Forbid(); }
            // The run id in the route is for routing/audit; the actual operation targets a specific waiting phase.
            var run = await runs.GetAsync(ctx.TenantId, id, ct);
            if (run is null) { return Results.NotFound(); }
            if (!run.Phases.Any(p => p.Id == body.PhaseRunId))
            {
                return Results.BadRequest(new { error = "phaseRunId does not belong to this run" });
            }
            var result = await runs.ReassignAsync(ctx.TenantId, ctx.UserId, body.PhaseRunId, body.UserId, ct);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new { error = result.Error });
            }
            return Results.Ok(new { assignmentId = result.AssignmentId, userId = result.NewUserId });
        });

        return app;
    }

    public sealed record StartRunDto(string ModuleId, string WorkflowId, Dictionary<string, string?>? Inputs);

    public sealed record AssignDto(Guid PhaseRunId, Guid UserId);
}
