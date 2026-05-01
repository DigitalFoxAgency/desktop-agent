using System.Collections.Concurrent;

namespace AgentPlatform.Bridge.PolicyBridge;

/// <summary>
/// Tracks outstanding confirmation requests by id and exposes a <see
/// cref="WaitForDecisionAsync"/> hook the interceptor blocks on. Decisions
/// flowing back from the API resolve the matching pending task. One entry per
/// proposed action — confirming once does not implicitly confirm subsequent
/// requests.
/// </summary>
public sealed class ConfirmationGate
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<ConfirmationOutcome>> _pending = new();

    public int OutstandingCount => _pending.Count;

    public Guid Register()
    {
        var id = Guid.NewGuid();
        _pending[id] = new TaskCompletionSource<ConfirmationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        return id;
    }

    public async Task<ConfirmationOutcome> WaitForDecisionAsync(Guid confirmationId, CancellationToken cancellationToken)
    {
        if (!_pending.TryGetValue(confirmationId, out var tcs))
        {
            throw new InvalidOperationException($"No pending confirmation for {confirmationId}");
        }

        using var reg = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(confirmationId, out _);
        }
    }

    public bool Resolve(Guid confirmationId, bool confirmed, string? note)
    {
        if (!_pending.TryGetValue(confirmationId, out var tcs))
        {
            return false;
        }
        return tcs.TrySetResult(new ConfirmationOutcome(confirmed, note));
    }
}

public sealed record ConfirmationOutcome(bool Confirmed, string? Note);
