using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Runs;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Runs;

public interface IWorkflowRunRepository
{
    Task SaveNewRunAsync(
        WorkflowRun run,
        PhaseRun firstPhase,
        Assignment assignment,
        InboxItem? inboxItem,
        CancellationToken cancellationToken);

    /// <summary>Persists the completed phase update plus the next phase + assignment + optional inbox item in a single transaction. If <paramref name="nextPhase"/> is null, only the completed phase + run status update is saved.</summary>
    Task AppendNextPhaseAsync(
        PhaseRun completedPhase,
        WorkflowRun run,
        PhaseRun? nextPhase,
        Assignment? assignment,
        InboxItem? inboxItem,
        CancellationToken cancellationToken);

    Task<WorkflowRun?> GetAsync(Guid tenantId, Guid runId, CancellationToken cancellationToken);

    Task<WorkflowRun?> GetRunByPhaseAsync(Guid tenantId, Guid phaseRunId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WorkflowRun>> ListAsync(Guid tenantId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<InboxItem>> ListInboxAsync(Guid tenantId, Guid userId, int limit, CancellationToken cancellationToken);

    /// <summary>Inbox query enriched with the phase status and the parent run's id + inputs JSON, so the UI can filter active vs done items and label each row with the client.</summary>
    Task<IReadOnlyList<InboxItemWithContext>> ListInboxWithContextAsync(Guid tenantId, Guid userId, int limit, CancellationToken cancellationToken);

    Task<AssignmentLookup?> GetCurrentAssignmentAsync(Guid tenantId, Guid phaseRunId, CancellationToken cancellationToken);

    /// <summary>Persists the in-place mutation of an existing <see cref="Assignment"/> row, plus an inbox item for the new assignee, plus the run status change. The assignments table has a unique constraint on <c>PhaseRunId</c>, so reassign mutates instead of inserting a new row.</summary>
    Task ReassignAsync(
        Assignment current,
        InboxItem? newInbox,
        WorkflowRun run,
        CancellationToken cancellationToken);
}

public sealed record AssignmentLookup(Assignment Assignment, Role RequiredRole, PhaseRun Phase, WorkflowRun Run);

public sealed record InboxItemWithContext(
    InboxItem Item,
    RunStatus PhaseStatus,
    Guid WorkflowRunId,
    string ModuleId,
    string WorkflowId,
    string InputsJson);
