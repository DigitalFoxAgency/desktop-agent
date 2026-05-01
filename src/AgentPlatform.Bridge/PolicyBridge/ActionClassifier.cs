using AgentPlatform.Application.Policies;
using AgentPlatform.Bridge.ClaudeWrapper;
using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Bridge.PolicyBridge;

/// <summary>
/// Translates a <see cref="ClaudeStreamEvent.ToolUseProposed"/> into an
/// <see cref="IntentDescriptor"/>, runs it through <see cref="IPolicyEngine"/>,
/// and surfaces a <see cref="PolicyDecision"/>. Stateless on purpose — every
/// proposed tool-use produces a fresh classification.
/// </summary>
public sealed class ActionClassifier(IPolicyEngine policy, string? moduleId = null, string? phaseId = null)
{
    private readonly IPolicyEngine _policy = policy;
    private readonly string? _moduleId = moduleId;
    private readonly string? _phaseId = phaseId;

    public Task<PolicyDecision> ClassifyAsync(ClaudeStreamEvent.ToolUseProposed proposed, CancellationToken cancellationToken)
    {
        var intent = new IntentDescriptor(proposed.ToolName, proposed.CommandLine, proposed.TargetPath, _moduleId, _phaseId);
        return _policy.ClassifyAsync(intent, cancellationToken);
    }
}
