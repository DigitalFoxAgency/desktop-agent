using AgentDesktop.Domain;

namespace AgentDesktop.Application.Abstractions;

/// <summary>
/// Stream of raw, un-validated module manifests from some external
/// store (file system, future remote marketplace, …). The
/// <c>ModuleRegistry</c> is responsible for validating each manifest
/// and producing a <see cref="Domain.Modules.Module"/>.
/// </summary>
public interface IModuleSource
{
    IAsyncEnumerable<RawModuleManifest> EnumerateAsync(CancellationToken ct);
}

/// <summary>
/// A single raw manifest. <see cref="ManifestPath"/> is informational
/// (used in error messages); <see cref="ManifestText"/> is the
/// payload validators consume; <see cref="ModuleRoot"/> is the
/// directory module-relative paths (e.g. <c>SourcePath</c>) resolve
/// against.
/// </summary>
public sealed record RawModuleManifest(
    string ManifestPath,
    string ManifestText,
    string ModuleRoot,
    ModuleSource Source);
