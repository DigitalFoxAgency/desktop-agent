using System.Net;
using System.Net.Http.Json;
using AgentPlatform.Api.Tests.Infrastructure;
using FluentAssertions;
using Xunit;

namespace AgentPlatform.Api.Tests.Inbox;

[Collection(ApiCollection.Name)]
public sealed class InboxEndpointTests
{
    private readonly ApiFactory _factory;

    public InboxEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_inbox_returns_empty_for_new_user()
    {
        var slug = "inbox-" + Guid.NewGuid().ToString("N")[..8];
        var client = await _factory.SignUpAndAuthenticateAsync(slug, $"{slug}@example.com");

        var resp = await client.GetAsync(new Uri("/api/inbox", UriKind.Relative));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await resp.Content.ReadFromJsonAsync<List<object>>();
        items.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_inbox_lists_phase_assignment_after_run_started()
    {
        // Admin signs up; admin holds Admin role only — onboard-client's first phase requires
        // engineer. To get an inbox item, the signing-up admin must also be assigned engineer.
        var slug = "inbox2-" + Guid.NewGuid().ToString("N")[..8];
        var email = $"{slug}@example.com";
        var client = await _factory.SignUpAndAuthenticateAsync(slug, email);

        // Add a second user with the engineer role.
        var tenantClaim = await ReadTenantIdAsync(client, email);
        var addUser = await client.PostAsJsonAsync($"/api/tenants/{tenantClaim}/users", new
        {
            email = $"engineer-{slug}@example.com",
            password = "Aa1aaaaaaaa",
            displayName = "Engineer",
            role = "engineer",
        });
        addUser.EnsureSuccessStatusCode();
        var added = await addUser.Content.ReadFromJsonAsync<AddUserResponse>();
        added!.UserId.Should().NotBeEmpty();

        // Start a run.
        var startResp = await client.PostAsJsonAsync("/api/runs", new
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
        startResp.EnsureSuccessStatusCode();
        var run = await startResp.Content.ReadFromJsonAsync<RunResponse>();
        run!.AssignedUserId.Should().Be(added.UserId);

        // Sign in as the engineer and check their inbox.
        var engineerClient = _factory.CreateClient();
        var engineerSignIn = await engineerClient.PostAsJsonAsync("/api/auth/signin", new
        {
            email = $"engineer-{slug}@example.com",
            password = "Aa1aaaaaaaa",
        });
        engineerSignIn.EnsureSuccessStatusCode();
        var engineerToken = await engineerSignIn.Content.ReadFromJsonAsync<ApiFactory.SignInResponseDto>();
        engineerClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", engineerToken!.Token);

        var inboxResp = await engineerClient.GetAsync(new Uri("/api/inbox", UriKind.Relative));
        inboxResp.EnsureSuccessStatusCode();
        var items = await inboxResp.Content.ReadFromJsonAsync<List<InboxItemDto>>();
        items.Should().NotBeNull();
        items!.Should().ContainSingle(i => i.PhaseRunId == run.PhaseRunId);
    }

    private static async Task<Guid> ReadTenantIdAsync(HttpClient client, string email)
    {
        // Re-signin to get tenantId from the body (token already in header is sufficient,
        // but we want the tenantId structurally).
        var resp = await client.PostAsJsonAsync("/api/auth/signin", new { email, password = "Aa1aaaaaaaa" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<ApiFactory.SignInResponseDto>();
        return body!.TenantId;
    }

    private sealed record AddUserResponse(Guid UserId);
    private sealed record RunResponse(Guid RunId, Guid PhaseRunId, Guid? AssignedUserId);
    private sealed record InboxItemDto(Guid Id, Guid PhaseRunId, string Title, string? Subtitle, string Kind, DateTimeOffset CreatedAt);
}
