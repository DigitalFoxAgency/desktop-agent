using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Application.Runs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace AgentPlatform.Api.Hubs;

/// <summary>
/// WebSocket endpoint at <c>/ws/phase/{phaseRunId}</c> consumed by the web client. Streams
/// assistant chunks, file-tree events, confirmation requests, and token usage out; routes
/// user input + confirmation decisions in.
/// </summary>
public static class PhaseSessionHub
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapPhaseSessionHub(this IEndpointRouteBuilder app)
    {
        app.Map("/ws/phase/{phaseRunId:guid}", async (Guid phaseRunId, HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Browsers can't easily attach an Authorization header to a WebSocket open;
            // accept the JWT via ?token= query string as a fallback.
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

            var sessions = ctx.RequestServices.GetRequiredService<IPhaseSessionService>();
            var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PhaseHub");

            var handle = sessions.GetActive(phaseRunId);
            if (handle is null)
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            using var socket = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
            var sendLock = new SemaphoreSlim(1, 1);

            async Task Send(object payload)
            {
                if (socket.State != WebSocketState.Open)
                {
                    return;
                }
                var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
                await sendLock.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
                }
                finally
                {
                    sendLock.Release();
                }
            }

            var pumpEvents = Task.Run(async () =>
            {
                try
                {
                    await foreach (var evt in handle.Channel.ReadEventsAsync(cts.Token).ConfigureAwait(false))
                    {
                        var payload = MapEvent(evt);
                        if (payload is not null)
                        {
                            await Send(payload).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) { /* shutdown */ }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Phase event pump failed");
                }
            }, cts.Token);

            var pumpInput = Task.Run(async () =>
            {
                var buffer = new byte[8 * 1024];
                var sb = new StringBuilder();
                try
                {
                    while (!cts.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        var result = await socket.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                        if (!result.EndOfMessage)
                        {
                            continue;
                        }
                        var json = sb.ToString();
                        sb.Clear();
                        await DispatchClientFrame(json, handle.Channel, cts.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { /* shutdown */ }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Phase input pump failed");
                }
            }, cts.Token);

            await Task.WhenAny(pumpEvents, pumpInput).ConfigureAwait(false);
            await cts.CancelAsync().ConfigureAwait(false);
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

    private static object? MapEvent(BridgeEvent evt) => evt switch
    {
        BridgeEvent.AssistantChunk c => new { type = "assistant_chunk", text = c.Text },
        BridgeEvent.AssistantTurnComplete => new { type = "assistant_turn_complete" },
        BridgeEvent.FileChanged f => new { type = "file_changed", path = f.RelativePath, kind = f.Kind.ToString().ToLowerInvariant() },
        BridgeEvent.ConfirmationRequested cr => new
        {
            type = "confirmation_request",
            confirmationId = cr.ConfirmationId,
            classification = cr.Classification.ToString(),
            summary = cr.Summary,
            targetPath = cr.TargetPath,
            commandLine = cr.CommandLine,
        },
        BridgeEvent.TokenUsage u => new
        {
            type = "token_usage",
            model = u.Model,
            inputTokens = u.InputTokens,
            outputTokens = u.OutputTokens,
            cacheCreationTokens = u.CacheCreationTokens,
            cacheReadTokens = u.CacheReadTokens,
        },
        BridgeEvent.PhaseCompleted pc => new { type = "phase_completed", skill = pc.Skill, verified = pc.Verified },
        _ => null,
    };

    private static async Task DispatchClientFrame(string json, IBridgeChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            {
                return;
            }
            var t = typeProp.GetString();
            if (t == "user_input" && doc.RootElement.TryGetProperty("text", out var text))
            {
                await channel.SendUserInputAsync(text.GetString() ?? string.Empty, cancellationToken).ConfigureAwait(false);
            }
            else if (t == "confirmation_decision"
                && doc.RootElement.TryGetProperty("confirmationId", out var idProp)
                && doc.RootElement.TryGetProperty("confirmed", out var confirmedProp))
            {
                var id = idProp.GetGuid();
                var confirmed = confirmedProp.GetBoolean();
                var note = doc.RootElement.TryGetProperty("note", out var n) ? n.GetString() : null;
                await channel.ResolveConfirmationAsync(id, confirmed, note, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (JsonException) { /* drop unparseable */ }
    }
}
