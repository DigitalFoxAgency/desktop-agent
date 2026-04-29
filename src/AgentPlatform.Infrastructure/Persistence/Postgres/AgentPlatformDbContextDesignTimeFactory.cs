using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AgentPlatform.Infrastructure.Persistence.Postgres;

/// <summary>
/// Used by `dotnet ef` tooling at design time. Production wiring is in AgentPlatform.Api.
/// </summary>
public sealed class AgentPlatformDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AgentPlatformDbContext>
{
    public AgentPlatformDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("AGENTPLATFORM_DB")
            ?? "Host=localhost;Port=5432;Database=agentplatform;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AgentPlatformDbContext>()
            .UseNpgsql(connection)
            .Options;
        return new AgentPlatformDbContext(options);
    }
}
