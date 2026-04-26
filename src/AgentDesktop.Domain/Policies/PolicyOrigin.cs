namespace AgentDesktop.Domain.Policies;

/// <summary>Where a proposed dangerous action originated.</summary>
public abstract record PolicyOrigin
{
    private PolicyOrigin()
    {
    }

    /// <summary>Direct skill invocation by the user (Story US4).</summary>
    public sealed record FromSkill(ModuleId ModuleId, SkillId SkillId) : PolicyOrigin;

    /// <summary>Step within a running scenario (Story US2).</summary>
    public sealed record FromScenarioStep(ScenarioId ScenarioId, int StepIndex, ModuleId ModuleId, SkillId SkillId) : PolicyOrigin;

    /// <summary>Returns the (ModuleId, SkillId) pair common to both shapes.</summary>
    public (ModuleId ModuleId, SkillId SkillId) AsModuleSkill() => this switch
    {
        FromSkill s => (s.ModuleId, s.SkillId),
        FromScenarioStep ss => (ss.ModuleId, ss.SkillId),
        _ => throw new InvalidOperationException($"Unknown PolicyOrigin: {GetType().Name}"),
    };
}
