using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Domain.Inbox;

public sealed class ConfirmationRequest
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid PhaseRunId { get; init; }
    public Guid RequestedByUserId { get; init; }
    public ActionClassification Classification { get; init; }
    public string ActionSummary { get; set; } = string.Empty;
    public string? TargetPath { get; set; }
    public string? CommandLine { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public ConfirmationDecision? Decision { get; set; }
}

public sealed record ConfirmationDecision(
    bool Confirmed,
    Guid DecidedByUserId,
    DateTimeOffset DecidedAt,
    string? Note);
