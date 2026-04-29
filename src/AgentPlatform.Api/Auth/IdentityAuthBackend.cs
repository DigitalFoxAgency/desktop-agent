using AgentPlatform.Application.Auth;
using Microsoft.AspNetCore.Identity;

namespace AgentPlatform.Api.Auth;

public sealed class IdentityAuthBackend : IAuthBackend
{
    private readonly UserManager<ApplicationUser> _users;

    public IdentityAuthBackend(UserManager<ApplicationUser> users) => _users = users;

    public async Task<AuthBackendResult> CreateUserAsync(Guid tenantId, string email, string password, string displayName, CancellationToken cancellationToken)
    {
        var existing = await _users.FindByEmailAsync(email).ConfigureAwait(false);
        if (existing is not null)
        {
            return new AuthBackendResult(false, Guid.Empty, Guid.Empty, "Email already registered.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            TenantId = tenantId,
            DisplayName = displayName,
        };
        var result = await _users.CreateAsync(user, password).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new AuthBackendResult(false, Guid.Empty, Guid.Empty, string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        return new AuthBackendResult(true, user.Id, tenantId, null);
    }

    public async Task<AuthBackendResult> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken)
    {
        var user = await _users.FindByEmailAsync(email).ConfigureAwait(false);
        if (user is null)
        {
            return new AuthBackendResult(false, Guid.Empty, Guid.Empty, "Invalid credentials.");
        }
        var ok = await _users.CheckPasswordAsync(user, password).ConfigureAwait(false);
        if (!ok)
        {
            return new AuthBackendResult(false, Guid.Empty, Guid.Empty, "Invalid credentials.");
        }
        return new AuthBackendResult(true, user.Id, user.TenantId, null);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId.ToString()).ConfigureAwait(false);
        if (user is null) { return false; }
        var result = await _users.ChangePasswordAsync(user, currentPassword, newPassword).ConfigureAwait(false);
        return result.Succeeded;
    }
}
