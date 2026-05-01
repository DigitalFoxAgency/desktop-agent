using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Domain.Policies;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Bridge;

public sealed class WebSocketBridgeChannel : IBridgeChannel, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly WebSocket _socket;
    private readonly ILogger<WebSocketBridgeChannel> _log;
    private readonly object _subLock = new();
    private readonly List<Channel<BridgeEvent>> _subscribers = [];
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _readerTask;
    private bool _disposed;

    public WebSocketBridgeChannel(WebSocket socket, ILogger<WebSocketBridgeChannel> log)
    {
        _socket = socket;
        _log = log;
        _readerTask = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    public Task ReaderCompletion => _readerTask;

    public async IAsyncEnumerable<BridgeEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Per-subscriber channel so multiple consumers (e.g. PhaseSessionService's
        // usage pump and PhaseSessionHub's browser fan-out) each see every event.
        var sub = Channel.CreateUnbounded<BridgeEvent>();
        lock (_subLock) { _subscribers.Add(sub); }
        try
        {
            await foreach (var evt in sub.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            lock (_subLock) { _subscribers.Remove(sub); }
            sub.Writer.TryComplete();
        }
    }

    private void Broadcast(BridgeEvent evt)
    {
        Channel<BridgeEvent>[] snapshot;
        lock (_subLock) { snapshot = _subscribers.ToArray(); }
        foreach (var sub in snapshot)
        {
            sub.Writer.TryWrite(evt);
        }
    }

    public Task SendUserInputAsync(string text, CancellationToken cancellationToken)
        => SendFrameAsync(new BridgeFrame { Type = "user_input", Text = text }, cancellationToken);

    public Task ResolveConfirmationAsync(Guid confirmationId, bool confirmed, string? note, CancellationToken cancellationToken)
        => SendFrameAsync(new BridgeFrame
        {
            Type = "confirmation_decision",
            ConfirmationId = confirmationId,
            Confirmed = confirmed,
            Note = note,
        }, cancellationToken);

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "phase closed", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (WebSocketException) { /* ignore */ }
        await _cts.CancelAsync().ConfigureAwait(false);
        Channel<BridgeEvent>[] snapshot;
        lock (_subLock) { snapshot = _subscribers.ToArray(); _subscribers.Clear(); }
        foreach (var sub in snapshot) { sub.Writer.TryComplete(); }
    }

    private async Task SendFrameAsync(BridgeFrame frame, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return;
        }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally { _sendLock.Release(); }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }
                var json = sb.ToString();
                sb.Clear();
                TryDispatch(json);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bridge WS read loop ended");
        }
        finally
        {
            Channel<BridgeEvent>[] snapshot;
            lock (_subLock) { snapshot = _subscribers.ToArray(); _subscribers.Clear(); }
            foreach (var sub in snapshot) { sub.Writer.TryComplete(); }
        }
    }

    private void TryDispatch(string json)
    {
        BridgeFrame? frame;
        try
        {
            frame = JsonSerializer.Deserialize<BridgeFrame>(json, Json);
        }
        catch (JsonException ex)
        {
            _log.LogWarning(ex, "Failed to parse bridge frame: {Json}", json);
            return;
        }

        if (frame is null)
        {
            return;
        }
        var now = DateTimeOffset.UtcNow;
        BridgeEvent? evt = frame.Type switch
        {
            "assistant_chunk" => new BridgeEvent.AssistantChunk(frame.Text ?? string.Empty, now),
            "assistant_turn_complete" => new BridgeEvent.AssistantTurnComplete(now),
            "file_changed" => new BridgeEvent.FileChanged(frame.Path ?? string.Empty, ParseKind(frame.Kind), now),
            "confirmation_request" => new BridgeEvent.ConfirmationRequested(
                frame.ConfirmationId ?? Guid.NewGuid(),
                ParseClassification(frame.Classification),
                frame.Summary ?? string.Empty,
                frame.TargetPath,
                frame.CommandLine,
                now),
            "token_usage" => new BridgeEvent.TokenUsage(
                frame.Model ?? "unknown",
                frame.InputTokens ?? 0,
                frame.OutputTokens ?? 0,
                frame.CacheCreationTokens ?? 0,
                frame.CacheReadTokens ?? 0,
                now),
            "phase_completed" => new BridgeEvent.PhaseCompleted(frame.Skill ?? string.Empty, frame.Verified ?? false, now),
            _ => null,
        };
        if (evt is not null)
        {
            Broadcast(evt);
        }
    }

    private static FileChangeKind ParseKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "created" => FileChangeKind.Created,
        "deleted" => FileChangeKind.Deleted,
        _ => FileChangeKind.Modified,
    };

    private static ActionClassification ParseClassification(string? c) => c?.ToLowerInvariant() switch
    {
        "deletefile" => ActionClassification.DeleteFile,
        "gitpush" => ActionClassification.GitPush,
        "installpackage" => ActionClassification.InstallPackage,
        "runshell" => ActionClassification.RunShell,
        "buildclass" => ActionClassification.BuildClass,
        _ => ActionClassification.RunShell,
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _cts.Cancel();
        _socket.Dispose();
        _cts.Dispose();
        _sendLock.Dispose();
    }

    private sealed class BridgeFrame
    {
        public string Type { get; set; } = string.Empty;
        public string? Text { get; set; }
        public string? Path { get; set; }
        public string? Kind { get; set; }
        public Guid? ConfirmationId { get; set; }
        public string? Classification { get; set; }
        public string? Summary { get; set; }
        public string? TargetPath { get; set; }
        public string? CommandLine { get; set; }
        public bool? Confirmed { get; set; }
        public string? Note { get; set; }
        public string? Model { get; set; }
        public long? InputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? CacheCreationTokens { get; set; }
        public long? CacheReadTokens { get; set; }
        public string? Skill { get; set; }
        public bool? Verified { get; set; }
    }
}
