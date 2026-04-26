using System.Collections.ObjectModel;

namespace AgentDesktop.Domain.Modules;

/// <summary>
/// A named capability exposed by a <see cref="Module"/>. The
/// authoritative behaviour lives in the file at
/// <see cref="SourcePath"/> (typically a <c>SKILL.md</c>) inside the
/// module's source tree; this type is the platform's metadata view.
/// </summary>
public sealed record Skill
{
    public Skill(
        SkillId id,
        ModuleId moduleId,
        string name,
        string description,
        IReadOnlyList<SkillParameter> inputs,
        IReadOnlyList<SkillParameter> outputs,
        ActionClassification classification,
        SkillKind kind = SkillKind.Automated,
        string? sourcePath = null,
        string? verificationKey = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Skill name cannot be empty.", nameof(name));
        }

        Id = id;
        ModuleId = moduleId;
        Name = name;
        Description = description;
        Inputs = new ReadOnlyCollection<SkillParameter>(inputs.ToList());
        Outputs = new ReadOnlyCollection<SkillParameter>(outputs.ToList());
        Classification = classification;
        Kind = kind;
        SourcePath = sourcePath;
        VerificationKey = verificationKey;
    }

    public SkillId Id { get; }
    public ModuleId ModuleId { get; }
    public string Name { get; }
    public string Description { get; }
    public IReadOnlyList<SkillParameter> Inputs { get; }
    public IReadOnlyList<SkillParameter> Outputs { get; }
    public ActionClassification Classification { get; }
    public SkillKind Kind { get; }

    /// <summary>
    /// Path (relative to the module root) to the authoritative
    /// human-readable contract for this skill, e.g.
    /// <c>source/template/.claude/skills/init/SKILL.md</c>.
    /// </summary>
    public string? SourcePath { get; }

    /// <summary>
    /// Token expected in <c>SESSION-LOG.md</c> (e.g. <c>init: verified</c>)
    /// before scenario steps depending on this skill may start. Mirrors
    /// the launchpad constitution §8.
    /// </summary>
    public string? VerificationKey { get; }
}
