using System.Threading.Channels;
using AgentPlatform.Application.Bridge;

namespace AgentPlatform.Contracts.Tests.Fakes;

public sealed class FakeBridgeChannel : IBridgeChannel
{
    private readonly Channel<BridgeEvent> _events = Channel.CreateUnbounded<BridgeEvent>();
    private readonly List<string> _userInputs = new();
    private readonly List<(Guid Id, bool Confirmed, string? Note)> _decisions = new();
    private bool _closed;

    public IReadOnlyList<string> UserInputs
    {
        get { lock (_userInputs) { return _userInputs.ToArray(); } }
    }

    public IReadOnlyList<(Guid Id, bool Confirmed, string? Note)> Decisions
    {
        get { lock (_decisions) { return _decisions.ToArray(); } }
    }

    public bool IsClosed => _closed;

    public ValueTask EmitAsync(BridgeEvent evt, CancellationToken cancellationToken = default)
        => _events.Writer.WriteAsync(evt, cancellationToken);

    public IAsyncEnumerable<BridgeEvent> ReadEventsAsync(CancellationToken cancellationToken)
        => _events.Reader.ReadAllAsync(cancellationToken);

    public Task SendUserInputAsync(string text, CancellationToken cancellationToken)
    {
        lock (_userInputs) { _userInputs.Add(text); }
        return Task.CompletedTask;
    }

    public Task ResolveConfirmationAsync(Guid confirmationId, bool confirmed, string? note, CancellationToken cancellationToken)
    {
        lock (_decisions) { _decisions.Add((confirmationId, confirmed, note)); }
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        _closed = true;
        _events.Writer.TryComplete();
        return Task.CompletedTask;
    }
}
