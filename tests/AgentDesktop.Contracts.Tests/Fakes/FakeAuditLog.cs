using AgentDesktop.Application.Abstractions;
using AgentDesktop.Domain.Policies;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>In-memory audit log used by tests. Records the order of every call.</summary>
public sealed class FakeAuditLog : IAuditLog
{
    private readonly List<DangerousAction> _proposed = new();
    private readonly List<PolicyDecision> _decided = new();
    private readonly List<PolicyDecision> _executed = new();
    private readonly object _gate = new();

    public IReadOnlyList<DangerousAction> Proposed
    {
        get { lock (_gate) { return _proposed.ToList(); } }
    }

    public IReadOnlyList<PolicyDecision> Decided
    {
        get { lock (_gate) { return _decided.ToList(); } }
    }

    public IReadOnlyList<PolicyDecision> Executed
    {
        get { lock (_gate) { return _executed.ToList(); } }
    }

    public Task RecordProposedAsync(DangerousAction action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        lock (_gate) { _proposed.Add(action); }
        return Task.CompletedTask;
    }

    public Task RecordDecisionAsync(PolicyDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate) { _decided.Add(decision); }
        return Task.CompletedTask;
    }

    public Task RecordExecutionAsync(PolicyDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate) { _executed.Add(decision); }
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<PolicyDecision> EnumerateAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        List<PolicyDecision> snapshot;
        lock (_gate) { snapshot = _decided.ToList(); }
        snapshot.Reverse();
        foreach (var d in snapshot)
        {
            ct.ThrowIfCancellationRequested();
            yield return d;
            await Task.Yield();
        }
    }
}
