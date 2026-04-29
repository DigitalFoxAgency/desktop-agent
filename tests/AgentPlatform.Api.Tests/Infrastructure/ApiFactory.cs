using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace AgentPlatform.Api.Tests.Infrastructure;

/// <summary>
/// Boots the API against a real Postgres in a Testcontainer and a temporary modules root
/// containing the launchpad manifest. Shared across the Api.Tests assembly.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("agentplatform")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private string _modulesRoot = string.Empty;

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _modulesRoot = SeedTempModulesRoot();
    }

    public new async Task DisposeAsync()
    {
        if (Directory.Exists(_modulesRoot))
        {
            try { Directory.Delete(_modulesRoot, recursive: true); } catch (IOException) { }
        }
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseEnvironment("Test");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["AgentPlatform:ModulesRoot"] = _modulesRoot,
                ["Jwt:SigningKey"] = "test-only-signing-key-with-enough-bytes-1234567890",
            });
        });
        builder.ConfigureTestServices(services =>
        {
            // Speed: nothing to override here; the real services run against Testcontainer Postgres.
        });
    }

    private static string SeedTempModulesRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "agp-api-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // Copy the real launchpad manifest into a sibling folder. Schema lives next to the
        // built FileSystemModuleSource binary (already copied to the API output via
        // module.schema.json's CopyToOutputDirectory in AgentPlatform.Infrastructure.csproj).
        var manifestSource = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "modules", "df-client-launchpad", "module.json"));
        if (File.Exists(manifestSource))
        {
            var dst = Path.Combine(root, "df-client-launchpad");
            Directory.CreateDirectory(dst);
            File.Copy(manifestSource, Path.Combine(dst, "module.json"));
        }
        return root;
    }

    public async Task<HttpClient> SignUpAndAuthenticateAsync(string slug = "agency-test", string email = "admin@example.com")
    {
        var client = CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/signup", new
        {
            agencyName = "Test Agency",
            agencySlug = slug,
            email,
            password = "Aa1aaaaaaaa",
            displayName = "Admin User",
        });
        resp.EnsureSuccessStatusCode();
        var payload = await resp.Content.ReadFromJsonAsync<SignInResponseDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload!.Token);
        return client;
    }

    public sealed record SignInResponseDto(string Token, Guid TenantId, Guid UserId, string[] Roles);
}

[CollectionDefinition(Name)]
#pragma warning disable CA1711
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
#pragma warning restore CA1711
{
    public const string Name = "Api";
}
