using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace AgentPlatform.Infrastructure.Inbox;

/// <summary>
/// Singleton registry of open <c>/ws/inbox</c> WebSockets, keyed by user. The hub
/// adds connections on accept and removes them on close; the
/// <see cref="WebSocketInboxNotifier"/> uses it to fan inbox events out.
/// </summary>
public sealed class InboxConnectionRegistry
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, WebSocket>> _byUser = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _sendLocks = new();

    public IDisposable Register(Guid userId, WebSocket socket)
    {
        var connId = Guid.NewGuid();
        var bucket = _byUser.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, WebSocket>());
        bucket[connId] = socket;
        _sendLocks[connId] = new SemaphoreSlim(1, 1);
        return new Registration(this, userId, connId);
    }

    public async Task SendAsync(Guid userId, object payload, CancellationToken cancellationToken)
    {
        if (!_byUser.TryGetValue(userId, out var bucket) || bucket.IsEmpty) { return; }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var dead = new List<Guid>();
        foreach (var (id, socket) in bucket)
        {
            if (socket.State != WebSocketState.Open)
            {
                dead.Add(id);
                continue;
            }
            if (!_sendLocks.TryGetValue(id, out var gate)) { continue; }
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch (WebSocketException) { dead.Add(id); }
            catch (ObjectDisposedException) { dead.Add(id); }
            finally { gate.Release(); }
        }
        foreach (var id in dead) { Remove(userId, id); }
    }

    private void Remove(Guid userId, Guid connId)
    {
        if (_byUser.TryGetValue(userId, out var bucket))
        {
            bucket.TryRemove(connId, out _);
            if (bucket.IsEmpty) { _byUser.TryRemove(userId, out _); }
        }
        if (_sendLocks.TryRemove(connId, out var gate)) { gate.Dispose(); }
    }

    private sealed class Registration(InboxConnectionRegistry owner, Guid userId, Guid connId) : IDisposable
    {
        private readonly InboxConnectionRegistry _owner = owner;
        private readonly Guid _userId = userId;
        private readonly Guid _connId = connId;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            _owner.Remove(_userId, _connId);
        }
    }
}
