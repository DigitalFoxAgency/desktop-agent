using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Bridge.ClaudeWrapper;

/// <summary>
/// Plumbing-test stand-in for the real wrapper. On <see cref="StartAsync"/> it emits a
/// scripted intro, drops a file into the working dir to trigger the tree watcher, and
/// reports synthetic token usage. <see cref="SendInputAsync"/> echoes user input back.
/// No Anthropic API call. Selected via <c>AGP_MOCK=1</c>.
/// </summary>
public sealed class MockClaudeWrapper : IClaudeWrapper
{
    private readonly Channel<ClaudeStreamEvent> _events = Channel.CreateUnbounded<ClaudeStreamEvent>();
    private readonly ILogger<MockClaudeWrapper> _log;
    private string _workingDir = "/workspace";

    public MockClaudeWrapper(ILogger<MockClaudeWrapper> log) => _log = log;

    public async Task StartAsync(ClaudeSessionSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        _workingDir = spec.WorkingDir;
        _log.LogInformation("Mock claude starting in {WorkingDir} (skill={Skill})", spec.WorkingDir, spec.Skill);

        await Emit(new ClaudeStreamEvent.TextDelta($"[mock] Hello! I'm a fake Claude. Skill: {spec.Skill}.\n")).ConfigureAwait(false);
        await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        await Emit(new ClaudeStreamEvent.TextDelta("[mock] I'll write a file so you can see the file tree update…\n")).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(spec.WorkingDir);
            var path = Path.Combine(spec.WorkingDir, "MOCK-NOTE.md");
            await File.WriteAllTextAsync(path,
                $"# Mock run\n\nSkill: {spec.Skill}\nGenerated: {DateTimeOffset.UtcNow:O}\n",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Mock could not write MOCK-NOTE.md");
        }

        await Emit(new ClaudeStreamEvent.TextDelta("[mock] Done. Type a message and I'll echo it.\n")).ConfigureAwait(false);
        await Emit(new ClaudeStreamEvent.TokenUsage("mock-sonnet-4.6", InputTokens: 42, OutputTokens: 24, CacheCreationTokens: 0, CacheReadTokens: 0)).ConfigureAwait(false);
        await Emit(new ClaudeStreamEvent.TurnComplete()).ConfigureAwait(false);
    }

    public IAsyncEnumerable<ClaudeStreamEvent> ReadEventsAsync(CancellationToken cancellationToken)
        => _events.Reader.ReadAllAsync(cancellationToken);

    public async Task SendInputAsync(string text, CancellationToken cancellationToken)
    {
        await Emit(new ClaudeStreamEvent.TextDelta($"[mock] you said: {text}\n")).ConfigureAwait(false);
        await Emit(new ClaudeStreamEvent.TokenUsage("mock-sonnet-4.6", InputTokens: text.Length, OutputTokens: text.Length + 20, CacheCreationTokens: 0, CacheReadTokens: 0)).ConfigureAwait(false);
        await Emit(new ClaudeStreamEvent.TurnComplete()).ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _events.Writer.TryWrite(new ClaudeStreamEvent.SessionExited(0));
        _events.Writer.TryComplete();
        return Task.CompletedTask;
    }

    private async ValueTask Emit(ClaudeStreamEvent evt) => await _events.Writer.WriteAsync(evt).ConfigureAwait(false);
}
