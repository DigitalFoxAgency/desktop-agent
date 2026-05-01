using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Runs;

namespace AgentPlatform.Api.Endpoints;

public static class InboxEndpoints
{
    public static IEndpointRouteBuilder MapInboxEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/api/inbox", async (IRequestTenantContext ctx, IWorkflowRunRepository repo, CancellationToken ct, int? limit) =>
        {
            var items = await repo.ListInboxWithContextAsync(ctx.TenantId, ctx.UserId, limit ?? 50, ct);
            return Results.Ok(items.Select(c => new
            {
                c.Item.Id,
                c.Item.PhaseRunId,
                c.Item.Title,
                c.Item.Subtitle,
                Kind = c.Item.Kind.ToString(),
                c.Item.CreatedAt,
                // Lets the UI filter active vs done without a separate request.
                PhaseStatus = (int)c.PhaseStatus,
                c.WorkflowRunId,
                c.ModuleId,
                c.WorkflowId,
                Inputs = ParseInputs(c.InputsJson),
            }));
        }).RequireAuthorization();

        return app;
    }

    private static Dictionary<string, string?> ParseInputs(string? inputsJson)
    {
        if (string.IsNullOrWhiteSpace(inputsJson)) { return new Dictionary<string, string?>(); }
        try
        {
            using var doc = JsonDocument.Parse(inputsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) { return new Dictionary<string, string?>(); }
            var result = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
            {
                result[p.Name] = p.Value.ValueKind == JsonValueKind.String
                    ? p.Value.GetString()
                    : p.Value.GetRawText();
            }
            return result;
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>();
        }
    }
}
