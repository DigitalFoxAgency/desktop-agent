using AgentPlatform.Application.Modules;

namespace AgentPlatform.Api.HostedServices;

/// <summary>
/// Loads installed modules from disk on startup. T093.
/// </summary>
public sealed class ModuleRegistrySeeder(IModuleRegistry registry, ILogger<ModuleRegistrySeeder> logger) : IHostedService
{
    private readonly IModuleRegistry _registry = registry;
    private readonly ILogger<ModuleRegistrySeeder> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _registry.RefreshAsync(cancellationToken).ConfigureAwait(false);
            var loaded = await _registry.ListAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Module registry loaded {Count} module(s).", loaded.Count);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Module registry refresh failed.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
