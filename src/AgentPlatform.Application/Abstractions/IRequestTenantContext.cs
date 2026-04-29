using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Abstractions;

public interface IRequestTenantContext
{
    Guid TenantId { get; }
    Guid UserId { get; }
    IReadOnlyCollection<Role> Roles { get; }
    bool TryGetTenantId(out Guid tenantId);
}
