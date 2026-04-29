using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgentPlatform.Api.Middleware;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Tenants;
using Microsoft.IdentityModel.Tokens;

namespace AgentPlatform.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "agent-platform";
    public string Audience { get; set; } = "agent-platform-web";
    public string SigningKey { get; set; } = "dev-only-secret-key-change-me-please-32+chars";
    public int LifetimeMinutes { get; set; } = 480;
}

public sealed class JwtIssuer
{
    private readonly JwtOptions _options;
    private readonly IClock _clock;

    public JwtIssuer(JwtOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public string Issue(Guid tenantId, Guid userId, string email, IEnumerable<Role> roles)
    {
        var now = _clock.UtcNow.UtcDateTime;
        var expires = now.AddMinutes(_options.LifetimeMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Email, email),
            new(TenantScopeMiddleware.TenantClaim, tenantId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, RoleNames.ToSlug(r))));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
