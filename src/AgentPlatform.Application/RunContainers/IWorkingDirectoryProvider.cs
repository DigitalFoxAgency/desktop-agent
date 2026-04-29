namespace AgentPlatform.Application.RunContainers;

/// <summary>
/// Resolves the host-side working directory for a workflow run. Implementations decide
/// whether the path is on the local filesystem, a mounted volume, or a remote share.
/// </summary>
public interface IWorkingDirectoryProvider
{
    string Resolve(Guid tenantId, Guid runId);
}
