using System.Security.Claims;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Runs;
using AgentPlatform.Domain.Tenants;
using Microsoft.AspNetCore.Mvc;

namespace AgentPlatform.Api.Endpoints;

public static class PhaseEndpoints
{
    public static IEndpointRouteBuilder MapPhaseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/phases").RequireAuthorization();

        group.MapPost("/{phaseRunId:guid}/open", OpenAsync);
        group.MapPost("/{phaseRunId:guid}/close", CloseAsync);
        group.MapPost("/{phaseRunId:guid}/reassign", ReassignAsync);
        group.MapGet("/{phaseRunId:guid}/files", ListFilesAsync);
        group.MapGet("/{phaseRunId:guid}/files/content", GetFileAsync);

        return app;
    }

    private static async Task<IResult> ReassignAsync(
        Guid phaseRunId,
        ReassignDto body,
        IRequestTenantContext tenantContext,
        WorkflowRunService runs,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.TryGetTenantId(out var tenantId)) { return Results.Unauthorized(); }
        if (!tenantContext.Roles.Contains(Role.Admin)) { return Results.Forbid(); }
        var result = await runs.ReassignAsync(tenantId, tenantContext.UserId, phaseRunId, body.UserId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Results.Problem(result.Error ?? "reassign failed", statusCode: StatusCodes.Status400BadRequest);
        }
        return Results.Ok(new { assignmentId = result.AssignmentId, userId = result.NewUserId });
    }

    public sealed record ReassignDto(Guid UserId);

    private static async Task<IResult> OpenAsync(
        Guid phaseRunId,
        HttpContext ctx,
        IPhaseSessionService sessions,
        IRequestTenantContext tenantContext,
        CancellationToken cancellationToken)
    {
        if (!tenantContext.TryGetTenantId(out var tenantId))
        {
            return Results.Unauthorized();
        }
        var userId = ResolveUserId(ctx);
        if (userId is null)
        {
            return Results.Unauthorized();
        }
        var result = await sessions.OpenAsync(tenantId, userId.Value, phaseRunId, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || result.Session is null)
        {
            return Results.Problem(result.Error ?? "open failed", statusCode: StatusCodes.Status409Conflict);
        }
        var s = result.Session;
        return Results.Ok(new
        {
            phaseRunId = s.PhaseRunId,
            workflowRunId = s.WorkflowRunId,
            containerId = s.ContainerId,
            startedAt = s.StartedAt,
        });
    }

    private static async Task<IResult> CloseAsync(
        Guid phaseRunId,
        IPhaseSessionService sessions,
        CancellationToken cancellationToken)
    {
        await sessions.CloseAsync(phaseRunId, cancellationToken).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static IResult ListFilesAsync(
        Guid phaseRunId,
        IPhaseSessionService sessions,
        IRequestTenantContext tenantContext,
        [FromQuery] string? path)
    {
        var session = sessions.GetActive(phaseRunId);
        if (session is null)
        {
            return Results.NotFound();
        }
        var root = session.WorkingDir;
        var rel = path ?? string.Empty;
        var fullPath = Path.GetFullPath(Path.Combine(root, rel));
        if (!fullPath.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "path escapes working dir" });
        }
        if (!Directory.Exists(fullPath))
        {
            return Results.NotFound();
        }
        var roles = tenantContext.Roles;
        var entries = Directory.EnumerateFileSystemEntries(fullPath)
            .Select(e =>
            {
                var info = new FileInfo(e);
                var entryRel = Path.GetRelativePath(root, e).Replace('\\', '/');
                return new
                {
                    name = Path.GetFileName(e),
                    path = entryRel,
                    isDirectory = Directory.Exists(e),
                    size = info.Exists ? info.Length : 0L,
                    modifiedAt = info.Exists ? info.LastWriteTimeUtc : (DateTime?)null,
                };
            })
            .Where(e => RoleFileVisibility.IsVisible(e.path, roles))
            .OrderBy(e => e.isDirectory ? 0 : 1)
            .ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return Results.Ok(entries);
    }

    private static IResult GetFileAsync(
        Guid phaseRunId,
        IPhaseSessionService sessions,
        IRequestTenantContext tenantContext,
        [FromQuery] string path)
    {
        var session = sessions.GetActive(phaseRunId);
        if (session is null)
        {
            return Results.NotFound();
        }
        if (string.IsNullOrEmpty(path))
        {
            return Results.BadRequest(new { error = "path required" });
        }
        var root = Path.GetFullPath(session.WorkingDir);
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
        {
            return Results.BadRequest(new { error = "path escapes working dir" });
        }
        // Don't leak existence — return 404 to non-tech callers reaching for hidden paths.
        if (!RoleFileVisibility.IsVisible(path.Replace('\\', '/'), tenantContext.Roles))
        {
            return Results.NotFound();
        }
        if (!File.Exists(fullPath))
        {
            return Results.NotFound();
        }
        var info = new FileInfo(fullPath);
        if (info.Length > 1_000_000)
        {
            return Results.BadRequest(new { error = "file too large to preview" });
        }
        var text = File.ReadAllText(fullPath);
        return Results.Ok(new { path, size = info.Length, content = text });
    }

    private static Guid? ResolveUserId(HttpContext ctx)
    {
        var raw = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
