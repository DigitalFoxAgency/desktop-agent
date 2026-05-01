using System.Security.Claims;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Api.Middleware;

public sealed class TenantScopeMiddleware(RequestDelegate next)
{
    public const string TenantClaim = "tenant_id";

    private readonly RequestDelegate _next = next;

    public Task InvokeAsync(HttpContext context, RequestTenantContext tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);

        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = user.FindFirst(TenantClaim);
            var subClaim = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var roles = user.FindAll(ClaimTypes.Role).Select(c => SafeRole(c.Value)).Where(r => r is not null).Select(r => r!.Value).ToArray();
            if (tenantClaim is not null && Guid.TryParse(tenantClaim.Value, out var tenantId)
                && Guid.TryParse(subClaim, out var userId))
            {
                tenantContext.Set(tenantId, userId, roles);
            }
        }

        return _next(context);
    }

    private static Role? SafeRole(string slug)
    {
        try { return RoleNames.FromSlug(slug); } catch (ArgumentException) { return null; }
    }
}

public sealed class RequestTenantContext : IRequestTenantContext
{
    private Guid? _tenantId;
    private Guid _userId;
    private IReadOnlyCollection<Role> _roles = Array.Empty<Role>();

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException("Tenant context not initialised for this request.");
    public Guid UserId => _userId;
    public IReadOnlyCollection<Role> Roles => _roles;

    public bool TryGetTenantId(out Guid tenantId)
    {
        if (_tenantId.HasValue)
        {
            tenantId = _tenantId.Value;
            return true;
        }
        tenantId = Guid.Empty;
        return false;
    }

    public void Set(Guid tenantId, Guid userId, IReadOnlyCollection<Role> roles)
    {
        _tenantId = tenantId;
        _userId = userId;
        _roles = roles;
    }
}

public static class TenantScopeExtensions
{
    public static IApplicationBuilder UseTenantScope(this IApplicationBuilder app)
        => app.UseMiddleware<TenantScopeMiddleware>();
}
