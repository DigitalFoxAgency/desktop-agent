using System.Text.Json;
using AgentPlatform.Application.Modules;
using AgentPlatform.Domain.Modules;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Api.Endpoints;

public static class ModuleEndpoints
{
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/modules", async (IModuleRegistry registry, CancellationToken ct) =>
        {
            var modules = await registry.ListAsync(ct);
            var dtos = modules.Select(m => ToDto(m)).ToList();
            return Results.Ok(dtos);
        }).RequireAuthorization();

        return app;
    }

    private static ModuleDto ToDto(Module module)
    {
        var workflows = new List<WorkflowDto>();
        if (module.Versions.Count > 0 && module.Status == ModuleStatus.Available)
        {
            var version = module.Versions[^1];
            foreach (var w in ManifestParser.ExtractAllWorkflows(module, version))
            {
                workflows.Add(new WorkflowDto(
                    w.WorkflowId,
                    w.DisplayName,
                    w.Description,
                    JsonDocument.Parse(w.InputsSchemaJson).RootElement.Clone(),
                    w.Phases
                        .OrderBy(p => p.Order)
                        .Select(p => new PhaseDto(p.PhaseId, p.DisplayName, RoleNames.ToSlug(p.Role), p.Skill, p.Kind.ToString().ToLowerInvariant()))
                        .ToArray()));
            }
        }
        return new ModuleDto(
            module.ModuleId,
            module.DisplayName,
            module.Status.ToString(),
            module.UnavailableReason,
            workflows);
    }

    public sealed record ModuleDto(string Id, string Name, string Status, string? UnavailableReason, IReadOnlyList<WorkflowDto> Workflows);
    public sealed record WorkflowDto(string Id, string Name, string? Description, JsonElement Inputs, IReadOnlyList<PhaseDto> Phases);
    public sealed record PhaseDto(string Id, string Name, string Role, string Skill, string Kind);
}
