using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AgentPlatform.Bridge.ClaudeWrapper;
using AgentPlatform.Bridge.Config;
using AgentPlatform.Bridge.FileWatcher;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Bridge;

/// <summary>
/// Top-level Bridge process: connects to the API, fans claude / file-watcher events out
/// over a single WebSocket and routes user input + confirmation decisions back into the
/// wrapper.
/// </summary>
public sealed class BridgeRuntime : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BridgeOptions _opts;
    private readonly IClaudeWrapper _wrapper;
    private readonly ILogger<BridgeRuntime> _log;
    private readonly IHostApplicationLifetime _lifetime;

    public BridgeRuntime(
        IOptions<BridgeOptions> opts,
        IClaudeWrapper wrapper,
        ILogger<BridgeRuntime> log,
        IHostApplicationLifetime lifetime)
    {
        _opts = opts.Value;
        _wrapper = wrapper;
        _log = log;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrEmpty(_opts.BridgeToken) || _opts.PhaseRunId == Guid.Empty)
        {
            _log.LogError("Bridge requires AGP_BRIDGE_TOKEN and AGP_PHASE_RUN_ID; exiting.");
            _lifetime.StopApplication();
            return;
        }

        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("X-Bridge-Token", _opts.BridgeToken);
        ws.Options.SetRequestHeader("X-Phase-Run-Id", _opts.PhaseRunId.ToString("D"));

        var url = new Uri(_opts.ApiWebSocketUrl);
        _log.LogInformation("Bridge connecting to {Url} for phase {Phase}", url, _opts.PhaseRunId);

        try
        {
            await ws.ConnectAsync(url, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Bridge failed to connect");
            _lifetime.StopApplication();
            return;
        }

        var watcherLog = LoggerFactory.Create(b => b.AddSimpleConsole()).CreateLogger<TreeEventPublisher>();
        await using var watcher = new TreeEventPublisher(_opts.WorkingDir, watcherLog, LoggerFactory.Create(b => b.AddSimpleConsole()));

        await _wrapper.StartAsync(new ClaudeSessionSpec(
            _opts.WorkingDir,
            _opts.Skill,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()), stoppingToken).ConfigureAwait(false);

        var reader = Task.Run(() => ReadIncomingAsync(ws, stoppingToken), stoppingToken);
        var sender = Task.Run(() => SendOutgoingAsync(ws, watcher, stoppingToken), stoppingToken);

        await Task.WhenAny(reader, sender).ConfigureAwait(false);
        try
        {
            if (ws.State == WebSocketState.Open)
            {
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (WebSocketException) { /* ignore */ }
        await _wrapper.StopAsync(CancellationToken.None).ConfigureAwait(false);
        _lifetime.StopApplication();
    }

    private async Task SendOutgoingAsync(ClientWebSocket ws, TreeEventPublisher watcher, CancellationToken cancellationToken)
    {
        var sendLock = new SemaphoreSlim(1, 1);

        async Task SendFrame(object frame)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
            await sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            finally { sendLock.Release(); }
        }

        var claudeTask = Task.Run(async () =>
        {
            await foreach (var evt in _wrapper.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
            {
                switch (evt)
                {
                    case ClaudeStreamEvent.TextDelta td:
                        await SendFrame(new { type = "assistant_chunk", text = td.Text }).ConfigureAwait(false);
                        break;
                    case ClaudeStreamEvent.TurnComplete:
                        await SendFrame(new { type = "assistant_turn_complete" }).ConfigureAwait(false);
                        break;
                    case ClaudeStreamEvent.TokenUsage tu:
                        await SendFrame(new
                        {
                            type = "token_usage",
                            model = tu.Model,
                            inputTokens = tu.InputTokens,
                            outputTokens = tu.OutputTokens,
                            cacheCreationTokens = tu.CacheCreationTokens,
                            cacheReadTokens = tu.CacheReadTokens,
                        }).ConfigureAwait(false);
                        break;
                    case ClaudeStreamEvent.ToolUseProposed tup:
                        // Phase 6 (US4) replaces this passthrough with policy-driven confirmation requests.
                        await SendFrame(new
                        {
                            type = "tool_use",
                            tool = tup.ToolName,
                            commandLine = tup.CommandLine,
                            targetPath = tup.TargetPath,
                        }).ConfigureAwait(false);
                        break;
                    case ClaudeStreamEvent.SessionExited:
                        return;
                }
            }
        }, cancellationToken);

        var fileTask = Task.Run(async () =>
        {
            await foreach (var f in watcher.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await SendFrame(new
                {
                    type = "file_changed",
                    path = f.RelativePath,
                    kind = f.Kind.ToString().ToLowerInvariant(),
                }).ConfigureAwait(false);
            }
        }, cancellationToken);

        await Task.WhenAll(claudeTask, fileTask).ConfigureAwait(false);
    }

    private async Task ReadIncomingAsync(ClientWebSocket ws, CancellationToken cancellationToken)
    {
        var buffer = new byte[8 * 1024];
        var sb = new StringBuilder();
        while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return;
            }
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (!result.EndOfMessage)
            {
                continue;
            }
            var json = sb.ToString();
            sb.Clear();
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                {
                    continue;
                }
                var t = typeProp.GetString();
                if (t == "user_input" && doc.RootElement.TryGetProperty("text", out var text))
                {
                    await _wrapper.SendInputAsync(text.GetString() ?? string.Empty, cancellationToken).ConfigureAwait(false);
                }
                // Phase 6 will dispatch confirmation_decision frames into the policy bridge.
            }
            catch (JsonException ex)
            {
                _log.LogWarning(ex, "Bridge could not parse incoming frame");
            }
        }
    }
}
