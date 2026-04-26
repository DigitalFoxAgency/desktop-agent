using AgentDesktop.Application.Modules;
using AgentDesktop.Application.Runtime;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Desktop.Composition;

/// <summary>
/// Walks the runtime through Install → Start at app launch and primes
/// the module registry. Invoked from <see cref="App.OnFrameworkInitializationCompleted"/>.
/// </summary>
internal sealed class RuntimeBootstrapper
{
    private readonly IRuntimeManager _runtime;
    private readonly IModuleRegistry _modules;
    private readonly ILogger<RuntimeBootstrapper> _logger;

    public RuntimeBootstrapper(
        IRuntimeManager runtime,
        IModuleRegistry modules,
        ILogger<RuntimeBootstrapper> logger)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(logger);

        _runtime = runtime;
        _modules = modules;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await _runtime.EnsureInstalledAsync(ct).ConfigureAwait(false);
            await _runtime.StartAsync(ct).ConfigureAwait(false);
            await _modules.RefreshAsync(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Bootstrap must not crash the app on first-run failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Runtime bootstrap failed.");
        }
    }
}
