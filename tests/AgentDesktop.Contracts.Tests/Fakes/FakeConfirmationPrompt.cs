using System.Collections.Concurrent;
using AgentDesktop.Application.Abstractions;
using AgentDesktop.Domain.Policies;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>
/// Programmable confirmation prompt. Tests pre-program the response
/// for an action's <see cref="DangerousAction.Kind"/>; the fake
/// asserts at end of test that no two prompts overlapped (FR-014
/// serialisation property).
/// </summary>
public sealed class FakeConfirmationPrompt : IConfirmationPrompt
{
    private readonly ConcurrentQueue<bool> _responses = new();
    private int _inFlight;

    public List<DangerousAction> Asked { get; } = new();

    public bool MaxConcurrentExceeded { get; private set; }

    /// <summary>If true, the next <see cref="ConfirmAsync"/> call throws <see cref="OperationCanceledException"/>.</summary>
    public bool CancelOnNextRequest { get; set; }

    public void EnqueueResponse(bool confirm) => _responses.Enqueue(confirm);

    public async Task<bool> ConfirmAsync(DangerousAction action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);

        var was = Interlocked.Increment(ref _inFlight);
        if (was > 1)
        {
            MaxConcurrentExceeded = true;
        }

        try
        {
            // Yield once so concurrent callers really do interleave when
            // the engine fails to serialise correctly — surfacing bugs.
            await Task.Yield();

            lock (Asked)
            {
                Asked.Add(action);
            }

            if (CancelOnNextRequest)
            {
                CancelOnNextRequest = false;
                throw new OperationCanceledException("Cancellation injected by FakeConfirmationPrompt for the test.");
            }

            if (!_responses.TryDequeue(out var response))
            {
                throw new InvalidOperationException(
                    $"FakeConfirmationPrompt has no programmed response for {action.Kind} on '{action.Target}'.");
            }

            return response;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}
