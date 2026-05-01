using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Tenants;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Runs;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Runs;

public sealed class PhaseAssignmentService(TenantService tenants, IClock clock)
{
    private readonly TenantService _tenants = tenants;
    private readonly IClock _clock = clock;

    public async Task<PhaseAssignmentResult> CreateAsync(
        Guid tenantId,
        PhaseRun phaseRun,
        Role requiredRole,
        string title,
        string? subtitle,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(phaseRun);

        var holders = await _tenants.FindUsersByRoleAsync(tenantId, requiredRole, cancellationToken).ConfigureAwait(false);
        var primary = holders.Count > 0 ? holders[0] : null;

        var assignment = new Assignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PhaseRunId = phaseRun.Id,
            RequiredRole = requiredRole,
            AssignedUserId = primary?.Id,
            State = primary is null ? AssignmentState.Waiting : AssignmentState.Assigned,
            CreatedAt = _clock.UtcNow,
            AssignedAt = primary is null ? null : _clock.UtcNow,
        };

        InboxItem? inbox = null;
        if (primary is not null)
        {
            inbox = new InboxItem
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = primary.Id,
                PhaseRunId = phaseRun.Id,
                Kind = InboxItemKind.PhaseAssigned,
                Title = title,
                Subtitle = subtitle,
                CreatedAt = _clock.UtcNow,
            };
        }

        return new PhaseAssignmentResult(assignment, inbox);
    }
}

public sealed record PhaseAssignmentResult(Assignment Assignment, InboxItem? InboxItem);
