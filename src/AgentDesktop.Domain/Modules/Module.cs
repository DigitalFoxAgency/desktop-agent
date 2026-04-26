using System.Collections.ObjectModel;

namespace AgentDesktop.Domain.Modules;

/// <summary>
/// Loaded representation of a validated <c>module.json</c>. A user's
/// modules are first-class product entities. Note: not to be confused
/// with the <c>Modules</c> subsystem (architectural unit) — see
/// <c>docs/architecture/subsystems-future.md</c> for the naming rule.
/// </summary>
public sealed record Module
{
    public Module(
        ModuleId id,
        SemanticVersion version,
        string name,
        string description,
        int schemaVersion,
        IReadOnlyList<ModuleDependency> dependencies,
        IReadOnlyList<Skill> skills,
        IReadOnlyList<McpServerDescriptor> mcpServers,
        IReadOnlyList<PromptTemplate> prompts,
        IReadOnlyList<ModulePolicy> policies,
        ModuleSource source,
        ModuleLoadStatus loadStatus = ModuleLoadStatus.Loaded,
        string? loadError = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(mcpServers);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(policies);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Module name cannot be empty.", nameof(name));
        }

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be ≥ 1.");
        }

        if (loadStatus != ModuleLoadStatus.Loaded && loadError is null)
        {
            throw new ArgumentException(
                "LoadError must be set when LoadStatus is not Loaded.",
                nameof(loadError));
        }

        // Skill ids are unique within a module
        var dupSkill = skills
            .GroupBy(s => s.Id)
            .FirstOrDefault(g => g.Count() > 1);
        if (dupSkill is not null)
        {
            throw new ArgumentException(
                $"Duplicate skill id within module: '{dupSkill.Key}'.",
                nameof(skills));
        }

        // Skills must declare this module as their owner
        foreach (var skill in skills)
        {
            if (!skill.ModuleId.Equals(id))
            {
                throw new ArgumentException(
                    $"Skill '{skill.Id}' declares module '{skill.ModuleId}' but belongs to '{id}'.",
                    nameof(skills));
            }
        }

        Id = id;
        Version = version;
        Name = name;
        Description = description;
        SchemaVersion = schemaVersion;
        Dependencies = new ReadOnlyCollection<ModuleDependency>(dependencies.ToList());
        Skills = new ReadOnlyCollection<Skill>(skills.ToList());
        McpServers = new ReadOnlyCollection<McpServerDescriptor>(mcpServers.ToList());
        Prompts = new ReadOnlyCollection<PromptTemplate>(prompts.ToList());
        Policies = new ReadOnlyCollection<ModulePolicy>(policies.ToList());
        Source = source;
        LoadStatus = loadStatus;
        LoadError = loadError;
    }

    public ModuleId Id { get; }
    public SemanticVersion Version { get; }
    public string Name { get; }
    public string Description { get; }
    public int SchemaVersion { get; }
    public IReadOnlyList<ModuleDependency> Dependencies { get; }
    public IReadOnlyList<Skill> Skills { get; }
    public IReadOnlyList<McpServerDescriptor> McpServers { get; }
    public IReadOnlyList<PromptTemplate> Prompts { get; }
    public IReadOnlyList<ModulePolicy> Policies { get; }
    public ModuleSource Source { get; }
    public ModuleLoadStatus LoadStatus { get; }
    public string? LoadError { get; }

    /// <summary>Returns the skill with the given id, or <c>null</c> if not present.</summary>
    public Skill? FindSkill(SkillId id) => Skills.FirstOrDefault(s => s.Id.Equals(id));

    /// <summary>
    /// Build a "failed-to-load" placeholder module. Used by the registry
    /// so the catalogue can show the broken module to the user instead
    /// of silently omitting it.
    /// </summary>
    public static Module Failed(
        ModuleId id,
        SemanticVersion version,
        ModuleSource source,
        ModuleLoadStatus status,
        string error)
    {
        if (status == ModuleLoadStatus.Loaded)
        {
            throw new ArgumentException("Failed() requires a non-Loaded status.", nameof(status));
        }

        return new Module(
            id,
            version,
            id.Value,
            "Failed to load: " + error,
            schemaVersion: 1,
            dependencies: Array.Empty<ModuleDependency>(),
            skills: Array.Empty<Skill>(),
            mcpServers: Array.Empty<McpServerDescriptor>(),
            prompts: Array.Empty<PromptTemplate>(),
            policies: Array.Empty<ModulePolicy>(),
            source: source,
            loadStatus: status,
            loadError: error);
    }
}
