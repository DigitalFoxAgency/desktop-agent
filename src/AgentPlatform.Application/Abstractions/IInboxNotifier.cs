using AgentPlatform.Domain.Inbox;

namespace AgentPlatform.Application.Abstractions;

public interface IInboxNotifier
{
    Task NotifyAsync(InboxItem item, CancellationToken cancellationToken);
}
