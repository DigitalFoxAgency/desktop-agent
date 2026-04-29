using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Application.Bridge;

public interface IBridgeChannel
{
    IAsyncEnumerable<BridgeEvent> ReadEventsAsync(CancellationToken cancellationToken);

    Task SendUserInputAsync(string text, CancellationToken cancellationToken);

    Task ResolveConfirmationAsync(Guid confirmationId, bool confirmed, string? note, CancellationToken cancellationToken);

    Task CloseAsync(CancellationToken cancellationToken);
}

public abstract record BridgeEvent(DateTimeOffset OccurredAt)
{
    public sealed record AssistantChunk(string Text, DateTimeOffset OccurredAt) : BridgeEvent(OccurredAt);

    public sealed record AssistantTurnComplete(DateTimeOffset OccurredAt) : BridgeEvent(OccurredAt);

    public sealed record FileChanged(string RelativePath, FileChangeKind Kind, DateTimeOffset OccurredAt) : BridgeEvent(OccurredAt);

    public sealed record ConfirmationRequested(
        Guid ConfirmationId,
        ActionClassification Classification,
        string Summary,
        string? TargetPath,
        string? CommandLine,
        DateTimeOffset OccurredAt) : BridgeEvent(OccurredAt);

    public sealed record TokenUsage(
        string Model,
        long InputTokens,
        long OutputTokens,
        long CacheCreationTokens,
        long CacheReadTokens,
        DateTimeOffset OccurredAt) : BridgeEvent(OccurredAt);

    public sealed record PhaseCompleted(string Skill, bool Verified, DateTimeOffset OccurredAt) : BridgeEvent(OccurredAt);
}

public enum FileChangeKind
{
    Created = 0,
    Modified = 1,
    Deleted = 2,
}
