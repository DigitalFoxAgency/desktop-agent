using System.Collections.Concurrent;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Application.Modules;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Application.Secrets;
using AgentPlatform.Application.Usage;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Runs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Application.Runs;

public sealed class PhaseSessionService : IPhaseSessionService, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IRunContainerDriver _driver;
    private readonly IBridgeChannelFactory _bridges;
    private readonly IClock _clock;
    private readonly ILogger<PhaseSessionService> _log;
    private readonly PhaseSessionOptions _opts;

    private readonly ConcurrentDictionary<Guid, PhaseSessionHandle> _active = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _pumps = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _openLocks = new();

    public PhaseSessionService(
        IServiceScopeFactory scopes,
        IRunContainerDriver driver,
        IBridgeChannelFactory bridges,
        IClock clock,
        IOptions<PhaseSessionOptions> opts,
        ILogger<PhaseSessionService> log)
    {
        _scopes = scopes;
        _driver = driver;
        _bridges = bridges;
        _clock = clock;
        _opts = opts.Value;
        _log = log;
    }

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

                    var runCostCents = await meter.GetRunCostCentsAsync(handle.WorkflowRunId, CancellationToken.None).ConfigureAwait(false);
                    if (_opts.PerRunCostCapCents > 0 && runCostCents >= _opts.PerRunCostCapCents)
                    {
                        _log.LogWarning("Run {RunId} hit cost cap ({Cents}c); pausing", handle.WorkflowRunId, runCostCents);
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
            ["ANTHROPIC_API_KEY"] = _opts.AnthropicApiKey ?? string.Empty,
            ["ANTHROPIC_PROMPT_CACHE"] = "1",
        };
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
    public int MemoryMegabytes { get; set; } = 2048;
    public double CpuLimit { get; set; } = 1.0;
    public TimeSpan BridgeConnectTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public long PerRunCostCapCents { get; set; } = 500;

    /// <summary>When true, the API instructs the Bridge to run in mock mode (scripted responses, no Anthropic call). Useful for plumbing smoke tests.</summary>
    public bool MockBridge { get; set; }
}
