namespace AgentPlatform.Application.Auth;

/// <summary>
/// Port over the identity provider. Implementations adapt ASP.NET Core Identity (or any other store).
/// </summary>
public interface IAuthBackend
{
    Task<AuthBackendResult> CreateUserAsync(Guid tenantId, string email, string password, string displayName, CancellationToken cancellationToken);

    Task<AuthBackendResult> VerifyPasswordAsync(string email, string password, CancellationToken cancellationToken);

    Task<bool> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken cancellationToken);
}

public sealed record AuthBackendResult(bool Succeeded, Guid UserId, Guid TenantId, string? Error);
