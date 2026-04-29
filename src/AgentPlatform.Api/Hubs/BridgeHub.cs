using AgentPlatform.Infrastructure.Bridge;

namespace AgentPlatform.Api.Hubs;

/// <summary>
/// WebSocket endpoint at <c>/ws/bridge</c> for the per-run Bridge process. Authentication
/// is by short-lived token (issued at phase open). Successful handshakes register a
/// <see cref="WebSocketBridgeChannel"/> in the connection registry.
/// </summary>
public static class BridgeHub
{
    public static IEndpointRouteBuilder MapBridgeHub(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/bridge", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var registry = ctx.RequestServices.GetRequiredService<BridgeConnectionRegistry>();
            var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("BridgeHub");

            var token = ctx.Request.Headers["X-Bridge-Token"].ToString();
            if (!registry.TryConsumeToken(token, out var phaseRunId, out var tenantId))
            {
                log.LogWarning("Bridge handshake rejected: invalid/expired token");
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            var channel = registry.RegisterConnection(phaseRunId, tenantId, socket);
            try
            {
                await channel.ReaderCompletion.ConfigureAwait(false);
            }
            finally
            {
                registry.Forget(phaseRunId);
            }
        });

        return app;
    }
}
