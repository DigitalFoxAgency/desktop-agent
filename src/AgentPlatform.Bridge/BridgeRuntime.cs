using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Policies;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Bridge.ClaudeWrapper;
using AgentPlatform.Bridge.Config;
using AgentPlatform.Bridge.FileWatcher;
using AgentPlatform.Bridge.PhaseCompletion;
using AgentPlatform.Bridge.PolicyBridge;
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
    private readonly ConfirmationGate _confirmGate = new();
    private readonly BuildStepSemaphore _buildSemaphore = new();
    private readonly IntentInterceptor _interceptor;

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
        var moduleId = string.IsNullOrWhiteSpace(_opts.ModuleDir) ? null : Path.GetFileName(_opts.ModuleDir.TrimEnd('/'));
        var classifier = new ActionClassifier(new DefaultPolicyEngine(), moduleId, phaseId: _opts.Skill);
        _interceptor = new IntentInterceptor(classifier, _confirmGate, _buildSemaphore);
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

        var detectorLog = LoggerFactory.Create(b => b.AddSimpleConsole()).CreateLogger<CompletionDetector>();
        await using var completion = new CompletionDetector(_opts.WorkingDir, _opts.Skill, detectorLog);
        completion.Start();

        WireModuleSkills();

        await _wrapper.StartAsync(new ClaudeSessionSpec(
            _opts.WorkingDir,
            _opts.Skill,
            new Dictionary<string, string>(),
            new Dictionary<string, string>()), stoppingToken).ConfigureAwait(false);

        var reader = Task.Run(() => ReadIncomingAsync(ws, stoppingToken), stoppingToken);
        var sender = Task.Run(() => SendOutgoingAsync(ws, watcher, completion, stoppingToken), stoppingToken);

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

    private void WireModuleSkills()
    {
        if (string.IsNullOrWhiteSpace(_opts.ModuleDir)) { return; }
        var skillsSrc = Path.Combine(_opts.ModuleDir, "source", "template", ".claude", "skills");
        if (!Directory.Exists(skillsSrc))
        {
            _log.LogInformation("ModuleDir set but no skills at {Src}; skipping skill wiring", skillsSrc);
            return;
        }
        var home = Environment.GetEnvironmentVariable("AGP_CLAUDE_HOME") ?? Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            _log.LogWarning("HOME unset; cannot wire module skills");
            return;
        }
        var skillsDst = Path.Combine(home, ".claude", "skills");
        try
        {
            Directory.CreateDirectory(skillsDst);
            foreach (var skillDir in Directory.EnumerateDirectories(skillsSrc))
            {
                var name = Path.GetFileName(skillDir);
                var link = Path.Combine(skillsDst, name);
                if (Directory.Exists(link) || File.Exists(link)) { continue; }
                File.CreateSymbolicLink(link, skillDir);
            }
            _log.LogInformation("Wired module skills from {Src} into {Dst}", skillsSrc, skillsDst);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to wire module skills");
        }
    }

    private async Task SendOutgoingAsync(ClientWebSocket ws, TreeEventPublisher watcher, CompletionDetector completion, CancellationToken cancellationToken)
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
                        await HandleToolUseAsync(tup, SendFrame, cancellationToken).ConfigureAwait(false);
                        break;
                    case ClaudeStreamEvent.SessionExited:
                        completion.SignalSessionExited();
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

        var completionTask = Task.Run(async () =>
        {
            await foreach (var c in completion.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await SendFrame(new
                {
                    type = "phase_completed",
                    skill = c.Skill,
                    verified = c.Verified,
                }).ConfigureAwait(false);
            }
        }, cancellationToken);

        await Task.WhenAll(claudeTask, fileTask, completionTask).ConfigureAwait(false);
    }

    private async Task HandleToolUseAsync(
        ClaudeStreamEvent.ToolUseProposed tup,
        Func<object, Task> sendFrame,
        CancellationToken cancellationToken)
    {
        InterceptionResult result;
        try
        {
            result = await _interceptor.InspectAsync(tup, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "IntentInterceptor failed; passing tool through");
            await sendFrame(new
            {
                type = "tool_use",
                tool = tup.ToolName,
                commandLine = tup.CommandLine,
                targetPath = tup.TargetPath,
            }).ConfigureAwait(false);
            return;
        }

        if (!result.ConfirmationRequired)
        {
            await sendFrame(new
            {
                type = "tool_use",
                tool = tup.ToolName,
                commandLine = tup.CommandLine,
                targetPath = tup.TargetPath,
            }).ConfigureAwait(false);
            return;
        }

        // Emit the confirmation request frame; the API persists it and
        // surfaces it in the web UI. The bridge waits for the matching
        // confirmation_decision frame via _confirmGate before continuing.
        await sendFrame(new
        {
            type = "confirmation_request",
            confirmationId = result.ConfirmationId,
            classification = result.Decision.Classification.ToString(),
            summary = result.Summary,
            targetPath = result.TargetPath,
            commandLine = result.CommandLine,
            reason = result.Decision.Reason,
        }).ConfigureAwait(false);

        ConfirmationOutcome outcome;
        try
        {
            outcome = await _confirmGate.WaitForDecisionAsync(result.ConfirmationId!.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (outcome.Confirmed && result.Decision.RequiresBuildSemaphore)
        {
            // Build-class action: gate behind the host-wide cap of 2 concurrent
            // builds. While waiting we surface a "waiting" frame so the web UI
            // can render a spinner and the user knows nothing's stuck.
            await sendFrame(new
            {
                type = "build_semaphore_waiting",
                confirmationId = result.ConfirmationId,
            }).ConfigureAwait(false);

            using var slot = await _buildSemaphore.AcquireAsync(cancellationToken).ConfigureAwait(false);
            await sendFrame(new
            {
                type = "build_semaphore_acquired",
                confirmationId = result.ConfirmationId,
            }).ConfigureAwait(false);
            await ResumeWrapperAsync(outcome, cancellationToken).ConfigureAwait(false);
            return;
        }

        await ResumeWrapperAsync(outcome, cancellationToken).ConfigureAwait(false);
    }

    private async Task ResumeWrapperAsync(ConfirmationOutcome outcome, CancellationToken cancellationToken)
    {
        // Until claude's stream-json permission protocol is wired through the
        // wrapper, surface the user's decision back into the conversation as a
        // synthetic user message. Confirmed → claude proceeds; declined →
        // claude is told to skip the action gracefully.
        var note = string.IsNullOrWhiteSpace(outcome.Note) ? string.Empty : $" (note: {outcome.Note})";
        var message = outcome.Confirmed
            ? $"[platform] User confirmed the proposed action — proceed.{note}"
            : $"[platform] User declined the proposed action — skip it and continue with the rest of the plan.{note}";
        try
        {
            await _wrapper.SendInputAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to forward confirmation outcome to wrapper");
        }
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
                else if (t == "confirmation_decision"
                    && doc.RootElement.TryGetProperty("confirmationId", out var idProp)
                    && doc.RootElement.TryGetProperty("confirmed", out var confirmedProp))
                {
                    var id = idProp.GetGuid();
                    var confirmed = confirmedProp.GetBoolean();
                    var note = doc.RootElement.TryGetProperty("note", out var n) ? n.GetString() : null;
                    if (!_confirmGate.Resolve(id, confirmed, note))
                    {
                        _log.LogWarning("Confirmation decision for unknown id {Id}", id);
                    }
                }
            }
            catch (JsonException ex)
            {
                _log.LogWarning(ex, "Bridge could not parse incoming frame");
            }
        }
    }
}
