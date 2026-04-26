using AgentDesktop.Application.Abstractions;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Modules;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Application.Modules;

/// <summary>
/// Default <see cref="IModuleRegistry"/>. Loads modules from an
/// <see cref="IModuleSource"/> on Refresh, validates each manifest
/// via <see cref="ModuleManifestValidator"/>, and resolves
/// dependency availability across the loaded set. Unloadable
/// modules are surfaced as <see cref="ModuleLoadStatus.Unavailable"/>
/// or <see cref="ModuleLoadStatus.Incompatible"/> rather than
/// silently dropped.
/// </summary>
public sealed class ModuleRegistry : IModuleRegistry, IDisposable
{
    private readonly IModuleSource _source;
    private readonly ModuleManifestValidator _validator;
    private readonly ILogger<ModuleRegistry> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private Dictionary<ModuleId, Module> _modules = new();
    private bool _disposed;

    public ModuleRegistry(
        IModuleSource source,
        ModuleManifestValidator validator,
        ILogger<ModuleRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(logger);

        _source = source;
        _validator = validator;
        _logger = logger;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _refreshGate.Dispose();
        _disposed = true;
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await _refreshGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var loaded = new Dictionary<ModuleId, Module>();
            await foreach (var raw in _source.EnumerateAsync(ct).ConfigureAwait(false))
            {
                var module = await _validator
                    .ParseAndValidateAsync(raw.ManifestText, raw.Source, ct)
                    .ConfigureAwait(false);

                if (loaded.ContainsKey(module.Id))
                {
                    _logger.LogWarning(
                        "Duplicate module id '{ModuleId}' detected at {ManifestPath}; keeping the first one loaded.",
                        module.Id,
                        raw.ManifestPath);
                    continue;
                }

                loaded[module.Id] = module;
            }

            // Resolve dependencies — modules whose declared deps aren't
            // present become Unavailable.
            var resolved = new Dictionary<ModuleId, Module>();
            foreach (var (id, module) in loaded)
            {
                if (module.LoadStatus != ModuleLoadStatus.Loaded)
                {
                    resolved[id] = module;
                    continue;
                }

                var missing = module.Dependencies
                    .Where(d => !loaded.ContainsKey(d.ModuleId))
                    .Select(d => d.ModuleId.Value)
                    .ToList();

                if (missing.Count > 0)
                {
                    resolved[id] = Module.Failed(
                        module.Id,
                        module.Version,
                        module.Source,
                        ModuleLoadStatus.Unavailable,
                        $"Missing dependencies: {string.Join(", ", missing)}");
                    continue;
                }

                resolved[id] = module;
            }

            _modules = resolved;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public IReadOnlyList<Module> GetAll() => _modules.Values.ToList();

    public Module? Find(ModuleId id) =>
        _modules.TryGetValue(id, out var m) ? m : null;

    public Operation? FindOperation(ModuleId moduleId, string operationId) =>
        Find(moduleId)?.FindOperation(operationId);

    public Skill? FindSkill(ModuleId moduleId, SkillId skillId) =>
        Find(moduleId)?.FindSkill(skillId);
}
