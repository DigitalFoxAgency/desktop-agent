using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Application.Policies;

public interface IPolicyEngine
{
    Task<PolicyDecision> ClassifyAsync(IntentDescriptor intent, CancellationToken cancellationToken);
}

public sealed record IntentDescriptor(
    string Tool,
    string? CommandLine,
    string? TargetPath,
    string? ModuleId,
    string? PhaseId);
