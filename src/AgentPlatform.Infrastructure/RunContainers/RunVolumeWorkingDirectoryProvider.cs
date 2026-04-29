using AgentPlatform.Application.RunContainers;

namespace AgentPlatform.Infrastructure.RunContainers;

public sealed class RunVolumeWorkingDirectoryProvider : IWorkingDirectoryProvider
{
    private readonly RunVolumeManager _volumes;

    public RunVolumeWorkingDirectoryProvider(RunVolumeManager volumes) => _volumes = volumes;

    public string Resolve(Guid tenantId, Guid runId)
        => _volumes.ResolveHostPath(runId.ToString("N"));
}
