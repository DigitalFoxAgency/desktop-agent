namespace AgentPlatform.Domain.Inbox;

public sealed class InboxItem
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid UserId { get; init; }
    public Guid PhaseRunId { get; init; }
    public InboxItemKind Kind { get; init; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? OpenedAt { get; set; }
    public DateTimeOffset? DismissedAt { get; set; }
}

public enum InboxItemKind
{
    PhaseAssigned = 0,
    ConfirmationPending = 1,
    RunPaused = 2,
}
