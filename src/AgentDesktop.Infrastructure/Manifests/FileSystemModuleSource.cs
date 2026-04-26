using System.Runtime.CompilerServices;
using AgentDesktop.Application.Abstractions;
using AgentDesktop.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentDesktop.Infrastructure.Manifests;

/// <summary>
/// Reads <c>module.json</c> manifests from the well-known modules
/// root (each module under <c>&lt;root&gt;/&lt;id&gt;/module.json</c>).
/// The launchpad's submodule layout — <c>modules/df-client-launchpad/source/</c> —
/// is naturally supported because <c>sourcePath</c> values inside
/// the manifest are interpreted by consumers relative to the
/// module's directory, not relative to this source.
/// </summary>
public sealed class FileSystemModuleSource : IModuleSource
{
    private readonly FileSystemModuleSourceOptions _options;
    private readonly ILogger<FileSystemModuleSource> _logger;

    public FileSystemModuleSource(
        IOptions<FileSystemModuleSourceOptions> options,
        ILogger<FileSystemModuleSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    public async IAsyncEnumerable<RawModuleManifest> EnumerateAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.RootPath) || !Directory.Exists(_options.RootPath))
        {
            _logger.LogInformation(
                "Modules root '{RootPath}' does not exist; nothing to load.",
                _options.RootPath);
            yield break;
        }

        foreach (var moduleDir in Directory.EnumerateDirectories(_options.RootPath))
        {
            ct.ThrowIfCancellationRequested();

            var manifestPath = Path.Combine(moduleDir, "module.json");
            if (!File.Exists(manifestPath))
            {
                _logger.LogWarning(
                    "Skipping '{Dir}': no module.json found.",
                    moduleDir);
                continue;
            }

            string text;
            try
            {
                text = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Failed to read manifest at {Path}", manifestPath);
                continue;
            }

            yield return new RawModuleManifest(
                ManifestPath: manifestPath,
                ManifestText: text,
                ModuleRoot: moduleDir,
                Source: _options.Source);
        }
    }
}

public sealed class FileSystemModuleSourceOptions
{
    /// <summary>Absolute path to the modules root directory.</summary>
    public string RootPath { get; set; } = "modules";

    /// <summary>Module source to tag every loaded manifest with.</summary>
    public ModuleSource Source { get; set; } = ModuleSource.Bundled;
}
