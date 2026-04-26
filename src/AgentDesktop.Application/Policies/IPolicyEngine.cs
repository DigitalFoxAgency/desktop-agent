using AgentDesktop.Domain.Policies;

namespace AgentDesktop.Application.Policies;

/// <summary>
/// Classifies proposed actions and orchestrates user confirmation for
/// dangerous ones. See <c>contracts/IPolicyEngine.md</c> for the
/// behavioural contract — including the rules that confirmations are
/// single-use, prompts are serialised, and unknown action kinds default
/// to dangerous (default-deny).
/// </summary>
public interface IPolicyEngine
{
    /// <summary>Evaluate a proposed dangerous action and return the resulting decision.</summary>
    Task<PolicyDecision> EvaluateAsync(DangerousAction action, CancellationToken ct);
}
