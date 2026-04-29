using AgentPlatform.Bridge.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection("Bridge"));
builder.Services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true));

using var host = builder.Build();
var log = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Bridge");
log.LogInformation("AgentPlatform.Bridge starting (skeleton). Real wrapper / transport / watcher land in Phase 4 (T101–T103).");
await host.RunAsync().ConfigureAwait(false);
return 0;

namespace AgentPlatform.Bridge
{
    /// <summary>Public marker so test hosts can reference the assembly.</summary>
    public partial class Program;
}
