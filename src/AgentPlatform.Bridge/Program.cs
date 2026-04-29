using AgentPlatform.Bridge.ClaudeWrapper;
using AgentPlatform.Bridge.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "AGP_");
builder.Services.Configure<BridgeOptions>(o =>
{
    var cfg = builder.Configuration;
    if (Guid.TryParse(cfg["RUN_ID"], out var runId))
    {
        o.RunId = runId;
    }
    if (Guid.TryParse(cfg["PHASE_RUN_ID"], out var phaseId))
    {
        o.PhaseRunId = phaseId;
    }
    o.WorkingDir = cfg["WORKING_DIR"] ?? o.WorkingDir;
    o.ApiWebSocketUrl = cfg["BRIDGE_URL"] ?? o.ApiWebSocketUrl;
    o.BridgeToken = cfg["BRIDGE_TOKEN"] ?? o.BridgeToken;
    o.Skill = cfg["SKILL"] ?? o.Skill;
    o.ClaudeBinary = cfg["CLAUDE_BINARY"] ?? o.ClaudeBinary;
});

builder.Services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true));
builder.Services.AddSingleton<StreamJsonOptions>(sp =>
    new StreamJsonOptions { ClaudeBinary = sp.GetRequiredService<IOptions<BridgeOptions>>().Value.ClaudeBinary });
builder.Services.AddSingleton<IClaudeWrapper, StreamJsonClaudeWrapper>();
builder.Services.AddHostedService<AgentPlatform.Bridge.BridgeRuntime>();

using var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
return 0;

namespace AgentPlatform.Bridge
{
    /// <summary>Public marker so test hosts can reference the assembly.</summary>
    public partial class Program;
}
