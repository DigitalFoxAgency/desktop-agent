using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgentPlatform.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Api.Tests.Integration;

/// <summary>
/// End-to-end US1 acceptance: signup → signin → add engineer → start onboard-client → engineer's inbox shows init.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StartWorkflowFlowTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    [Fact]
    public async Task Full_flow_signup_to_engineer_inbox()
    {
        var slug = "flow-" + Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin-{slug}@example.com";
        var engineerEmail = $"eng-{slug}@example.com";

        var admin = _factory.CreateClient();
        var signupResp = await admin.PostAsJsonAsync("/api/auth/signup", new
        {
            agencyName = "Flow Agency",
            agencySlug = slug,
            email = adminEmail,
            password = "Aa1aaaaaaaa",
            displayName = "Admin",
        });
        signupResp.EnsureSuccessStatusCode();
        var adminAuth = await signupResp.Content.ReadFromJsonAsync<ApiFactory.SignInResponseDto>();
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth!.Token);

        var addEngineer = await admin.PostAsJsonAsync($"/api/tenants/{adminAuth.TenantId}/users", new
        {
            email = engineerEmail,
            password = "Aa1aaaaaaaa",
            displayName = "Engineer",
            role = "engineer",
        });
        addEngineer.EnsureSuccessStatusCode();

        var startResp = await admin.PostAsJsonAsync("/api/runs", new
        {
            moduleId = "df-client-launchpad",
            workflowId = "onboard-client",
            inputs = new Dictionary<string, string?>
            {
                ["niche"] = "dental",
                ["city"] = "Brno",
                ["clientName"] = "DentaPro",
            },
        });
        startResp.EnsureSuccessStatusCode();
        var run = await startResp.Content.ReadFromJsonAsync<RunResponse>();
        run!.RunId.Should().NotBeEmpty();
        run.AssignedUserId.Should().NotBeNull();

        var engineer = _factory.CreateClient();
        var engineerSignIn = await engineer.PostAsJsonAsync("/api/auth/signin", new { email = engineerEmail, password = "Aa1aaaaaaaa" });
        engineerSignIn.EnsureSuccessStatusCode();
        var engineerAuth = await engineerSignIn.Content.ReadFromJsonAsync<ApiFactory.SignInResponseDto>();
        engineer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", engineerAuth!.Token);
        engineerAuth.Roles.Should().Contain("engineer");

        var inboxResp = await engineer.GetAsync(new Uri("/api/inbox", UriKind.Relative));
        inboxResp.EnsureSuccessStatusCode();
        var items = await inboxResp.Content.ReadFromJsonAsync<List<InboxItem>>();
        items.Should().ContainSingle(i => i.PhaseRunId == run.PhaseRunId);

        // /api/runs returns the run we just started.
        var listResp = await admin.GetAsync(new Uri("/api/runs", UriKind.Relative));
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<RunListItem>>();
        list.Should().Contain(r => r.Id == run.RunId);
    }

    private sealed record RunResponse(Guid RunId, Guid PhaseRunId, Guid? AssignedUserId);
    private sealed record InboxItem(Guid Id, Guid PhaseRunId, string Title, string? Subtitle, string Kind, DateTimeOffset CreatedAt);
    private sealed record RunListItem(Guid Id, string ModuleId, string WorkflowId);
}
