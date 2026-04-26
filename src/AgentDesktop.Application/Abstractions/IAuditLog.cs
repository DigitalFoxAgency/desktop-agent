using AgentDesktop.Domain.Policies;

namespace AgentDesktop.Application.Abstractions;

/// <summary>
/// Append-only audit log. Records every <see cref="PolicyDecision"/>
/// and supporting transitions per FR-022. Implementations live in
/// Infrastructure (SQLite-backed).
/// </summary>
public interface IAuditLog
{
    /// <summary>Record that a dangerous action was proposed (created).</summary>
    Task RecordProposedAsync(DangerousAction action, CancellationToken ct);

    /// <summary>Record the user's decision (Confirmed / Declined / Skipped / Expired).</summary>
    Task RecordDecisionAsync(PolicyDecision decision, CancellationToken ct);

    /// <summary>Record the post-execution outcome (Succeeded / Failed) for a confirmed decision.</summary>
    Task RecordExecutionAsync(PolicyDecision decision, CancellationToken ct);

    /// <summary>Read all decisions in reverse-chronological order. Used by the audit-review surface.</summary>
    IAsyncEnumerable<PolicyDecision> EnumerateAsync(CancellationToken ct);
}
