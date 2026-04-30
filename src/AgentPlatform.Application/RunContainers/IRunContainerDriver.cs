namespace AgentPlatform.Application.RunContainers;

public interface IRunContainerDriver
{
    Task<RunContainerHandle> StartAsync(RunContainerSpec spec, CancellationToken cancellationToken);

    Task StopAsync(string containerId, CancellationToken cancellationToken);

    Task EnsureVolumeAsync(string runId, CancellationToken cancellationToken);

    /// <summary>Archives the per-run working volume (typically by moving it to an archive root). Called once a run terminates so the working dir is preserved off the live path.</summary>
    Task ArchiveVolumeAsync(string runId, CancellationToken cancellationToken);
}

public sealed record RunContainerSpec(
    Guid TenantId,
    Guid WorkflowRunId,
    Guid PhaseRunId,
    string Image,
    string WorkingDirHostPath,
    IReadOnlyDictionary<string, string> Environment,
    int MemoryMegabytes,
    double CpuLimit);

public sealed record RunContainerHandle(
    string ContainerId,
    DateTimeOffset StartedAt);
