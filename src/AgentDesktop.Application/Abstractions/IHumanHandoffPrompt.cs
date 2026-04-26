namespace AgentDesktop.Application.Abstractions;

/// <summary>
/// Boundary the delegation runner uses when a module emits
/// <see cref="Runtime.IDelegationCallbacks.RequestHumanHandoffAsync"/>.
/// Implemented by the UI host (Avalonia <c>HumanHandoffDialog</c>).
/// </summary>
public interface IHumanHandoffPrompt
{
    /// <summary>
    /// Show the hand-off instructions and wait for the user to mark
    /// the step done. Throws on cancellation.
    /// </summary>
    Task WaitForCompletionAsync(string stepName, string instructions, CancellationToken ct);
}
