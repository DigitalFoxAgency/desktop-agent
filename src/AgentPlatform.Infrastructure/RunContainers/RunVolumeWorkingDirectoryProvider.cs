using AgentPlatform.Application.RunContainers;

namespace AgentPlatform.Infrastructure.RunContainers;

public sealed class RunVolumeWorkingDirectoryProvider(RunVolumeManager volumes) : IWorkingDirectoryProvider
{
    private readonly RunVolumeManager _volumes = volumes;

    public string Resolve(Guid tenantId, Guid runId)
        => _volumes.ResolveHostPath(runId.ToString("N"));
}
