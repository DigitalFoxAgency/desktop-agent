using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Application.Policies;

public interface IConfirmationRepository
{
    Task AddAsync(ConfirmationRequest request, CancellationToken cancellationToken);

    Task<ConfirmationRequest?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);

    Task UpdateAsync(ConfirmationRequest request, CancellationToken cancellationToken);

    Task AddDangerousActionAsync(DangerousAction action, CancellationToken cancellationToken);
}
