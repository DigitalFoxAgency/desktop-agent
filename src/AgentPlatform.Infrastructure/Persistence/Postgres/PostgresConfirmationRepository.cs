using AgentPlatform.Application.Policies;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Policies;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

public sealed class PostgresConfirmationRepository(AgentPlatformDbContext db) : IConfirmationRepository
{
    private readonly AgentPlatformDbContext _db = db;

    public async Task AddAsync(ConfirmationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _db.ConfirmationRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ConfirmationRequest?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken)
        => await _db.ConfirmationRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

    public async Task UpdateAsync(ConfirmationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _db.ConfirmationRequests.Update(request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task AddDangerousActionAsync(DangerousAction action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        _db.DangerousActions.Add(action);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
