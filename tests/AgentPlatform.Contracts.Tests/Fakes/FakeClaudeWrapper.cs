using System.Threading.Channels;

namespace AgentPlatform.Contracts.Tests.Fakes;

/// <summary>
/// Wrapper-shaped fake. We don't reference AgentPlatform.Bridge here (Contracts.Tests is
/// app-layer); this fake matches the shape of the events the wrapper emits so it can
/// drive bridge-channel tests via a tiny adapter.
/// </summary>
public sealed class FakeClaudeWrapper
{
    private readonly Channel<FakeClaudeEvent> _events = Channel.CreateUnbounded<FakeClaudeEvent>();
    private readonly List<string> _inputs = new();
    private readonly List<FakeClaudeEvent> _scripted = new();

    public IReadOnlyList<string> Inputs
    {
        get { lock (_inputs) { return _inputs.ToArray(); } }
    }

    public FakeClaudeWrapper Script(params FakeClaudeEvent[] events)
    {
        _scripted.AddRange(events);
        return this;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var evt in _scripted)
        {
            _events.Writer.TryWrite(evt);
        }
        _events.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public IAsyncEnumerable<FakeClaudeEvent> ReadEventsAsync(CancellationToken cancellationToken)
        => _events.Reader.ReadAllAsync(cancellationToken);

    public Task SendInputAsync(string text, CancellationToken cancellationToken)
    {
        lock (_inputs) { _inputs.Add(text); }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _events.Writer.TryComplete();
        return Task.CompletedTask;
    }
}

public abstract record FakeClaudeEvent
{
    public sealed record TextDelta(string Text) : FakeClaudeEvent;
    public sealed record ToolUseProposed(string ToolName, string? CommandLine, string? TargetPath) : FakeClaudeEvent;
    public sealed record TokenUsage(string Model, long InputTokens, long OutputTokens) : FakeClaudeEvent;
    public sealed record TurnComplete() : FakeClaudeEvent;
    public sealed record SessionExited(int ExitCode) : FakeClaudeEvent;
}
