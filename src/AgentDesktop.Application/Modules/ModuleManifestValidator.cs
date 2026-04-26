using System.Text.Json;
using System.Text.Json.Serialization;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Modules;
using NJsonSchema;
using NJsonSchema.Validation;

namespace AgentDesktop.Application.Modules;

/// <summary>
/// Validates raw <c>module.json</c> text against the embedded
/// <c>module.schema.json</c> and converts a valid manifest into a
/// <see cref="Domain.Modules.Module"/>. Invalid manifests produce
/// a <see cref="Module"/> with <see cref="ModuleLoadStatus.Incompatible"/>
/// rather than throwing — the registry then shows the broken
/// module to the user instead of silently dropping it.
/// </summary>
public sealed class ModuleManifestValidator
{
    private readonly Lazy<Task<JsonSchema>> _schema;

    public ModuleManifestValidator()
    {
        _schema = new Lazy<Task<JsonSchema>>(LoadSchemaAsync);
    }

    /// <summary>
    /// Parse + validate a raw manifest. Always returns a Module —
    /// either Loaded with parsed contents, or Incompatible with a
    /// LoadError describing what went wrong.
    /// </summary>
    public async Task<Module> ParseAndValidateAsync(
        string manifestJson,
        ModuleSource source,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(manifestJson);
        ct.ThrowIfCancellationRequested();

        // First pass: structural validation against the JSON schema.
        var schema = await _schema.Value.ConfigureAwait(false);
        ICollection<ValidationError> errors;
        try
        {
            errors = schema.Validate(manifestJson);
        }
        catch (JsonException ex)
        {
            return Module.Failed(
                new ModuleId("unknown"),
                SemanticVersion.Parse("0.0.0"),
                source,
                ModuleLoadStatus.Incompatible,
                $"Manifest is not valid JSON: {ex.Message}");
        }

        // Try to extract id/version even on validation failure so the
        // catalogue can show "broken module X.Y.Z".
        ManifestDto? dto = null;
        try
        {
            dto = JsonSerializer.Deserialize<ManifestDto>(manifestJson, JsonOptions);
        }
        catch (JsonException)
        {
            // Already covered above; safety net.
        }

        var fallbackId = TryParseId(dto?.Id) ?? new ModuleId("unknown");
        var fallbackVersion = TryParseVersion(dto?.Version) ?? new SemanticVersion(0, 0, 0);

        if (errors.Count > 0)
        {
            var firstError = errors.First();
            return Module.Failed(
                fallbackId,
                fallbackVersion,
                source,
                ModuleLoadStatus.Incompatible,
                $"Schema validation failed: {firstError.Path}: {firstError.Kind}");
        }

        if (dto is null)
        {
            return Module.Failed(
                fallbackId,
                fallbackVersion,
                source,
                ModuleLoadStatus.Incompatible,
                "Manifest deserialised to null after schema validation passed; this should not happen.");
        }

        // Schema-version policy: the platform supports schemaVersion 1.
        // Future versions are loaded as Incompatible (FR-018).
        if (dto.SchemaVersion != 1)
        {
            return Module.Failed(
                fallbackId,
                fallbackVersion,
                source,
                ModuleLoadStatus.Incompatible,
                $"Unsupported manifest schemaVersion: {dto.SchemaVersion}.");
        }

        try
        {
            return BuildModule(dto, source);
        }
        catch (ArgumentException ex)
        {
            return Module.Failed(
                fallbackId,
                fallbackVersion,
                source,
                ModuleLoadStatus.Incompatible,
                $"Manifest contents rejected by domain invariants: {ex.Message}");
        }
    }

