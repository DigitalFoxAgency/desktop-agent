using AgentPlatform.Application.Tenants;
using AgentPlatform.Domain.Tenants;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

public sealed class PostgresTenantRepository : ITenantRepository
{
    private readonly AgentPlatformDbContext _db;

    public PostgresTenantRepository(AgentPlatformDbContext db) => _db = db;

    public async Task<Tenant> AddTenantAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return tenant;
    }

    public Task<Tenant?> GetTenantBySlugAsync(string slug, CancellationToken cancellationToken)
        => _db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);

    public Task<Tenant?> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken)
        => _db.Tenants.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);

    public async Task AddUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<User?> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
        => _db.Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == userId, cancellationToken);

    public async Task<IReadOnlyList<User>> ListUsersAsync(Guid tenantId, CancellationToken cancellationToken)
        => await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task AssignRoleAsync(UserRole userRole, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userRole);
        var exists = await _db.UserRoles.IgnoreQueryFilters()
            .AnyAsync(r => r.TenantId == userRole.TenantId && r.UserId == userRole.UserId && r.Role == userRole.Role, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            return;
        }
        _db.UserRoles.Add(userRole);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<Role>> GetRolesAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
    {
        var rows = await _db.UserRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.UserId == userId)
            .Select(r => r.Role)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return rows;
    }

    public async Task<IReadOnlyList<User>> FindUsersByRoleAsync(Guid tenantId, Role role, CancellationToken cancellationToken)
    {
        var userIds = _db.UserRoles.IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId && r.Role == role)
            .Select(r => r.UserId);

        return await _db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.TenantId == tenantId && userIds.Contains(u.Id))
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateUserAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        _db.Users.Update(user);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
