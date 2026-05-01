using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace AgentPlatform.Api.Logging;

/// <summary>
/// Structured-logging setup. Emits Serilog's CompactJsonFormatter to stdout
/// so container log drivers (Docker, Kubernetes) can ship lines straight to
/// the agency's log pipeline. Microsoft / EF Core noise is dialled down to
/// Warning to keep request logs readable.
/// </summary>
public static class SerilogConfig
{
    public static void ConfigureLogger(IHostBuilder host)
    {
        ArgumentNullException.ThrowIfNull(host);
        host.UseSerilog((ctx, services, cfg) =>
        {
            cfg
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", LogEventLevel.Information)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("service", "agentplatform-api")
                .ReadFrom.Configuration(ctx.Configuration)
                .ReadFrom.Services(services)
                .WriteTo.Console(new CompactJsonFormatter());
        });
    }
}
