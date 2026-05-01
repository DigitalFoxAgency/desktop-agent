using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Modules;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Modules;

/// <summary>
/// In-memory module registry hydrated from <see cref="IModuleSource"/> on startup
/// (and on demand via <see cref="RefreshAsync"/>). Manifests that violate the schema
/// or declare an unknown <c>schemaVersion</c> are surfaced as <see cref="ModuleStatus.Unavailable"/>.
/// </summary>
public sealed class ModuleRegistry(IModuleSource source, IClock clock) : IModuleRegistry
{
    private readonly IModuleSource _source = source;
    private readonly IClock _clock = clock;
    private ConcurrentDictionary<string, Module> _byModuleId = new();

    public Task<IReadOnlyList<Module>> ListAsync(CancellationToken cancellationToken)
    {
        var snapshot = (IReadOnlyList<Module>)_byModuleId.Values.OrderBy(m => m.ModuleId, StringComparer.Ordinal).ToList();
        return Task.FromResult(snapshot);
    }

    public Task<Module?> GetAsync(string moduleId, CancellationToken cancellationToken)
    {
        _byModuleId.TryGetValue(moduleId, out var module);
        return Task.FromResult(module);
    }

    public Task<WorkflowDef?> GetWorkflowAsync(string moduleId, string workflowId, CancellationToken cancellationToken)
    {
        if (!_byModuleId.TryGetValue(moduleId, out var module) || module.Versions.Count == 0)
        {
            return Task.FromResult<WorkflowDef?>(null);
        }
        var latest = module.Versions[^1];
        var workflow = ManifestParser.ExtractWorkflow(module, latest, workflowId);
        return Task.FromResult(workflow);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var next = new ConcurrentDictionary<string, Module>(StringComparer.Ordinal);

        try
        {
            var discovered = await _source.DiscoverAsync(cancellationToken).ConfigureAwait(false);
            foreach (var item in discovered)
            {
                var module = new Module
                {
                    Id = ManifestParser.DeterministicGuid($"module:{item.ModuleId}"),
                    ModuleId = item.ModuleId,
                    DisplayName = item.DisplayName,
                    SourcePath = item.SourcePath,
                    Status = ModuleStatus.Available,
                };
                module.Versions.Add(new ModuleVersion
                {
                    Id = ManifestParser.DeterministicGuid($"version:{item.ModuleId}:{item.Version}"),
                    ModuleId = module.Id,
                    Version = item.Version,
                    SchemaVersion = item.SchemaVersion,
                    ManifestJson = item.ManifestJson,
                    RegisteredAt = _clock.UtcNow,
                });
                next[item.ModuleId] = module;
            }
        }
        catch (InvalidOperationException ex)
        {
            // Schema/validation errors from FileSystemModuleSource — surface them as a
            // sentinel "Unavailable" module so the catalogue endpoint can show *why*
            // (rather than the API silently returning an empty list).
            const string SentinelId = "_invalid";
            next[SentinelId] = new Module
            {
                Id = ManifestParser.DeterministicGuid("module:_invalid"),
                ModuleId = SentinelId,
                DisplayName = "Invalid module",
                SourcePath = string.Empty,
                Status = ModuleStatus.Unavailable,
                UnavailableReason = ex.Message,
            };
        }

        _byModuleId = next;
    }
}

public static class ManifestParser
{
    public static Guid DeterministicGuid(string seed)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(seed), hash);
        Span<byte> bytes = stackalloc byte[16];
        hash[..16].CopyTo(bytes);
        return new Guid(bytes);
    }

    public static WorkflowDef? ExtractWorkflow(Module module, ModuleVersion version, string workflowId)
    {
        using var doc = JsonDocument.Parse(version.ManifestJson);
        if (!doc.RootElement.TryGetProperty("workflows", out var workflows) || workflows.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        foreach (var w in workflows.EnumerateArray())
        {
            var id = w.GetProperty("id").GetString();
            if (!string.Equals(id, workflowId, StringComparison.Ordinal))
            {
                continue;
            }

            var def = new WorkflowDef
            {
                Id = DeterministicGuid($"workflow:{module.ModuleId}:{version.Version}:{workflowId}"),
                ModuleVersionId = version.Id,
                WorkflowId = workflowId,
                DisplayName = w.GetProperty("name").GetString() ?? workflowId,
                Description = w.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                InputsSchemaJson = w.TryGetProperty("inputs", out var inputs) ? inputs.GetRawText() : "[]",
            };

            if (w.TryGetProperty("phases", out var phases) && phases.ValueKind == JsonValueKind.Array)
            {
                var order = 0;
                foreach (var p in phases.EnumerateArray())
                {
                    var phaseId = p.GetProperty("id").GetString()!;
                    def.Phases.Add(new PhaseDef
                    {
                        Id = DeterministicGuid($"phase:{module.ModuleId}:{version.Version}:{workflowId}:{phaseId}"),
                        WorkflowDefId = def.Id,
                        Order = order++,
                        PhaseId = phaseId,
                        DisplayName = p.GetProperty("name").GetString() ?? phaseId,
                        Role = RoleNames.FromSlug(p.GetProperty("role").GetString()!),
                        Skill = p.GetProperty("skill").GetString() ?? phaseId,
                        Kind = ParseKind(p.TryGetProperty("kind", out var k) ? k.GetString() : "standard"),
                    });
                }
            }
            return def;
        }
        return null;
    }

    public static IEnumerable<WorkflowDef> ExtractAllWorkflows(Module module, ModuleVersion version)
    {
        using var doc = JsonDocument.Parse(version.ManifestJson);
        if (!doc.RootElement.TryGetProperty("workflows", out var workflows) || workflows.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }
        foreach (var w in workflows.EnumerateArray())
        {
            var id = w.GetProperty("id").GetString();
            if (id is null) { continue; }
            var def = ExtractWorkflow(module, version, id);
            if (def is not null) { yield return def; }
        }
    }

    private static PhaseKind ParseKind(string? value) => value switch
    {
        "verification" => PhaseKind.Verification,
        "manual" => PhaseKind.Manual,
        _ => PhaseKind.Standard,
    };
}
