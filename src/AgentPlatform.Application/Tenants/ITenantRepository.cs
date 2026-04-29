using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Tenants;

public interface ITenantRepository
{
    Task<Tenant> AddTenantAsync(Tenant tenant, CancellationToken cancellationToken);

    Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken);

    Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AddUserAsync(User user, CancellationToken cancellationToken);

    Task<User?> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<User>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken);

    Task AssignRoleAsync(UserRole userRole, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<Role>> GetRolesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<User>> FindUsersByRoleAsync(Guid tenantId, Role role, CancellationToken cancellationToken);

    Task UpdateUserAsync(User user, CancellationToken cancellationToken);
}
