using AgentDesktop.Domain.Policies;

namespace AgentDesktop.Application.Abstractions;

/// <summary>
/// Boundary the policy engine uses to ask the user about a dangerous
/// action. Implemented by the UI host (Avalonia ConfirmationDialog).
/// Implementations MUST serialise concurrent prompts so two dialogs
/// are never on screen at once (FR-014 spirit).
/// </summary>
public interface IConfirmationPrompt
{
    /// <summary>
    /// Returns <c>true</c> if the user confirms the action,
    /// <c>false</c> if they decline. Throws on cancellation.
    /// </summary>
    Task<bool> ConfirmAsync(DangerousAction action, CancellationToken ct);
}
