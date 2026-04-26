using AgentDesktop.Domain;
using AgentDesktop.Domain.Modules;

namespace AgentDesktop.Application.Modules;

/// <summary>
/// Loaded catalogue of modules. See <c>contracts/IModuleRegistry.md</c>
/// for the behavioural contract — including the rule that
/// validation failures produce a <see cref="ModuleLoadStatus.Incompatible"/>
/// or <see cref="ModuleLoadStatus.Unavailable"/> module rather than
/// throwing.
/// </summary>
public interface IModuleRegistry
{
    /// <summary>Reload modules from the underlying source. Idempotent and concurrency-safe.</summary>
    Task RefreshAsync(CancellationToken ct);

    /// <summary>Returns every module the registry knows about, including failed-to-load ones.</summary>
    IReadOnlyList<Module> GetAll();

    /// <summary>Returns the module with the given id, or <c>null</c> if absent.</summary>
    Module? Find(ModuleId id);

    /// <summary>Returns the skill with the given (module, skill) pair, or <c>null</c>.</summary>
    Skill? FindSkill(ModuleId moduleId, SkillId skillId);
}
