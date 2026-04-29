namespace AgentPlatform.Bridge.ClaudeWrapper;

public interface IClaudeWrapper
{
    Task StartAsync(ClaudeSessionSpec spec, CancellationToken cancellationToken);

    IAsyncEnumerable<ClaudeStreamEvent> ReadEventsAsync(CancellationToken cancellationToken);

    Task SendInputAsync(string text, CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}

public sealed record ClaudeSessionSpec(
    string WorkingDir,
    string Skill,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Environment);

public abstract record ClaudeStreamEvent
{
    public sealed record TextDelta(string Text) : ClaudeStreamEvent;

    public sealed record ToolUseProposed(
        string ToolName,
        string? CommandLine,
        string? TargetPath,
        string RawJson) : ClaudeStreamEvent;

    public sealed record ToolUseResult(string ToolName, bool Success, string? OutputText) : ClaudeStreamEvent;

    public sealed record TurnComplete : ClaudeStreamEvent;

    public sealed record TokenUsage(
        string Model,
        long InputTokens,
        long OutputTokens,
        long CacheCreationTokens,
        long CacheReadTokens) : ClaudeStreamEvent;

    public sealed record SessionExited(int ExitCode) : ClaudeStreamEvent;
}
