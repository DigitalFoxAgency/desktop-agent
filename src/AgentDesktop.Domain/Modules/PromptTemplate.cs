namespace AgentDesktop.Domain.Modules;

/// <summary>A named prompt template shipped by a module.</summary>
public sealed record PromptTemplate
{
    public PromptTemplate(string name, string body)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(body);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Prompt name cannot be empty.", nameof(name));
        }

        Name = name;
        Body = body;
    }

    public string Name { get; }
    public string Body { get; }
}
