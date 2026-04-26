using System.Collections.ObjectModel;

namespace AgentDesktop.Domain.Modules;

/// <summary>
/// A platform-visible entry point declared by a module's manifest.
/// The platform delegates whole operations to the owning module;
/// the module's internal step ordering is the module's concern
/// (see research.md R18).
/// </summary>
public sealed record Operation
{
    public Operation(
        string id,
        string name,
        string description,
        IReadOnlyList<SkillParameter> inputs)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(inputs);

        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Operation id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Operation name cannot be empty.", nameof(name));
        }

        Id = id;
        Name = name;
        Description = description;
        Inputs = new ReadOnlyCollection<SkillParameter>(inputs.ToList());
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<SkillParameter> Inputs { get; }
}
