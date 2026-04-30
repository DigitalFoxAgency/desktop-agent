using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Bridge.ClaudeWrapper;

/// <summary>
/// Drives `claude --output-format stream-json --input-format stream-json` and translates
/// its NDJSON event stream into <see cref="ClaudeStreamEvent"/>s.
/// </summary>
public sealed class StreamJsonClaudeWrapper : IClaudeWrapper, IAsyncDisposable
{
    private readonly StreamJsonOptions _opts;
    private readonly ILogger<StreamJsonClaudeWrapper> _log;
    private readonly Channel<ClaudeStreamEvent> _events = Channel.CreateUnbounded<ClaudeStreamEvent>();
    private Process? _process;
    private Task? _stdoutReader;
    private Task? _stderrReader;

    public StreamJsonClaudeWrapper(StreamJsonOptions opts, ILogger<StreamJsonClaudeWrapper> log)
    {
        _opts = opts;
        _log = log;
    }

    public Task StartAsync(ClaudeSessionSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var args = new List<string>
        {
            "--output-format", "stream-json",
            "--input-format", "stream-json",
            "--print",
            "--verbose",
        };
        // Note: Claude Code 2.x invokes skills via the `/<skill-name>` slash
        // command inside the prompt, not via a CLI flag. The skill name is
        // prepended to the seed message below.

        // Grant claude tool-access to the run's module directory (e.g. the
        // launchpad source tree) so its CLAUDE.md / agents / scripts / template
        // files are readable without polluting the working dir.
        var moduleDir = Environment.GetEnvironmentVariable("AGP_MODULE_DIR");
        if (!string.IsNullOrWhiteSpace(moduleDir) && Directory.Exists(moduleDir))
        {
            args.Add("--add-dir");
            args.Add(moduleDir);
        }

        // Until US4's per-action confirmation surface lands, claude's own
        // permission gate would block every Write/Edit/Bash. Allow the API to
        // override the mode (acceptEdits | acceptAll | bypassPermissions | ask
        // | plan). Default `acceptEdits` lets file edits flow but still
        // prompts on Bash; production should keep claude in `ask` and rely on
        // the policy engine + ConfirmationGate.
        var permMode = Environment.GetEnvironmentVariable("AGP_PERMISSION_MODE");
        if (!string.IsNullOrWhiteSpace(permMode))
        {
            args.Add("--permission-mode");
            args.Add(permMode);
        }

        var psi = new ProcessStartInfo
        {
            FileName = _opts.ClaudeBinary,
            WorkingDirectory = spec.WorkingDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }
        foreach (var (k, v) in spec.Environment)
        {
            psi.Environment[k] = v;
        }
        // If the API supplied AGP_CLAUDE_HOME, redirect HOME for the CLI so it
        // reads creds + .claude.json from the bind-mounted host directory.
        var claudeHome = Environment.GetEnvironmentVariable("AGP_CLAUDE_HOME");
        if (!string.IsNullOrWhiteSpace(claudeHome))
        {
            psi.Environment["HOME"] = claudeHome;
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.Exited += (_, _) =>
        {
            var code = _process?.ExitCode ?? -1;
            _events.Writer.TryWrite(new ClaudeStreamEvent.SessionExited(code));
            _events.Writer.TryComplete();
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException("Failed to start claude.");
        }

        _stdoutReader = Task.Run(() => ReadStdoutAsync(_process.StandardOutput.BaseStream, cancellationToken), cancellationToken);
        _stderrReader = Task.Run(() => ReadStderrAsync(_process.StandardError, cancellationToken), cancellationToken);

        var skillCommand = string.IsNullOrWhiteSpace(spec.Skill) ? "" : $"/{spec.Skill}";
        var inputsBlock = spec.Inputs.Count == 0
            ? ""
            : "\n\nInputs:\n" + string.Join("\n", spec.Inputs.Select(kv => $"- {kv.Key}: {kv.Value}"));
        var seed = (skillCommand + inputsBlock).Trim();
        if (seed.Length == 0) { seed = "Begin the assigned task."; }
        return SendInputAsync(seed, cancellationToken);
    }

    public IAsyncEnumerable<ClaudeStreamEvent> ReadEventsAsync(CancellationToken cancellationToken)
        => _events.Reader.ReadAllAsync(cancellationToken);

    public async Task SendInputAsync(string text, CancellationToken cancellationToken)
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }
        var msg = new
        {
            type = "user",
            message = new
            {
                role = "user",
                content = new[] { new { type = "text", text } },
            },
        };
        var line = JsonSerializer.Serialize(msg);
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is null)
        {
            return;
        }
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                if (!_process.WaitForExit(2000))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Claude stop failed");
        }
        if (_stdoutReader is not null) { try { await _stdoutReader.ConfigureAwait(false); } catch { /* ignore */ } }
        if (_stderrReader is not null) { try { await _stderrReader.ConfigureAwait(false); } catch { /* ignore */ } }
        _events.Writer.TryComplete();
    }

    private async Task ReadStdoutAsync(Stream stdout, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stdout);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                ParseAndEmit(line);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Claude stdout reader failed");
        }
    }

    private async Task ReadStderrAsync(StreamReader stderr, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await stderr.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }
                _log.LogDebug("[claude/stderr] {Line}", line);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
    }

    private void ParseAndEmit(string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { return; }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return;
            }
            var t = typeProp.GetString();
            switch (t)
            {
                case "assistant" when doc.RootElement.TryGetProperty("message", out var msg):
                    EmitAssistant(msg);
                    break;
                case "result":
                    if (doc.RootElement.TryGetProperty("usage", out var usage))
                    {
                        EmitUsage(usage, doc.RootElement);
                    }
                    _events.Writer.TryWrite(new ClaudeStreamEvent.TurnComplete());
                    break;
            }
        }
    }

    private void EmitAssistant(JsonElement message)
    {
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (var part in content.EnumerateArray())
        {
            if (!part.TryGetProperty("type", out var typeProp))
            {
                continue;
            }
            var t = typeProp.GetString();
            if (t == "text" && part.TryGetProperty("text", out var text))
            {
                _events.Writer.TryWrite(new ClaudeStreamEvent.TextDelta(text.GetString() ?? string.Empty));
            }
            else if (t == "tool_use")
            {
                var name = part.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                string? command = null, target = null;
                if (part.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object)
                {
                    if (input.TryGetProperty("command", out var c))
                    {
                        command = c.GetString();
                    }
                    if (input.TryGetProperty("file_path", out var fp))
                    {
                        target = fp.GetString();
                    }
                    if (target is null && input.TryGetProperty("path", out var p))
                    {
                        target = p.GetString();
                    }
                }
                _events.Writer.TryWrite(new ClaudeStreamEvent.ToolUseProposed(name, command, target, part.GetRawText()));
            }
        }
        if (message.TryGetProperty("usage", out var u))
        {
            EmitUsage(u, message);
        }
    }

    private void EmitUsage(JsonElement usage, JsonElement parent)
    {
        long input = usage.TryGetProperty("input_tokens", out var i) ? i.GetInt64() : 0;
        long output = usage.TryGetProperty("output_tokens", out var o) ? o.GetInt64() : 0;
        long cacheCreate = usage.TryGetProperty("cache_creation_input_tokens", out var cc) ? cc.GetInt64() : 0;
        long cacheRead = usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt64() : 0;
        var model = parent.TryGetProperty("model", out var m) ? m.GetString() ?? "unknown" : "unknown";
        _events.Writer.TryWrite(new ClaudeStreamEvent.TokenUsage(model, input, output, cacheCreate, cacheRead));
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None).ConfigureAwait(false);
        _process?.Dispose();
    }
}

public sealed class StreamJsonOptions
{
    public string ClaudeBinary { get; set; } = "claude";
}
