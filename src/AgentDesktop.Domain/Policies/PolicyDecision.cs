namespace AgentDesktop.Domain.Policies;

/// <summary>
/// Persisted outcome of a single policy evaluation. Created the moment
/// the user resolves the confirmation prompt (or it expires); the
/// execution-result fields are filled in after the action completes.
/// Audit-log invariants live here — see FR-022 mapping in
/// data-model.md.
/// </summary>
public sealed record PolicyDecision
{
    public PolicyDecision(
        PolicyDecisionId id,
        DangerousAction action,
        PolicyOutcome outcome,
        DateTimeOffset decidedAt,
        PolicyExecutionResult? executionResult = null,
        string? executionError = null)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (id != action.Id)
        {
            throw new ArgumentException(
                "PolicyDecision.Id must match its DangerousAction.Id.",
                nameof(id));
        }

        if (decidedAt < action.RequestedAt)
        {
            throw new ArgumentException(
                "DecidedAt cannot precede the action's RequestedAt.",
                nameof(decidedAt));
        }

        if (executionResult == PolicyExecutionResult.Failed && string.IsNullOrWhiteSpace(executionError))
        {
            throw new ArgumentException(
                "ExecutionError must be set when ExecutionResult is Failed.",
                nameof(executionError));
        }

        if (executionResult is null && executionError is not null)
        {
            throw new ArgumentException(
                "ExecutionError requires a non-null ExecutionResult.",
                nameof(executionError));
        }

        if (outcome != PolicyOutcome.Confirmed && executionResult is not null)
        {
            throw new ArgumentException(
                "Only Confirmed decisions can carry an ExecutionResult.",
                nameof(executionResult));
        }

        Id = id;
        Action = action;
        Outcome = outcome;
        DecidedAt = decidedAt;
        ExecutionResult = executionResult;
        ExecutionError = executionError;
    }

    public PolicyDecisionId Id { get; }
    public DangerousAction Action { get; }
    public PolicyOutcome Outcome { get; }
    public DateTimeOffset DecidedAt { get; }
    public PolicyExecutionResult? ExecutionResult { get; }
    public string? ExecutionError { get; }

    /// <summary>Returns a copy with the execution result attached. Used by the policy engine after the action has run.</summary>
    public PolicyDecision WithExecutionResult(PolicyExecutionResult result, string? error = null) =>
        new(Id, Action, Outcome, DecidedAt, result, error);
}
