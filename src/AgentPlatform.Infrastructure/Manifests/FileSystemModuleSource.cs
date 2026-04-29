using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using NJsonSchema;
using NJsonSchema.Validation;

namespace AgentPlatform.Infrastructure.Manifests;

public sealed class FileSystemModuleSourceOptions
{
    public string ModulesRoot { get; set; } = "modules";
}

public sealed class FileSystemModuleSource : IModuleSource
{
    private const int SupportedSchemaVersion = 2;

    private readonly FileSystemModuleSourceOptions _options;
    private readonly Lazy<Task<JsonSchema>> _schema;

    public FileSystemModuleSource(FileSystemModuleSourceOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _schema = new Lazy<Task<JsonSchema>>(LoadSchemaAsync);
    }

    public async Task<IReadOnlyList<DiscoveredModule>> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_options.ModulesRoot))
        {
            return Array.Empty<DiscoveredModule>();
        }

        var schema = await _schema.Value.ConfigureAwait(false);
        var results = new List<DiscoveredModule>();

        foreach (var dir in Directory.EnumerateDirectories(_options.ModulesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var manifestPath = Path.Combine(dir, "module.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            var schemaVersion = doc.RootElement.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.Number
                ? sv.GetInt32()
                : 0;
            if (schemaVersion != SupportedSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"Module at {manifestPath} declares schemaVersion={schemaVersion}; only v{SupportedSchemaVersion} is supported.");
            }

            var errors = schema.Validate(json);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Module manifest {manifestPath} is invalid: {FormatErrors(errors)}");
            }

            var moduleId = doc.RootElement.GetProperty("id").GetString()!;
            var name = doc.RootElement.GetProperty("name").GetString()!;
            var version = doc.RootElement.GetProperty("version").GetString()!;
            results.Add(new DiscoveredModule(
                ModuleId: moduleId,
                DisplayName: name,
                SchemaVersion: schemaVersion,
                Version: version,
                ManifestJson: json,
                SourcePath: dir));
        }

        return results;
    }

    private async Task<JsonSchema> LoadSchemaAsync()
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Manifests", "module.schema.json");
        if (!File.Exists(schemaPath))
        {
            // Fallback: read from this assembly's source-relative location
            schemaPath = Path.Combine(Path.GetDirectoryName(typeof(FileSystemModuleSource).Assembly.Location)!,
                "Manifests", "module.schema.json");
        }

        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException("module.schema.json not found", schemaPath);
        }

        var schemaJson = await File.ReadAllTextAsync(schemaPath).ConfigureAwait(false);
        return await JsonSchema.FromJsonAsync(schemaJson).ConfigureAwait(false);
    }

    private static string FormatErrors(ICollection<ValidationError> errors)
        => string.Join("; ", errors.Select(e => $"{e.Path}: {e.Kind}"));
}
