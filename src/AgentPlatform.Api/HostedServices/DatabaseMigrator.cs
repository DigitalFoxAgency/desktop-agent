using AgentPlatform.Api.Auth;
using AgentPlatform.Infrastructure.Persistence.Postgres;
using Microsoft.EntityFrameworkCore;

namespace AgentPlatform.Api.HostedServices;

/// <summary>
/// Applies EF Core migrations for the agency context and ensures Identity tables exist
/// (Identity migrations are out of scope until T161; we use EnsureCreated for now).
/// </summary>
public sealed class DatabaseMigrator : IHostedService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseMigrator> _logger;

    public DatabaseMigrator(IServiceProvider services, ILogger<DatabaseMigrator> logger)
    {
        _services = services;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = _services.CreateAsyncScope();
        var agency = scope.ServiceProvider.GetRequiredService<AgentPlatformDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        try
        {
            await agency.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            await identity.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Database migration failed.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
