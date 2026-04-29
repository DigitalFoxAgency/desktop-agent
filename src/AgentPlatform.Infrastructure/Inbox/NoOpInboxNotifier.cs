using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Inbox;

namespace AgentPlatform.Infrastructure.Inbox;

/// <summary>
/// Default inbox notifier — does nothing. WebSocket-backed implementation lands in US3 (T122).
/// </summary>
public sealed class NoOpInboxNotifier : IInboxNotifier
{
    public Task NotifyAsync(InboxItem item, CancellationToken cancellationToken) => Task.CompletedTask;
}
