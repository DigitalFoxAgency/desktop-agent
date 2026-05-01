using AgentPlatform.Application.RunContainers;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.RunContainers;

public sealed class DockerRunContainerDriver(
    RunVolumeManager volumes,
    DockerDriverOptions opts,
    ILogger<DockerRunContainerDriver> log) : IRunContainerDriver, IDisposable
{
    private readonly DockerClient _docker = new DockerClientConfiguration(new Uri(opts.DockerEndpoint)).CreateClient();
    private readonly RunVolumeManager _volumes = volumes;
    private readonly DockerDriverOptions _opts = opts;
    private readonly ILogger<DockerRunContainerDriver> _log = log;

    public async Task EnsureVolumeAsync(string runId, CancellationToken cancellationToken)
        => await _volumes.EnsureAsync(runId, cancellationToken).ConfigureAwait(false);

    public Task ArchiveVolumeAsync(string runId, CancellationToken cancellationToken)
        => _volumes.ArchiveAsync(runId, cancellationToken);

    public async Task<RunContainerHandle> StartAsync(RunContainerSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var hostPath = !string.IsNullOrWhiteSpace(spec.WorkingDirHostPath)
            ? spec.WorkingDirHostPath
            : _volumes.ResolveHostPath(spec.WorkflowRunId.ToString("N"));
        Directory.CreateDirectory(hostPath);

        var env = spec.Environment.Select(kv => $"{kv.Key}={kv.Value}").ToList();
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["agentplatform.tenant"] = spec.TenantId.ToString("D"),
            ["agentplatform.run"] = spec.WorkflowRunId.ToString("D"),
            ["agentplatform.phase"] = spec.PhaseRunId.ToString("D"),
        };

        var name = $"agp-{spec.PhaseRunId:N}";

        var hostConfig = new HostConfig
        {
            AutoRemove = false,
            Memory = (long)spec.MemoryMegabytes * 1024L * 1024L,
            NanoCPUs = (long)(spec.CpuLimit * 1_000_000_000),
            // Drop all kernel capabilities; add back only what file ops need.
            // Without DAC_OVERRIDE/FOWNER/CHOWN, npm/pnpm caches and git
            // workspace ops break under non-root users on bind-mounted dirs.
            CapDrop = ["ALL"],
            CapAdd = ["DAC_OVERRIDE", "FOWNER", "CHOWN"],
            // Read-only root FS is intentionally NOT enabled: the in-container
            // claude CLI + npm + gh write transient state under HOME and /tmp.
            // We rely on (a) USER runner from Dockerfile.run-base, (b) capability
            // drops above, and (c) no-new-privileges below to bound damage.
            ReadonlyRootfs = false,
            // Block setuid escalation inside the container (defence-in-depth
            // even though we're already running as a non-root user).
            SecurityOpt = ["no-new-privileges:true"],
            // Cap process count to stop runaway forks (e.g. a misbehaving
            // build) from exhausting the host's PID table.
            PidsLimit = 512,
            Mounts = BuildMounts(hostPath, _opts),
            ExtraHosts = ["host.docker.internal:host-gateway"],
            NetworkMode = _opts.NetworkMode,
        };

        var create = new CreateContainerParameters
        {
            Image = spec.Image,
            Name = name,
            Env = env,
            Labels = labels,
            WorkingDir = "/workspace",
            HostConfig = hostConfig,
            User = _opts.RunAsUser,
            Tty = false,
            AttachStdin = false,
            AttachStdout = false,
            AttachStderr = false,
        };

        try
        {
            var existing = await _docker.Containers.ListContainersAsync(new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool> { [name] = true },
                },
            }, cancellationToken).ConfigureAwait(false);
            foreach (var c in existing)
            {
                _log.LogInformation("Removing stale container {Name} ({Id})", name, c.ID);
                try
                {
                    await _docker.Containers.RemoveContainerAsync(c.ID,
                        new ContainerRemoveParameters { Force = true }, cancellationToken).ConfigureAwait(false);
                }
                catch (DockerApiException ex)
                {
                    _log.LogWarning(ex, "Stale container removal failed for {Id}", c.ID);
                }
            }
        }
        catch (DockerApiException ex)
        {
            _log.LogWarning(ex, "Container list failed; continuing");
        }

        CreateContainerResponse created;
        try
        {
            created = await _docker.Containers.CreateContainerAsync(create, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            // A container with this name appeared between our list and create
            // (concurrent open or a process killed mid-flight). Force-remove and
            // retry once.
            _log.LogWarning("Create conflicted on {Name}; force-removing and retrying", name);
            try
            {
                await _docker.Containers.RemoveContainerAsync(name,
                    new ContainerRemoveParameters { Force = true }, cancellationToken).ConfigureAwait(false);
            }
            catch (DockerApiException removeEx)
            {
                _log.LogWarning(removeEx, "Conflict-recovery remove failed for {Name}", name);
            }
            created = await _docker.Containers.CreateContainerAsync(create, cancellationToken).ConfigureAwait(false);
        }
        var started = await _docker.Containers.StartContainerAsync(
            created.ID,
            new ContainerStartParameters(),
            cancellationToken).ConfigureAwait(false);

        if (!started)
        {
            throw new InvalidOperationException($"Docker refused to start container {created.ID}");
        }

        _log.LogInformation("Started container {Id} for phase {PhaseRunId}", created.ID, spec.PhaseRunId);
        return new RunContainerHandle(created.ID, DateTimeOffset.UtcNow);
    }

    public async Task StopAsync(string containerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(containerId))
        {
            return;
        }

        await TryDumpLogsAsync(containerId, cancellationToken).ConfigureAwait(false);

        try
        {
            await _docker.Containers.StopContainerAsync(
                containerId,
                new ContainerStopParameters { WaitBeforeKillSeconds = 5 },
                cancellationToken).ConfigureAwait(false);
        }
        catch (DockerApiException ex)
        {
            _log.LogWarning(ex, "Stop failed for {Id}", containerId);
        }

        try
        {
            await _docker.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true, RemoveVolumes = false },
                cancellationToken).ConfigureAwait(false);
        }
        catch (DockerApiException ex)
        {
            _log.LogWarning(ex, "Remove failed for {Id}", containerId);
        }
    }

    private async Task TryDumpLogsAsync(string containerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_opts.LogArchivePath))
        {
            return;
        }
        try
        {
            Directory.CreateDirectory(_opts.LogArchivePath);
            var path = Path.Combine(_opts.LogArchivePath, $"{containerId[..Math.Min(12, containerId.Length)]}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.log");
            await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var stream = await _docker.Containers.GetContainerLogsAsync(containerId, tty: false, new ContainerLogsParameters
            {
                ShowStdout = true,
                ShowStderr = true,
                Timestamps = true,
            }, cancellationToken).ConfigureAwait(false);
            await stream.CopyOutputToAsync(Stream.Null, fs, fs, cancellationToken).ConfigureAwait(false);
            _log.LogInformation("Container {Id} logs flushed to {Path}", containerId, path);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Container log flush failed for {Id}", containerId);
        }
    }

    public void Dispose() => _docker.Dispose();

    private static List<Mount> BuildMounts(string workspaceHostPath, DockerDriverOptions opts)
    {
        var mounts = new List<Mount>
        {
            new()
            {
                Type = "bind",
                Source = workspaceHostPath,
                Target = "/workspace",
            },
        };
        // Optional: bind in a host directory used as the in-container `claude`
        // CLI's HOME, so it sees both ~/.claude/ and ~/.claude.json from a
        // prior `claude login` against a Claude.ai subscription. Lets the
        // CLI run without an Anthropic API key.
        if (!string.IsNullOrWhiteSpace(opts.ClaudeCredentialsHostPath))
        {
            Directory.CreateDirectory(opts.ClaudeCredentialsHostPath);
            mounts.Add(new Mount
            {
                Type = "bind",
                Source = opts.ClaudeCredentialsHostPath,
                Target = "/home/runner/.agp-claude",
                ReadOnly = false,
            });
        }
        if (!string.IsNullOrWhiteSpace(opts.ModulesHostPath) && Directory.Exists(opts.ModulesHostPath))
        {
            mounts.Add(new Mount
            {
                Type = "bind",
                Source = opts.ModulesHostPath,
                Target = "/opt/modules",
                ReadOnly = true,
            });
        }
        return mounts;
    }
}

public sealed class DockerDriverOptions
{
    public string DockerEndpoint { get; set; } =
        OperatingSystem.IsWindows() ? "npipe://./pipe/docker_engine" : "unix:///var/run/docker.sock";
    public string NetworkMode { get; set; } = "bridge";
    public string? RunAsUser { get; set; }

    /// <summary>Host path to the modules root (e.g. <c>repo/modules</c>); bind-mounted read-only at <c>/opt/modules</c>.</summary>
    public string? ModulesHostPath { get; set; }

    /// <summary>Host directory holding pre-authenticated <c>claude</c> credentials, bind-mounted to <c>/home/runner/.claude</c> in the run container. Populate it once by running <c>claude login</c> via the helper script.</summary>
    public string? ClaudeCredentialsHostPath { get; set; }

    /// <summary>Optional host directory to flush container stdout/stderr into when a phase tears down. Useful for after-the-fact debugging.</summary>
    public string? LogArchivePath { get; set; }
}
