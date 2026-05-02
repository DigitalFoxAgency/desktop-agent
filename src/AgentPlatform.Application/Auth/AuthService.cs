using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Tenants;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Auth;

/// <summary>
/// Orchestrates sign-up (creates tenant + admin user) and sign-in.
/// Identity-provider details live behind <see cref="IAuthBackend"/>.
/// </summary>
public sealed class AuthService(IAuthBackend backend, TenantService tenants, IAuditLog audit, IClock clock)
{
    private readonly IAuthBackend _backend = backend;
    private readonly TenantService _tenants = tenants;
    private readonly IAuditLog _audit = audit;
    private readonly IClock _clock = clock;

    public async Task<SignUpResult> SignUpAsync(SignUpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Tenant tenant;
        try
        {
            tenant = await _tenants.CreateTenantAsync(request.AgencyName, request.AgencySlug, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return new SignUpResult(false, null, null, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new SignUpResult(false, null, null, ex.Message);
        }

        var auth = await _backend.CreateUserAsync(tenant.Id, request.Email, request.Password, request.DisplayName, cancellationToken).ConfigureAwait(false);
        if (!auth.Succeeded)
        {
            return new SignUpResult(false, null, null, auth.Error);
        }

        await _tenants.AddUserAsync(tenant.Id, auth.UserId, request.Email, request.DisplayName, cancellationToken).ConfigureAwait(false);
        await _tenants.AssignRoleAsync(tenant.Id, auth.UserId, Role.Admin, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            ActorUserId = auth.UserId,
            Category = "auth",
            Action = "signup",
            SubjectType = "tenant",
            SubjectId = tenant.Id.ToString(),
            OccurredAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);

        return new SignUpResult(true, tenant.Id, auth.UserId, null);
    }

    public async Task<SignInResult> SignInAsync(string email, string password, CancellationToken cancellationToken)
    {
        var auth = await _backend.VerifyPasswordAsync(email, password, cancellationToken).ConfigureAwait(false);
        if (!auth.Succeeded)
        {
            return new SignInResult(false, null, null, Array.Empty<Role>(), auth.Error);
        }

        var roles = await _tenants.GetRolesAsync(auth.TenantId, auth.UserId, cancellationToken).ConfigureAwait(false);
        await _tenants.MarkSignedInAsync(auth.TenantId, auth.UserId, _clock.UtcNow, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = auth.TenantId,
            ActorUserId = auth.UserId,
            Category = "auth",
            Action = "signin",
            SubjectType = "user",
            SubjectId = auth.UserId.ToString(),
            OccurredAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);

        return new SignInResult(true, auth.TenantId, auth.UserId, roles, null);
    }
}

public sealed record SignUpRequest(string AgencyName, string AgencySlug, string Email, string Password, string DisplayName);

public sealed record SignUpResult(bool Succeeded, Guid? TenantId, Guid? UserId, string? Error);

public sealed record SignInResult(bool Succeeded, Guid? TenantId, Guid? UserId, IReadOnlyCollection<Role> Roles, string? Error);
