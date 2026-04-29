using AgentPlatform.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace AgentPlatform.Infrastructure.Tests;

/// <summary>
/// xUnit collection fixture: spins up a real Postgres container, applies the EF migrations,
/// and exposes a typed DbContext factory. Tests get a clean context per fact via the
/// ICollectionFixture pattern.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("agentplatform")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CreateContext();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public AgentPlatformDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgentPlatformDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AgentPlatformDbContext(options);
    }
}

[CollectionDefinition(Name)]
#pragma warning disable CA1711 // xUnit collection definitions are conventionally named "...Collection".
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>
#pragma warning restore CA1711
{
    public const string Name = "Postgres";
}
