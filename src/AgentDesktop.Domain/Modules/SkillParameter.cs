namespace AgentDesktop.Domain.Modules;

/// <summary>Declared kind of a skill input/output parameter.</summary>
public enum SkillParameterKind
{
    String = 0,
    Integer = 1,
    Number = 2,
    Boolean = 3,
    Path = 4,
    Url = 5,
    Json = 6,
}

/// <summary>A single declared input or output of a skill.</summary>
public sealed record SkillParameter
{
    public SkillParameter(
        string name,
        SkillParameterKind kind,
        bool required,
        string description)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(description);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("SkillParameter name cannot be empty.", nameof(name));
        }

        Name = name;
        Kind = kind;
        Required = required;
        Description = description;
    }

    public string Name { get; }
    public SkillParameterKind Kind { get; }
    public bool Required { get; }
    public string Description { get; }
}