    private static Module BuildModule(ManifestDto dto, ModuleSource source)
    {
        var moduleId = new ModuleId(dto.Id!);
        var version = SemanticVersion.Parse(dto.Version!);

        var operations = (dto.Operations ?? new List<OperationDto>())
            .Select(op => new Operation(
                id: op.Id!,
                name: op.Name!,
                description: op.Description!,
                inputs: (op.Inputs ?? new List<ParameterDto>()).Select(MapParameter).ToList()))
            .ToList();

        var skills = (dto.Skills ?? new List<SkillDto>())
            .Select(s => new Skill(
                id: new SkillId(s.Id!),
                moduleId: moduleId,
                name: s.Name!,
                description: s.Description!,
                inputs: (s.Inputs ?? new List<ParameterDto>()).Select(MapParameter).ToList(),
                outputs: (s.Outputs ?? new List<ParameterDto>()).Select(MapParameter).ToList(),
                classification: ParseClassification(s.Classification!),
                kind: ParseSkillKind(s.Kind),
                sourcePath: s.SourcePath))
            .ToList();

        var dependencies = (dto.Dependencies ?? new List<DependencyDto>())
            .Select(d => new ModuleDependency(
                new ModuleId(d.Id!),
                SemanticVersionRange.Parse(d.Version!)))
            .ToList();

        var mcpServers = (dto.McpServers ?? new List<McpServerDto>())
            .Select(m => new McpServerDescriptor(
                m.Name!,
                m.Command!,
                m.Args ?? new List<string>(),
                m.Env ?? new Dictionary<string, string>()))
            .ToList();

        var prompts = (dto.Prompts ?? new List<PromptDto>())
            .Select(p => new PromptTemplate(p.Name!, p.Body!))
            .ToList();

        var policies = (dto.Policies ?? new List<PolicyDto>())
            .Select(p => new ModulePolicy(
                p.ActionClass!,
                ParseClassification(p.Classification!),
                p.Reason ?? string.Empty))
            .ToList();

        return new Module(
            id: moduleId,
            version: version,
            name: dto.Name!,
            description: dto.Description!,
            schemaVersion: dto.SchemaVersion,
            dependencies: dependencies,
            operations: operations,
            skills: skills,
            mcpServers: mcpServers,
            prompts: prompts,
            policies: policies,
            source: source);
    }

    private static SkillParameter MapParameter(ParameterDto p) => new(
        name: p.Name!,
        kind: ParseParameterKind(p.Kind!),
        required: p.Required ?? false,
        description: p.Description ?? string.Empty);

    private static SkillParameterKind ParseParameterKind(string raw) => raw switch
    {
        "string" => SkillParameterKind.String,
        "integer" => SkillParameterKind.Integer,
        "number" => SkillParameterKind.Number,
        "boolean" => SkillParameterKind.Boolean,
        "path" => SkillParameterKind.Path,
        "url" => SkillParameterKind.Url,
        "json" => SkillParameterKind.Json,
        _ => throw new ArgumentException($"Unknown parameter kind: {raw}", nameof(raw)),
    };

    private static ActionClassification ParseClassification(string raw) => raw switch
    {
        "safe" => ActionClassification.Safe,
        "dangerous" => ActionClassification.Dangerous,
        _ => throw new ArgumentException($"Unknown classification: {raw}", nameof(raw)),
    };

    private static SkillKind ParseSkillKind(string? raw) => raw switch
    {
        null or "automated" => SkillKind.Automated,
        "human" => SkillKind.Human,
        _ => throw new ArgumentException($"Unknown skill kind: {raw}", nameof(raw)),
    };

    private static ModuleId? TryParseId(string? raw) =>
        string.IsNullOrWhiteSpace(raw) ? null : new ModuleId(raw);

    private static SemanticVersion? TryParseVersion(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        return SemanticVersion.TryParse(raw, out var v) ? v : null;
    }

    private async Task<JsonSchema> LoadSchemaAsync()
    {
        var assembly = typeof(ModuleManifestValidator).Assembly;
        const string ResourceName = "AgentDesktop.Application.Modules.module.schema.json";
        await using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Cannot read embedded schema: {ResourceName}");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        return await JsonSchema.FromJsonAsync(json).ConfigureAwait(false);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed class ManifestDto
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("dependencies")] public List<DependencyDto>? Dependencies { get; set; }
        [JsonPropertyName("operations")] public List<OperationDto>? Operations { get; set; }
        [JsonPropertyName("skills")] public List<SkillDto>? Skills { get; set; }
        [JsonPropertyName("mcpServers")] public List<McpServerDto>? McpServers { get; set; }
        [JsonPropertyName("prompts")] public List<PromptDto>? Prompts { get; set; }
        [JsonPropertyName("policies")] public List<PolicyDto>? Policies { get; set; }
    }

    private sealed class DependencyDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("version")] public string? Version { get; set; }
    }

    private sealed class OperationDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("inputs")] public List<ParameterDto>? Inputs { get; set; }
    }

    private sealed class SkillDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("classification")] public string? Classification { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("sourcePath")] public string? SourcePath { get; set; }
        [JsonPropertyName("inputs")] public List<ParameterDto>? Inputs { get; set; }
        [JsonPropertyName("outputs")] public List<ParameterDto>? Outputs { get; set; }
    }

    private sealed class ParameterDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("kind")] public string? Kind { get; set; }
        [JsonPropertyName("required")] public bool? Required { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class McpServerDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("command")] public string? Command { get; set; }
        [JsonPropertyName("args")] public List<string>? Args { get; set; }
        [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }
    }

    private sealed class PromptDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
    }

    private sealed class PolicyDto
    {
        [JsonPropertyName("actionClass")] public string? ActionClass { get; set; }
        [JsonPropertyName("classification")] public string? Classification { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }
}
