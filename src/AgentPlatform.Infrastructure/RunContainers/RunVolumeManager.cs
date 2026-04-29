using Microsoft.Extensions.Logging;

namespace AgentPlatform.Infrastructure.RunContainers;

public sealed class RunVolumeManager
{
    private readonly RunVolumeOptions _opts;
    private readonly ILogger<RunVolumeManager> _log;

    public RunVolumeManager(RunVolumeOptions opts, ILogger<RunVolumeManager> log)
    {
        _opts = opts;
        _log = log;
    }

    public string ResolveHostPath(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("runId required", nameof(runId));
        }
        return Path.Combine(_opts.RunsRoot, runId);
    }

    public Task EnsureAsync(string runId, CancellationToken cancellationToken)
    {
        var path = ResolveHostPath(runId);
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
            _log.LogInformation("Created run volume {Path}", path);
        }
        return Task.CompletedTask;
    }

    public Task ArchiveAsync(string runId, CancellationToken cancellationToken)
    {
        var path = ResolveHostPath(runId);
        if (!Directory.Exists(path))
        {
            return Task.CompletedTask;
        }
        Directory.CreateDirectory(_opts.ArchiveRoot);
        var archivePath = Path.Combine(_opts.ArchiveRoot, $"{runId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        try
        {
            Directory.Move(path, archivePath);
            _log.LogInformation("Archived run volume {Run} → {Archive}", path, archivePath);
        }
        catch (IOException ex)
        {
            _log.LogWarning(ex, "Volume archive failed for {Run}", runId);
        }
        return Task.CompletedTask;
    }
}

public sealed class RunVolumeOptions
{
    public string RunsRoot { get; set; } = "/var/lib/agency/runs";
    public string ArchiveRoot { get; set; } = "/var/lib/agency/archive";
}
