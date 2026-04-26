using AgentDesktop.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseStatusCodePages();
app.MapSubscriptionEndpoints();

await app.RunAsync().ConfigureAwait(false);

namespace AgentDesktop.Api
{
    /// <summary>Public marker type so WebApplicationFactory tests can target this assembly.</summary>
    public partial class Program;
}
