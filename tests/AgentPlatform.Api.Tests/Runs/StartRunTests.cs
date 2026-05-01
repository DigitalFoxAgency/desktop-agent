using System.Net;
using System.Net.Http.Json;
using AgentPlatform.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Api.Tests.Runs;

[Collection(ApiCollection.Name)]
public sealed class StartRunTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    [Fact]
    public async Task Post_runs_starts_run_and_returns_runId()
    {
        var slug = "run-" + Guid.NewGuid().ToString("N")[..8];
        var client = await _factory.SignUpAndAuthenticateAsync(slug, $"{slug}@example.com");

        var resp = await client.PostAsJsonAsync("/api/runs", new
        {
            moduleId = "df-client-launchpad",
            workflowId = "onboard-client",
            inputs = new Dictionary<string, string?>
            {
                ["niche"] = "law firm",
                ["city"] = "Praha",
                ["clientName"] = "Acme",
            },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<RunResponse>();
        body!.RunId.Should().NotBeEmpty();
        body.PhaseRunId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Post_runs_rejects_missing_required_input()
    {
        var slug = "run2-" + Guid.NewGuid().ToString("N")[..8];
        var client = await _factory.SignUpAndAuthenticateAsync(slug, $"{slug}@example.com");

        var resp = await client.PostAsJsonAsync("/api/runs", new
        {
            moduleId = "df-client-launchpad",
            workflowId = "onboard-client",
            inputs = new Dictionary<string, string?>(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_runs_rejects_unknown_workflow()
    {
        var slug = "run3-" + Guid.NewGuid().ToString("N")[..8];
        var client = await _factory.SignUpAndAuthenticateAsync(slug, $"{slug}@example.com");

        var resp = await client.PostAsJsonAsync("/api/runs", new
        {
            moduleId = "df-client-launchpad",
            workflowId = "does-not-exist",
            inputs = new Dictionary<string, string?>(),
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private sealed record RunResponse(Guid RunId, Guid PhaseRunId, Guid? AssignedUserId);
}
