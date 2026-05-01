using System.Collections.Concurrent;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Application.Modules;
using AgentPlatform.Application.Policies;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Application.Secrets;
using AgentPlatform.Application.Usage;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Runs;

public sealed class PhaseSessionService(
    IServiceScopeFactory scopes,
    IRunContainerDriver driver,
    IBridgeChannelFactory bridges,
    IClock clock,
    IOptions<PhaseSessionOptions> opts,
    ILogger<PhaseSessionService> log) : IPhaseSessionService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly IRunContainerDriver _driver = driver;
    private readonly IBridgeChannelFactory _bridges = bridges;
    private readonly IClock _clock = clock;
    private readonly ILogger<PhaseSessionService> _log = log;
    private readonly PhaseSessionOptions _opts = opts.Value;

    private readonly ConcurrentDictionary<Guid, PhaseSessionHandle> _active = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pumps = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _openLocks = new();

    public async Task<OpenPhaseSessionResult> OpenAsync(Guid tenantId, Guid userId, Guid phaseRunId, CancellationToken cancellationToken)
    {
        if (_active.TryGetValue(phaseRunId, out var existing))
        {
            return OpenPhaseSessionResult.Success(existing);
        }

        // Serialize concurrent OpenAsync calls for the same phase so the second
        // caller observes the populated _active entry instead of racing the
        // container create (which would 409 on name collision).
        var gate = _openLocks.GetOrAdd(phaseRunId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_active.TryGetValue(phaseRunId, out existing))
            {
                return OpenPhaseSessionResult.Success(existing);
            }
            return await OpenInternalAsync(tenantId, userId, phaseRunId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<OpenPhaseSessionResult> OpenInternalAsync(Guid tenantId, Guid userId, Guid phaseRunId, CancellationToken cancellationToken)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var phases = scope.ServiceProvider.GetRequiredService<IPhaseRunRepository>();
        var modules = scope.ServiceProvider.GetRequiredService<IModuleRegistry>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISecretStore>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

        var lookup = await phases.GetAsync(tenantId, phaseRunId, cancellationToken).ConfigureAwait(false);
        if (lookup is null)
        {
            return OpenPhaseSessionResult.Failure("phase not found");
        }

        var (phase, run) = lookup;
        if (run.Status == RunStatus.Paused)
        {
            // Common cause: cost cap. Tell the UI exactly why so it can offer
            // an admin "raise cap" / "resume" affordance instead of a generic
            // disconnect.
            var reason = $"Run paused (cost cap ${_opts.PerRunCostCapCents / 100m:F2} reached). Increase the cap or disable it to continue.";
            return OpenPhaseSessionResult.Failure(reason);
        }
        var workflow = await modules.GetWorkflowAsync(run.ModuleId, run.WorkflowId, cancellationToken).ConfigureAwait(false);
        var phaseDef = workflow?.Phases.FirstOrDefault(p => p.PhaseId == phase.PhaseId);
        if (workflow is null || phaseDef is null)
        {
            return OpenPhaseSessionResult.Failure("workflow or phase definition missing");
        }

        await _driver.EnsureVolumeAsync(run.Id.ToString("N"), cancellationToken).ConfigureAwait(false);

        var token = _bridges.IssueConnectToken(phaseRunId, tenantId);
        var env = await BuildEnvironmentAsync(secrets, tenantId, run, phase, phaseDef.Skill, token, cancellationToken).ConfigureAwait(false);

        var spec = new RunContainerSpec(
            tenantId,
            run.Id,
            phase.Id,
            _opts.RunImage,
            run.WorkingDirPath,
            env,
            _opts.MemoryMegabytes,
            _opts.CpuLimit);

        RunContainerHandle container;
        try
        {
            container = await _driver.StartAsync(spec, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to start container for phase {PhaseRunId}", phaseRunId);
            return OpenPhaseSessionResult.Failure($"container start failed: {ex.Message}");
        }

        IBridgeChannel channel;
        try
        {
            channel = await _bridges.WaitForConnectionAsync(phaseRunId, _opts.BridgeConnectTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Bridge failed to connect for phase {PhaseRunId}; tearing down container", phaseRunId);
            await _driver.StopAsync(container.ContainerId, CancellationToken.None).ConfigureAwait(false);
            return OpenPhaseSessionResult.Failure($"bridge handshake failed: {ex.Message}");
        }

        phase.Status = RunStatus.Running;
        phase.StartedAt ??= _clock.UtcNow;
        phase.ContainerId = container.ContainerId;
        await phases.UpdateAsync(phase, cancellationToken).ConfigureAwait(false);

        var handle = new PhaseSessionHandle
        {
            PhaseRunId = phase.Id,
            TenantId = tenantId,
            WorkflowRunId = run.Id,
            ContainerId = container.ContainerId,
            WorkingDir = run.WorkingDirPath,
            Channel = channel,
            StartedAt = container.StartedAt,
        };
        _active[phaseRunId] = handle;

        var pumpCts = new CancellationTokenSource();
        _pumps[phaseRunId] = pumpCts;
        _ = Task.Run(() => PumpUsageAsync(handle, pumpCts.Token), pumpCts.Token);

        await audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = userId,
            Category = "phase",
            Action = "open",
            SubjectType = "phase_run",
            SubjectId = phase.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { container.ContainerId, phaseDef.Skill }),
            OccurredAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);

        return OpenPhaseSessionResult.Success(handle);
    }

    public async Task CloseAsync(Guid phaseRunId, CancellationToken cancellationToken)
    {
        if (!_active.TryRemove(phaseRunId, out var handle))
        {
            return;
        }

        if (_pumps.TryRemove(phaseRunId, out var cts))
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        try
        {
            await handle.Channel.CloseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Channel close failed for phase {PhaseRunId}", phaseRunId);
        }

        try
        {
            await _driver.StopAsync(handle.ContainerId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Container stop failed for {ContainerId}", handle.ContainerId);
        }
    }

    public PhaseSessionHandle? GetActive(Guid phaseRunId)
        => _active.TryGetValue(phaseRunId, out var h) ? h : null;

    private async Task PumpUsageAsync(PhaseSessionHandle handle, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in handle.Channel.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
            {
                if (evt is BridgeEvent.ConfirmationRequested confirmation)
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var confirms = scope.ServiceProvider.GetRequiredService<ConfirmationService>();
                    try
                    {
                        await confirms.RecordProposalAsync(
                            handle.TenantId,
                            handle.PhaseRunId,
                            requestedByUserId: null,
                            confirmation.ConfirmationId,
                            confirmation.Classification,
                            confirmation.Summary,
                            confirmation.TargetPath,
                            confirmation.CommandLine,
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Failed to persist confirmation {Id}", confirmation.ConfirmationId);
                    }
                    continue;
                }
                if (evt is BridgeEvent.PhaseCompleted completion)
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var runs = scope.ServiceProvider.GetRequiredService<WorkflowRunService>();
                    try
                    {
                        await runs.OnPhaseCompletedAsync(handle.TenantId, handle.PhaseRunId, completion.Verified, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, "Phase hand-off failed for {PhaseRunId}", handle.PhaseRunId);
                    }
                    // Tear down the container; the next phase opens a fresh one.
                    _ = Task.Run(() => CloseAsync(handle.PhaseRunId, CancellationToken.None), CancellationToken.None);
                    return;
                }
                if (evt is BridgeEvent.TokenUsage usage)
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var meter = scope.ServiceProvider.GetRequiredService<IUsageMeter>();
                    var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();

                    await meter.RecordAsync(new UsageLedgerEntry
                    {
                        Id = Guid.NewGuid(),
                        TenantId = handle.TenantId,
                        WorkflowRunId = handle.WorkflowRunId,
                        PhaseRunId = handle.PhaseRunId,
                        Model = usage.Model,
                        InputTokens = usage.InputTokens,
                        OutputTokens = usage.OutputTokens,
                        CacheCreationTokens = usage.CacheCreationTokens,
                        CacheReadTokens = usage.CacheReadTokens,
                        CostUsd = EstimateCostUsd(usage),
                        RecordedAt = usage.OccurredAt,
                    }, CancellationToken.None).ConfigureAwait(false);

                    if (_opts.DisableCostCap || _opts.PerRunCostCapCents <= 0) { continue; }

                    var runCostCents = await meter.GetRunCostCentsAsync(handle.WorkflowRunId, CancellationToken.None).ConfigureAwait(false);
                    if (runCostCents >= _opts.PerRunCostCapCents)
                    {
                        _log.LogWarning("Run {RunId} hit cost cap ({Cents}c); pausing", handle.WorkflowRunId, runCostCents);
                        var runs = scope.ServiceProvider.GetRequiredService<IWorkflowRunRepository>();
                        var run = await runs.GetAsync(handle.TenantId, handle.WorkflowRunId, CancellationToken.None).ConfigureAwait(false);
                        if (run is not null && run.Status != RunStatus.Paused)
                        {
                            run.Status = RunStatus.Paused;
                            // Reuse AppendNextPhase to persist run-status updates without writing a new phase.
                            await runs.AppendNextPhaseAsync(
                                run.Phases.First(p => p.Id == handle.PhaseRunId),
                                run,
                                nextPhase: null,
                                assignment: null,
                                inboxItem: null,
                                CancellationToken.None).ConfigureAwait(false);
                        }
                        await audit.WriteAsync(new AuditEntry
                        {
                            Id = Guid.NewGuid(),
                            TenantId = handle.TenantId,
                            ActorUserId = null,
                            Category = "run",
                            Action = "cost_cap_pause",
                            SubjectType = "workflow_run",
                            SubjectId = handle.WorkflowRunId.ToString(),
                            PayloadJson = JsonSerializer.Serialize(new { runCostCents, _opts.PerRunCostCapCents }),
                            OccurredAt = _clock.UtcNow,
                        }, CancellationToken.None).ConfigureAwait(false);
                        await CloseAsync(handle.PhaseRunId, CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _log.LogError(ex, "Usage pump failed for phase {PhaseRunId}", handle.PhaseRunId);
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildEnvironmentAsync(
        ISecretStore secrets,
        Guid tenantId,
        WorkflowRun run,
        PhaseRun phase,
        string skill,
        string bridgeToken,
        CancellationToken cancellationToken)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AGP_TENANT_ID"] = tenantId.ToString("D"),
            ["AGP_RUN_ID"] = run.Id.ToString("D"),
            ["AGP_PHASE_RUN_ID"] = phase.Id.ToString("D"),
            ["AGP_PHASE_ID"] = phase.PhaseId,
            ["AGP_SKILL"] = skill,
            ["AGP_BRIDGE_URL"] = _opts.BridgeUrl,
            ["AGP_BRIDGE_TOKEN"] = bridgeToken,
            ["AGP_WORKING_DIR"] = "/workspace",
            // The runtime mounts modules read-only at /opt/modules; tell the
            // Bridge where this run's module sits so it can wire skills + tool
            // access without polluting /workspace.
            ["AGP_MODULE_DIR"] = $"/opt/modules/{run.ModuleId}",
            ["AGP_PERMISSION_MODE"] = string.IsNullOrWhiteSpace(_opts.ClaudePermissionMode) ? "" : _opts.ClaudePermissionMode,
            // When the operator chose bypassPermissions, also tell the Bridge
            // to skip the confirmation gate so the UI doesn't show pointless
            // dialogs that claude has already raced past.
            ["AGP_AUTO_CONFIRM"] = string.Equals(_opts.ClaudePermissionMode, "bypassPermissions", StringComparison.OrdinalIgnoreCase) ? "1" : "0",
            ["AGP_SYSTEM_PROMPT"] = BuildSystemPrompt(run, phase, skill),
            ["AGP_RUN_INPUTS"] = run.InputsJson,
            ["ANTHROPIC_API_KEY"] = _opts.AnthropicApiKey ?? string.Empty,
            ["ANTHROPIC_PROMPT_CACHE"] = "1",
        };
        // Optional: route the in-container `claude` CLI to a different
        // Anthropic-compatible endpoint (e.g. a local claude-code-router /
        // LiteLLM proxy in front of a local model). When set, `claude` honors
        // ANTHROPIC_BASE_URL and the proxy handles model translation.
        if (!string.IsNullOrWhiteSpace(_opts.AnthropicBaseUrl))
        {
            env["ANTHROPIC_BASE_URL"] = _opts.AnthropicBaseUrl;
        }
        // Tell the bridge to point claude's HOME at the bind-mounted credentials
        // dir; both ~/.claude/ and ~/.claude.json then land in the host mount and
        // persist across phases.
        if (!string.IsNullOrWhiteSpace(_opts.ClaudeHomeInContainer))
        {
            env["AGP_CLAUDE_HOME"] = _opts.ClaudeHomeInContainer;
        }
        if (_opts.MockBridge)
        {
            env["AGP_MOCK"] = "1";
        }

        var keys = await secrets.ListKeysAsync(tenantId, cancellationToken).ConfigureAwait(false);
        foreach (var key in keys)
        {
            var value = await secrets.GetAsync(tenantId, key, cancellationToken).ConfigureAwait(false);
            if (value is not null)
            {
                env[$"AGP_SECRET_{key.ToUpperInvariant()}"] = value;
            }
        }

        return env;
    }

    private static string BuildSystemPrompt(WorkflowRun run, PhaseRun phase, string skill)
    {
        // Deterministic orientation message injected at session start. Stops
        // claude from spinning on "where do I write?" and "is /opt/modules
        // writable?" — the answers are: /workspace, no.
        return string.Join('\n', new[]
        {
            "You are running inside the Agent Platform's per-phase sandbox.",
            "",
            $"Module: {run.ModuleId}",
            $"Workflow: {run.WorkflowId}",
            $"Phase: {phase.PhaseId} (skill: {skill})",
            "",
            "Filesystem layout:",
            "- `/workspace` is your working directory and persistent volume across phases. Treat it as the module's project root. WRITE ALL OUTPUTS UNDER `/workspace`. Anything you create here is what end-users see in the chat UI's file tree.",
            "- `/opt/modules/<module-id>/` is the read-only module source (CLAUDE.md, agency skills, template files). READ-ONLY. NEVER attempt to write or run mkdir/cp into it; it will fail and waste turns. Use it only for reference.",
            "- The module's slash-skills (e.g. `/init`, `/pre-research`) are already wired into your home; invoke them by name.",
            "",
            "Operating principles:",
            "1. Don't ask the user where to write — always write under `/workspace`. If a SKILL.md mentions `clients/<slug>/` or similar, place it under `/workspace/clients/<slug>/`.",
            "2. Don't probe for permissions or settings files — assume you have full write access to `/workspace` and read access to `/opt/modules`.",
            "3. Keep replies concise and human-friendly. The user is often a non-technical agency role (marketer, strategist, designer). Surface progress in plain language; reserve technical detail for when asked.",
            "4. When the assigned phase's work is done, append a single line `[<skill>: completed]` to `/workspace/SESSION-LOG.md`. The platform watches this marker and queues the next phase automatically.",
        });
    }

    private static decimal EstimateCostUsd(BridgeEvent.TokenUsage u)
    {
        const decimal inputPerMillion = 3.0m;
        const decimal outputPerMillion = 15.0m;
        const decimal cacheReadPerMillion = 0.30m;
        const decimal cacheWritePerMillion = 3.75m;
        return ((decimal)u.InputTokens * inputPerMillion
              + (decimal)u.OutputTokens * outputPerMillion
              + (decimal)u.CacheReadTokens * cacheReadPerMillion
              + (decimal)u.CacheCreationTokens * cacheWritePerMillion) / 1_000_000m;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var id in _active.Keys.ToArray())
        {
            await CloseAsync(id, CancellationToken.None).ConfigureAwait(false);
        }
    }
}

public sealed class PhaseSessionOptions
{
    public string RunImage { get; set; } = "agentplatform/run-base:latest";
    public string BridgeUrl { get; set; } = "ws://host.docker.internal:5080/ws/bridge";
    public string? AnthropicApiKey { get; set; }

    /// <summary>Optional Anthropic-compatible endpoint (e.g. a local claude-code-router proxy at <c>http://host.docker.internal:11434</c>) used to drive the in-container <c>claude</c> CLI against a non-Anthropic model.</summary>
    public string? AnthropicBaseUrl { get; set; }

    /// <summary>In-container path used as HOME for the claude CLI when subscription-auth credentials are bind-mounted. Defaults to <c>/home/runner/.agp-claude</c> when the bind mount is enabled.</summary>
    public string? ClaudeHomeInContainer { get; set; } = "/home/runner/.agp-claude";
    public int MemoryMegabytes { get; set; } = 2048;
    public double CpuLimit { get; set; } = 1.0;
    public TimeSpan BridgeConnectTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public long PerRunCostCapCents { get; set; } = 500;

    /// <summary>When true, the API instructs the Bridge to run in mock mode (scripted responses, no Anthropic call). Useful for plumbing smoke tests.</summary>
    public bool MockBridge { get; set; }

    /// <summary>Override for claude's <c>--permission-mode</c>. Until US4's per-action confirmation surface is wired, set to <c>acceptEdits</c> (let edits through, prompt on Bash) or <c>bypassPermissions</c> (skip all gates) for end-to-end testing. Leave empty for claude's default (interactive ask).</summary>
    public string? ClaudePermissionMode { get; set; }

    /// <summary>Dev/test toggle. When true, the per-run cost cap is skipped entirely — no <c>cost_cap_pause</c> audit is written, no run status mutation. Equivalent to setting <see cref="PerRunCostCapCents"/> to 0, but more explicit.</summary>
    public bool DisableCostCap { get; set; }
}
