using System.Collections.ObjectModel;

namespace AgentDesktop.Domain.Scenarios;

/// <summary>One step in a scenario: a named module + skill plus how its inputs are bound.</summary>
public sealed record ScenarioStep
{
    public ScenarioStep(
        int index,
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, ScenarioBinding> inputBindings,
        string description)
    {
        ArgumentNullException.ThrowIfNull(inputBindings);
        ArgumentNullException.ThrowIfNull(description);

        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Step index must be ≥ 0.");
        }

        // Forward references must be rejected at load time, not run time.
        foreach (var (paramName, binding) in inputBindings)
        {
            if (binding is ScenarioBinding.PriorStepOutput { StepIndex: var src } && src >= index)
            {
                throw new ArgumentException(
                    $"Step {index}.{paramName} references step {src}, which is not strictly earlier.",
                    nameof(inputBindings));
            }
        }

        Index = index;
        ModuleId = moduleId;
        SkillId = skillId;
        InputBindings = new ReadOnlyDictionary<string, ScenarioBinding>(
            new Dictionary<string, ScenarioBinding>(inputBindings, StringComparer.Ordinal));
        Description = description;
    }

    public int Index { get; }
    public ModuleId ModuleId { get; }
    public SkillId SkillId { get; }
    public IReadOnlyDictionary<string, ScenarioBinding> InputBindings { get; }
    public string Description { get; }
}
