using AgentDesktop.Domain;
using AgentDesktop.Domain.Scenarios;

namespace AgentDesktop.Application.Scenarios;

/// <summary>
/// Loaded catalogue of scenarios, validated against the registered
/// modules. Scenarios whose modules/skills are absent or whose
/// schema version is unknown surface as <see cref="ScenarioLoadStatus.Incompatible"/>.
/// </summary>
public interface IScenarioRegistry
{
    Task RefreshAsync(CancellationToken ct);
    IReadOnlyList<Scenario> GetAll();
    Scenario? Find(ScenarioId id);
}
