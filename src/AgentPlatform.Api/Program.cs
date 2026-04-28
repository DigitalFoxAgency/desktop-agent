var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

namespace AgentPlatform.Api
{
    /// <summary>Public marker so test hosts can reference the assembly.</summary>
    public partial class Program;
}
