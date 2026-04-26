namespace AgentDesktop.Domain.Modules;

/// <summary>
/// Per-skill policy override declared by a module's manifest. The policy
/// engine combines these with the baseline classification table at
/// evaluation time.
/// </summary>
public sealed record ModulePolicy
{
    public ModulePolicy(SkillId skillId, ActionClassification classification, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        SkillId = skillId;
        Classification = classification;
        Reason = reason;
    }

    public SkillId SkillId { get; }
    public ActionClassification Classification { get; }
    public string Reason { get; }
}
