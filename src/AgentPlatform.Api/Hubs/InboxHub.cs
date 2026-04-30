using System.Net.WebSockets;
using System.Security.Claims;
using AgentPlatform.Infrastructure.Inbox;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace AgentPlatform.Api.Hubs;

/// <summary>
/// WebSocket endpoint at <c>/ws/inbox</c>. The web client opens one connection per
/// signed-in user; the API pushes <c>inbox_item_added</c> frames whenever a phase
/// hand-off lands in their queue.
/// </summary>
public static class InboxHub
{
    public static IEndpointRouteBuilder MapInboxHub(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/inbox", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (ctx.User.Identity?.IsAuthenticated != true
                && ctx.Request.Query.TryGetValue("token", out var qt)
                && !string.IsNullOrEmpty(qt))
            {
                ctx.Request.Headers.Authorization = $"Bearer {qt}";
                var auth = await ctx.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme).ConfigureAwait(false);
                if (auth.Succeeded && auth.Principal is not null)
                {
                    ctx.User = auth.Principal;
                }
            }

            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var sub = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? ctx.User.FindFirstValue("sub");
            if (!Guid.TryParse(sub, out var userId))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var registry = ctx.RequestServices.GetRequiredService<InboxConnectionRegistry>();
            using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            using var registration = registry.Register(userId, socket);

            // Hold the connection open; we don't expect inbound frames, but draining
            // them keeps the socket alive and lets the client signal close.
            var buffer = new byte[1024];
            try
            {
                while (socket.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(buffer, ctx.RequestAborted).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close) { break; }
                }
            }
            catch (OperationCanceledException) { /* shutdown */ }
            catch (WebSocketException) { /* client gone */ }

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (WebSocketException) { /* ignore */ }
        });

        return app;
    }
}
