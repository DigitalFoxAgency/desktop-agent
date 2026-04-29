using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Bridge.Transport;

/// <summary>
/// WebSocket transport between Bridge and API. Phase 2: skeleton with connect/send/receive
/// primitives. Phase 4 (T102) wires it to the wrapper streams + file watcher.
/// </summary>
public sealed class ApiBridgeClient : IAsyncDisposable
{
    private readonly Uri _endpoint;
    private readonly string _token;
    private readonly ILogger<ApiBridgeClient> _log;
    private readonly ClientWebSocket _ws = new();
    private bool _disposed;

    public ApiBridgeClient(Uri endpoint, string token, ILogger<ApiBridgeClient> log)
    {
        _endpoint = endpoint;
        _token = token;
        _log = log;
        _ws.Options.SetRequestHeader("Authorization", $"Bearer {token}");
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("Bridge connecting to {Endpoint}", _endpoint);
        await _ws.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);
    }

    public async Task SendAsync<T>(T payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _ws.SendAsync(json, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<string> ReadFramesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var sb = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested && _ws.State == WebSocketState.Open)
        {
            var result = await _ws.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                yield break;
            }
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
            }
            catch (WebSocketException) { /* ignore */ }
        }
        _ws.Dispose();
    }
}
