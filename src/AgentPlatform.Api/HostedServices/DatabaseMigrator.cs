using AgentPlatform.Api.Auth;
using AgentPlatform.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AgentPlatform.Api.HostedServices;

/// <summary>
/// Applies EF Core migrations for the agency context and ensures Identity tables exist
/// (Identity migrations are out of scope until T161; we materialise them via the
/// relational database creator since EnsureCreated short-circuits when the agency
/// migration has already populated the database).
/// </summary>
public sealed class DatabaseMigrator(IServiceProvider services, ILogger<DatabaseMigrator> logger) : IHostedService
{
    private readonly IServiceProvider _services = services;
    private readonly ILogger<DatabaseMigrator> _logger = logger;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var agency = scope.ServiceProvider.GetRequiredService<AgentPlatformDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        try
        {
            await agency.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

            if (!await IdentitySchemaExistsAsync(identity, cancellationToken).ConfigureAwait(false))
            {
                await identity.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS identity;", cancellationToken).ConfigureAwait(false);
                var creator = (RelationalDatabaseCreator)identity.GetService<IRelationalDatabaseCreator>();
                try
                {
                    await creator.CreateTablesAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Identity schema created.");
                }
                catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P07")
                {
                    _logger.LogInformation("Identity tables already exist; skipping create.");
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Database migration failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task<bool> IdentitySchemaExistsAsync(IdentityDbContext db, CancellationToken cancellationToken)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='identity' AND table_name='AspNetUsers');";
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is bool b && b;
    }
}
