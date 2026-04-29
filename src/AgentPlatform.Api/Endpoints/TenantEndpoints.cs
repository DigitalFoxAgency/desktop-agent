using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Auth;
using AgentPlatform.Application.Tenants;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Api.Endpoints;

public static class TenantEndpoints
{
    public static IEndpointRouteBuilder MapTenantEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/api/tenants").RequireAuthorization();

        group.MapGet("/{id:guid}/users", async (Guid id, IRequestTenantContext ctx, TenantService tenants, CancellationToken ct) =>
        {
            if (ctx.TenantId != id) { return Results.Forbid(); }
            var users = await tenants.ListUsersAsync(id, ct);
            return Results.Ok(users.Select(u => new { u.Id, u.Email, u.DisplayName, u.LastSignedInAt }));
        });

        group.MapPost("/{id:guid}/users", async (
            Guid id,
            AddUserDto body,
            IRequestTenantContext ctx,
            IAuthBackend backend,
            TenantService tenants,
            IAuditLog audit,
            IClock clock,
            CancellationToken ct) =>
        {
            if (body is null) { return Results.BadRequest(); }
            if (ctx.TenantId != id || !ctx.Roles.Contains(Role.Admin)) { return Results.Forbid(); }
            var auth = await backend.CreateUserAsync(id, body.Email, body.Password, body.DisplayName, ct);
            if (!auth.Succeeded) { return Results.BadRequest(new { error = auth.Error }); }
            await tenants.AddUserAsync(id, auth.UserId, body.Email, body.DisplayName, ct);
            if (!string.IsNullOrEmpty(body.Role))
            {
                await tenants.AssignRoleAsync(id, auth.UserId, RoleNames.FromSlug(body.Role), ct);
            }
            await audit.WriteAsync(new AuditEntry
            {
                Id = Guid.NewGuid(),
                TenantId = id,
                ActorUserId = ctx.UserId,
                Category = "tenant",
                Action = "user-add",
                SubjectType = "user",
                SubjectId = auth.UserId.ToString(),
                OccurredAt = clock.UtcNow,
            }, ct);
            return Results.Ok(new { userId = auth.UserId });
        });

        group.MapPost("/{id:guid}/roles", async (
            Guid id,
            AssignRoleDto body,
            IRequestTenantContext ctx,
            TenantService tenants,
            IAuditLog audit,
            IClock clock,
            CancellationToken ct) =>
        {
            if (body is null) { return Results.BadRequest(); }
            if (ctx.TenantId != id || !ctx.Roles.Contains(Role.Admin)) { return Results.Forbid(); }
            var role = RoleNames.FromSlug(body.Role);
            await tenants.AssignRoleAsync(id, body.UserId, role, ct);
            await audit.WriteAsync(new AuditEntry
            {
                Id = Guid.NewGuid(),
                TenantId = id,
                ActorUserId = ctx.UserId,
                Category = "tenant",
                Action = "role-assign",
                SubjectType = "user",
                SubjectId = body.UserId.ToString(),
                PayloadJson = $"{{\"role\":\"{body.Role}\"}}",
                OccurredAt = clock.UtcNow,
            }, ct);
            return Results.Ok();
        });

        return app;
    }

    public sealed record AddUserDto(string Email, string Password, string DisplayName, string? Role);
    public sealed record AssignRoleDto(Guid UserId, string Role);
}
