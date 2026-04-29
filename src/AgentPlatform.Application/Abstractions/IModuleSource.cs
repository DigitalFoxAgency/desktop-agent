namespace AgentPlatform.Application.Abstractions;

public interface IModuleSource
{
    Task<IReadOnlyList<DiscoveredModule>> DiscoverAsync(CancellationToken cancellationToken);
}

public sealed record DiscoveredModule(
    string ModuleId,
    string DisplayName,
    int SchemaVersion,
    string Version,
    string ManifestJson,
    string SourcePath);
