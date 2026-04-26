namespace AgentDesktop.Domain.Policies;

/// <summary>Where a proposed dangerous action originated.</summary>
public abstract record PolicyOrigin
{
    private PolicyOrigin()
    {
    }

    /// <summary>Direct skill invocation by the user (US4 advanced path).</summary>
    public sealed record FromSkill(ModuleId ModuleId, SkillId SkillId) : PolicyOrigin;

    /// <summary>
    /// A running delegation: a module is executing one of its
    /// declared operations and proposed a dangerous action via
    /// IDelegationCallbacks.RequestConfirmationAsync.
    /// </summary>
    public sealed record FromDelegation(ModuleId ModuleId, string OperationId) : PolicyOrigin;

    /// <summary>Returns the module id common to both shapes (the operation/skill id is shape-specific).</summary>
    public ModuleId AsModuleId() => this switch
    {
        FromSkill s => s.ModuleId,
        FromDelegation d => d.ModuleId,
        _ => throw new InvalidOperationException($"Unknown PolicyOrigin: {GetType().Name}"),
    };
}
