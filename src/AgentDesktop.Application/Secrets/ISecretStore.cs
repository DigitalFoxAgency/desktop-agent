using AgentDesktop.Domain;

namespace AgentDesktop.Application.Secrets;

/// <summary>
/// Boundary for OS-secure credential storage. See
/// <c>contracts/ISecretStore.md</c> for behavioural rules — including
/// "secrets MUST never be returned in log output, exceptions, or
/// telemetry" and atomic-write requirements.
/// </summary>
public interface ISecretStore
{
    Task<string?> GetAsync(SecretKey key, CancellationToken ct);
    Task SetAsync(SecretKey key, string value, CancellationToken ct);
    Task DeleteAsync(SecretKey key, CancellationToken ct);
}
