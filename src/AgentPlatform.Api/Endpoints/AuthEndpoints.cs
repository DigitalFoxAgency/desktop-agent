using AgentPlatform.Api.Auth;
using AgentPlatform.Application.Auth;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var group = app.MapGroup("/api/auth");

        group.MapPost("/signup", async (SignUpDto body, AuthService auth, JwtIssuer jwt, CancellationToken ct) =>
        {
            if (body is null) { return Results.BadRequest(new { error = "Body required." }); }
            var result = await auth.SignUpAsync(new SignUpRequest(
                body.AgencyName, body.AgencySlug, body.Email, body.Password, body.DisplayName), ct);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new { error = result.Error });
            }
            var token = jwt.Issue(result.TenantId!.Value, result.UserId!.Value, body.Email, new[] { Role.Admin });
            return Results.Ok(new SignInResponse(token, result.TenantId.Value, result.UserId.Value, new[] { RoleNames.Admin }));
        });

        group.MapPost("/signin", async (SignInDto body, AuthService auth, JwtIssuer jwt, CancellationToken ct) =>
        {
            if (body is null) { return Results.BadRequest(new { error = "Body required." }); }
            var result = await auth.SignInAsync(body.Email, body.Password, ct);
            if (!result.Succeeded)
            {
                return Results.Unauthorized();
            }
            var token = jwt.Issue(result.TenantId!.Value, result.UserId!.Value, body.Email, result.Roles);
            return Results.Ok(new SignInResponse(
                token,
                result.TenantId.Value,
                result.UserId.Value,
                result.Roles.Select(RoleNames.ToSlug).ToArray()));
        });

        group.MapPost("/signout", () => Results.Ok());

        return app;
    }

    public sealed record SignUpDto(string AgencyName, string AgencySlug, string Email, string Password, string DisplayName);
    public sealed record SignInDto(string Email, string Password);
    public sealed record SignInResponse(string Token, Guid TenantId, Guid UserId, IReadOnlyCollection<string> Roles);
}
