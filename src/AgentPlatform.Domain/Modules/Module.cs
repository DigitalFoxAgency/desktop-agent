namespace AgentPlatform.Domain.Modules;

#pragma warning disable CA1716 // 'Module' is the domain term used throughout spec.md / plan.md
public sealed class Module
{
    public Guid Id { get; init; }
    public string ModuleId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public ModuleStatus Status { get; set; }
    public string? UnavailableReason { get; set; }
    public List<ModuleVersion> Versions { get; init; } = [];
}

public sealed class ModuleVersion
{
    public Guid Id { get; init; }
    public Guid ModuleId { get; init; }
    public string Version { get; set; } = string.Empty;
    public int SchemaVersion { get; set; }
    public string ManifestJson { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; init; }
}

public enum ModuleStatus
{
    Available = 0,
    Unavailable = 1,
}
