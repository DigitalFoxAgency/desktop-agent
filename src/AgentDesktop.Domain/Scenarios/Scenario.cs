using System.Collections.ObjectModel;
using AgentDesktop.Domain.Modules;

namespace AgentDesktop.Domain.Scenarios;

/// <summary>A versioned, ordered workflow that composes skills across modules.</summary>
public sealed record Scenario
{
    public Scenario(
        ScenarioId id,
        SemanticVersion version,
        string name,
        string description,
        int schemaVersion,
        IReadOnlyList<SkillParameter> inputs,
        IReadOnlyList<ScenarioStep> steps,
        ScenarioLoadStatus loadStatus = ScenarioLoadStatus.Loaded,
        string? loadError = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(steps);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scenario name cannot be empty.", nameof(name));
        }

        if (schemaVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be ≥ 1.");
        }

        if (loadStatus != ScenarioLoadStatus.Loaded && loadError is null)
        {
            throw new ArgumentException(
                "LoadError must be set when LoadStatus is not Loaded.",
                nameof(loadError));
        }

        if (loadStatus == ScenarioLoadStatus.Loaded && steps.Count == 0)
        {
            throw new ArgumentException(
                "A loaded scenario must declare at least one step.",
                nameof(steps));
        }

        // Steps must be 0-based and contiguous.
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Index != i)
            {
                throw new ArgumentException(
                    $"Scenario step at position {i} has index {steps[i].Index}; steps must be 0-based and contiguous.",
                    nameof(steps));
            }
        }

        Id = id;
        Version = version;
        Name = name;
        Description = description;
        SchemaVersion = schemaVersion;
        Inputs = new ReadOnlyCollection<SkillParameter>(inputs.ToList());
        Steps = new ReadOnlyCollection<ScenarioStep>(steps.ToList());
        LoadStatus = loadStatus;
        LoadError = loadError;
    }

    public ScenarioId Id { get; }
    public SemanticVersion Version { get; }
    public string Name { get; }
    public string Description { get; }
    public int SchemaVersion { get; }
    public IReadOnlyList<SkillParameter> Inputs { get; }
    public IReadOnlyList<ScenarioStep> Steps { get; }
    public ScenarioLoadStatus LoadStatus { get; }
    public string? LoadError { get; }

    public static Scenario Failed(
        ScenarioId id,
        SemanticVersion version,
        string error) =>
        new(id, version, id.Value, "Failed to load: " + error,
            schemaVersion: 1,
            inputs: Array.Empty<SkillParameter>(),
            steps: Array.Empty<ScenarioStep>(),
            loadStatus: ScenarioLoadStatus.Incompatible,
            loadError: error);
}
