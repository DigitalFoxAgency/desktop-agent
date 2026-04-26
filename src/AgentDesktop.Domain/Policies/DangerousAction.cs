namespace AgentDesktop.Domain.Policies;

/// <summary>
/// A side-effect the agent is about to perform that the policy engine
/// has classified as Dangerous. Each one is a unit the user confirms
/// (or declines) exactly once — confirmation never carries over to a
/// different occurrence (FR-013).
/// </summary>
public sealed record DangerousAction
{
    public DangerousAction(
        PolicyDecisionId id,
        DangerousActionKind kind,
        string target,
        PolicyOrigin origin,
        DateTimeOffset requestedAt)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(origin);

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Target cannot be empty.", nameof(target));
        }

        Id = id;
        Kind = kind;
        Target = target;
        Origin = origin;
        RequestedAt = requestedAt;
    }

    public PolicyDecisionId Id { get; }
    public DangerousActionKind Kind { get; }

    /// <summary>Human-readable target (path, repo URL, package, command).</summary>
    public string Target { get; }

    public PolicyOrigin Origin { get; }
    public DateTimeOffset RequestedAt { get; }
}
