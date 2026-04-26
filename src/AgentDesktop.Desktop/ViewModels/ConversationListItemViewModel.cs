using System.Globalization;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Desktop.ViewModels;

/// <summary>One row in the sidebar conversation list.</summary>
public sealed class ConversationListItemViewModel : ViewModelBase
{
    public ConversationListItemViewModel(ConversationId id, string title, DateTimeOffset lastActivityAt)
    {
        Id = id;
        Title = title;
        LastActivityAt = lastActivityAt;
    }

    public ConversationId Id { get; }
    public string Title { get; }
    public DateTimeOffset LastActivityAt { get; }

    public string Subtitle => LastActivityAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture);

    public static ConversationListItemViewModel From(Conversation c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new ConversationListItemViewModel(c.Id, c.Title, c.LastActivityAt);
    }
}
