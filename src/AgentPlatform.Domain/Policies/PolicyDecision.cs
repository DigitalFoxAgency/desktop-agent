namespace AgentPlatform.Domain.Policies;

public sealed record PolicyDecision(
    ActionClassification Classification,
    bool RequiresConfirmation,
    bool RequiresBuildSemaphore,
    string Reason);
