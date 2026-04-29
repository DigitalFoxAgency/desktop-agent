namespace AgentPlatform.Domain.Secrets;

public sealed class VaultSecretRef
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string Key { get; set; } = string.Empty;
    public string CiphertextRef { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? RotatedAt { get; set; }
}
