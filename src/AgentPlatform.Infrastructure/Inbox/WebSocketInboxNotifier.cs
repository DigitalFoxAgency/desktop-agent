using AgentPlatform.Application.Abstractions;
using AgentPlatform.Domain.Inbox;

namespace AgentPlatform.Infrastructure.Inbox;

/// <summary>
/// Pushes inbox events to the assignee's open <c>/ws/inbox</c> connections (if any).
/// </summary>
public sealed class WebSocketInboxNotifier(InboxConnectionRegistry registry) : IInboxNotifier
{
    private readonly InboxConnectionRegistry _registry = registry;

    public Task NotifyAsync(InboxItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _registry.SendAsync(item.UserId, new
        {
            type = "inbox_item_added",
            id = item.Id,
            phaseRunId = item.PhaseRunId,
            kind = item.Kind.ToString(),
            title = item.Title,
            subtitle = item.Subtitle,
            createdAt = item.CreatedAt,
        }, cancellationToken);
    }
}
