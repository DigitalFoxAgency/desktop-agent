namespace AgentDesktop.Domain.Modules;

/// <summary>A module's declared dependency on another module by id + version range.</summary>
public sealed record ModuleDependency(ModuleId ModuleId, SemanticVersionRange VersionRange);
