using System.Net;
using System.Net.Http.Json;
using AgentPlatform.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Api.Tests.Modules;

[Collection(ApiCollection.Name)]
public sealed class ModuleCatalogueTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    [Fact]
    public async Task Get_modules_requires_auth()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync(new Uri("/api/modules", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_modules_lists_launchpad_and_workflows()
    {
        var slug = "cat-" + Guid.NewGuid().ToString("N")[..8];
        var client = await _factory.SignUpAndAuthenticateAsync(slug, $"{slug}@example.com");

        var resp = await client.GetAsync(new Uri("/api/modules", UriKind.Relative));
        resp.EnsureSuccessStatusCode();
        var modules = await resp.Content.ReadFromJsonAsync<List<ModuleResponse>>();
        modules.Should().NotBeNull();
        var launchpad = modules!.SingleOrDefault(m => m.Id == "df-client-launchpad");
        launchpad.Should().NotBeNull();
        launchpad!.Workflows.Select(w => w.Id).Should().Contain(ExpectedWorkflows);
    }

    private static readonly string[] ExpectedWorkflows = ["onboard-client", "launch-ads", "monthly-report"];

    private sealed record ModuleResponse(string Id, string Name, string Status, string? UnavailableReason, List<WorkflowResponse> Workflows);
    private sealed record WorkflowResponse(string Id, string Name);
}
