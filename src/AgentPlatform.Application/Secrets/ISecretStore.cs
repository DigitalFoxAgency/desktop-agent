namespace AgentPlatform.Application.Secrets;

public interface ISecretStore
{
    Task<string?> GetAsync(Guid tenantId, string key, CancellationToken cancellationToken);

    Task SetAsync(Guid tenantId, string key, string value, string? description, CancellationToken cancellationToken);

    Task DeleteAsync(Guid tenantId, string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListKeysAsync(Guid tenantId, CancellationToken cancellationToken);
}
