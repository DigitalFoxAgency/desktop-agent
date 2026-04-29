using AgentPlatform.Domain.Modules;

namespace AgentPlatform.Application.Modules;

public interface IModuleRegistry
{
    Task<IReadOnlyList<Module>> ListAsync(CancellationToken cancellationToken);

    Task<Module?> GetAsync(string moduleId, CancellationToken cancellationToken);

    Task<WorkflowDef?> GetWorkflowAsync(string moduleId, string workflowId, CancellationToken cancellationToken);

    Task RefreshAsync(CancellationToken cancellationToken);
}
