using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using AgentPlatform.Application.Bridge;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.Bridge;

public sealed class BridgeConnectionRegistry : IBridgeChannelFactory, IDisposable
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<WebSocketBridgeChannel>> _waiters = new();
    private readonly ConcurrentDictionary<string, BridgeTokenBinding> _tokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, WebSocketBridgeChannel> _channels = new();
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BridgeConnectionRegistry> _log;

    public BridgeConnectionRegistry(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _log = loggerFactory.CreateLogger<BridgeConnectionRegistry>();
    }

    public string IssueConnectToken(Guid phaseRunId, Guid tenantId)
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToHexString(bytes);
        _tokens[token] = new BridgeTokenBinding(phaseRunId, tenantId, DateTimeOffset.UtcNow);
        return token;
    }

    public bool TryConsumeToken(string token, out Guid phaseRunId, out Guid tenantId)
    {
        phaseRunId = Guid.Empty;
        tenantId = Guid.Empty;
        if (string.IsNullOrEmpty(token) || !_tokens.TryRemove(token, out var binding))
        {
            return false;
        }
        if (DateTimeOffset.UtcNow - binding.IssuedAt > TimeSpan.FromMinutes(5))
        {
            return false;
        }
        phaseRunId = binding.PhaseRunId;
        tenantId = binding.TenantId;
        return true;
    }

    public WebSocketBridgeChannel RegisterConnection(Guid phaseRunId, Guid tenantId, WebSocket socket)
    {
        var channel = new WebSocketBridgeChannel(socket, _loggerFactory.CreateLogger<WebSocketBridgeChannel>());
        _channels[phaseRunId] = channel;
        if (_waiters.TryRemove(phaseRunId, out var tcs))
        {
            tcs.TrySetResult(channel);
        }
        _log.LogInformation("Bridge connected for phase {PhaseRunId}", phaseRunId);
        return channel;
    }

    public async Task<IBridgeChannel> WaitForConnectionAsync(Guid phaseRunId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_channels.TryGetValue(phaseRunId, out var existing))
        {
            return existing;
        }

        var tcs = _waiters.GetOrAdd(phaseRunId, _ => new TaskCompletionSource<WebSocketBridgeChannel>(TaskCreationOptions.RunContinuationsAsynchronously));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        await using var registration = linked.Token.Register(() => tcs.TrySetCanceled(linked.Token)).ConfigureAwait(false);

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Bridge did not connect for phase {phaseRunId} within {timeout}.");
        }
    }

    public void Forget(Guid phaseRunId)
    {
        _channels.TryRemove(phaseRunId, out _);
        _waiters.TryRemove(phaseRunId, out _);
    }

    public WebSocketBridgeChannel? GetChannel(Guid phaseRunId)
        => _channels.TryGetValue(phaseRunId, out var c) ? c : null;

    public void Dispose()
    {
        foreach (var c in _channels.Values)
        {
            c.Dispose();
        }
    }

    private sealed record BridgeTokenBinding(Guid PhaseRunId, Guid TenantId, DateTimeOffset IssuedAt);
}
