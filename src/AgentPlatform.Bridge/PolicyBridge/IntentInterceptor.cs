using AgentPlatform.Application.RunContainers;
using AgentPlatform.Bridge.ClaudeWrapper;
using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Bridge.PolicyBridge;

/// <summary>
/// Hook invoked for every <see cref="ClaudeStreamEvent.ToolUseProposed"/>
/// emitted by the wrapper. Classifies the proposed action and returns the
/// frame the BridgeRuntime should send next: either a passthrough
/// <c>tool_use</c> notification (safe action) or a <c>confirmation_request</c>
/// (dangerous action — bridge must wait for the API's decision before
/// proceeding).
/// </summary>
public sealed class IntentInterceptor(
    ActionClassifier classifier,
    ConfirmationGate gate,
    BuildStepSemaphore? buildSemaphore = null)
{
    private readonly ActionClassifier _classifier = classifier;
    private readonly ConfirmationGate _gate = gate;
    private readonly BuildStepSemaphore? _buildSemaphore = buildSemaphore;

    public async Task<InterceptionResult> InspectAsync(
        ClaudeStreamEvent.ToolUseProposed proposed,
        CancellationToken cancellationToken)
    {
        var decision = await _classifier.ClassifyAsync(proposed, cancellationToken).ConfigureAwait(false);

        if (!decision.RequiresConfirmation)
        {
            return InterceptionResult.Forward(decision);
        }

        var confirmationId = _gate.Register();
        var summary = BuildSummary(decision.Classification, proposed);
        return InterceptionResult.RequiresConfirmation(
            confirmationId,
            decision,
            summary,
            proposed.TargetPath,
            proposed.CommandLine);
    }

    /// <summary>Acquires the build-step semaphore if the policy requires it. Caller disposes the returned handle on completion.</summary>
    public async Task<IDisposable?> AcquireBuildSlotAsync(PolicyDecision decision, CancellationToken cancellationToken)
    {
        if (!decision.RequiresBuildSemaphore || _buildSemaphore is null)
        {
            return null;
        }
        return await _buildSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string BuildSummary(ActionClassification classification, ClaudeStreamEvent.ToolUseProposed proposed)
        => classification switch
        {
            ActionClassification.DeleteFile => $"Delete file: {proposed.TargetPath ?? proposed.CommandLine ?? "(unknown)"}",
            ActionClassification.GitPush => $"Push to remote: {proposed.CommandLine ?? "git push"}",
            ActionClassification.InstallPackage => $"Install package: {proposed.CommandLine ?? "(unspecified)"}",
            ActionClassification.BuildClass => $"Run build step: {proposed.CommandLine ?? "(unspecified)"}",
            ActionClassification.RunShell => $"Run shell command: {proposed.CommandLine ?? "(unspecified)"}",
            _ => proposed.ToolName,
        };
}

public sealed record InterceptionResult(
    bool ConfirmationRequired,
    PolicyDecision Decision,
    Guid? ConfirmationId,
    string? Summary,
    string? TargetPath,
    string? CommandLine)
{
    public static InterceptionResult Forward(PolicyDecision decision)
        => new(false, decision, null, null, null, null);

    public static InterceptionResult RequiresConfirmation(
        Guid id,
        PolicyDecision decision,
        string summary,
        string? targetPath,
        string? commandLine)
        => new(true, decision, id, summary, targetPath, commandLine);
}
