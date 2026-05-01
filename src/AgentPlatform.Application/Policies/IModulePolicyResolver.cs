using AgentPlatform.Domain.Policies;

namespace AgentPlatform.Application.Policies;

public interface IModulePolicyResolver
{
    Task<ModulePolicyOverrides> GetAsync(string moduleId, CancellationToken cancellationToken);
}

public sealed class ModulePolicyOverrides(IReadOnlyDictionary<ActionClassification, string> entries)
{
    public static ModulePolicyOverrides Empty { get; } = new(new Dictionary<ActionClassification, string>());

    private readonly IReadOnlyDictionary<ActionClassification, string> _entries = entries;

    public bool TryGet(ActionClassification classification, out string ruling)
    {
        if (_entries.TryGetValue(classification, out var v))
        {
            ruling = v;
            return true;
        }
        ruling = string.Empty;
        return false;
    }
}
