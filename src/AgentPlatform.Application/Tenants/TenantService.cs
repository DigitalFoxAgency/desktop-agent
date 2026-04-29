using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Tenants;

public sealed class TenantService
{
    private readonly ITenantRepository _repo;
    private readonly IClock _clock;

    public TenantService(ITenantRepository repo, IClock clock)
    {
        _repo = repo;
        _clock = clock;
    }

    public async Task<Tenant> CreateTenantAsync(string name, string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tenant name is required.", nameof(name));
        }
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Tenant slug is required.", nameof(slug));
        }

        var existing = await _repo.GetTenantBySlugAsync(slug, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Tenant slug '{slug}' is already in use.");
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            CreatedAt = _clock.UtcNow,
            Plan = "trial",
            SubscriptionExpiresAt = _clock.UtcNow.AddDays(30),
            MonthlyTokenBudget = 5_000_000,
        };
        return await _repo.AddTenantAsync(tenant, cancellationToken).ConfigureAwait(false);
    }

    public async Task AddUserAsync(Guid tenantId, Guid userId, string email, string displayName, CancellationToken cancellationToken)
    {
        var existing = await _repo.GetUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return;
        }

        await _repo.AddUserAsync(new User
        {
            Id = userId,
            TenantId = tenantId,
            Email = email,
            DisplayName = displayName,
            CreatedAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task AssignRoleAsync(Guid tenantId, Guid userId, Role role, CancellationToken cancellationToken)
    {
        return _repo.AssignRoleAsync(new UserRole
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            Role = role,
            AssignedAt = _clock.UtcNow,
        }, cancellationToken);
    }

    public Task<IReadOnlyCollection<Role>> GetRolesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
        => _repo.GetRolesAsync(tenantId, userId, cancellationToken);

    public Task<IReadOnlyList<User>> FindUsersByRoleAsync(Guid tenantId, Role role, CancellationToken cancellationToken)
        => _repo.FindUsersByRoleAsync(tenantId, role, cancellationToken);

    public Task<IReadOnlyList<User>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken)
        => _repo.ListUsersAsync(tenantId, cancellationToken);

    public async Task MarkSignedInAsync(Guid tenantId, Guid userId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var user = await _repo.GetUserAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return;
        }
        user.LastSignedInAt = at;
        await _repo.UpdateUserAsync(user, cancellationToken).ConfigureAwait(false);
    }
}
