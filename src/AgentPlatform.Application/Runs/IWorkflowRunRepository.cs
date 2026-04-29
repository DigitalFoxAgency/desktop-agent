using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Runs;

namespace AgentPlatform.Application.Runs;

public interface IWorkflowRunRepository
{
    Task SaveNewRunAsync(
        WorkflowRun run,
        PhaseRun firstPhase,
        Assignment assignment,
        InboxItem? inboxItem,
        CancellationToken cancellationToken);

    Task<WorkflowRun?> GetAsync(Guid tenantId, Guid runId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowRun>> ListAsync(Guid tenantId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<InboxItem>> ListInboxAsync(Guid tenantId, Guid userId, int limit, CancellationToken cancellationToken);
}
