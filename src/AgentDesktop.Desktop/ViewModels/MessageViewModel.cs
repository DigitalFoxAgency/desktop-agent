using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentDesktop.Desktop.ViewModels;

/// <summary>Chat-surface adapter for a domain <see cref="Message"/>.</summary>
public sealed partial class MessageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _body;

    public MessageViewModel(MessageId id, MessageAuthor author, string body)
    {
        Id = id;
        Author = author;
        _body = body;
    }

    public MessageId Id { get; }
    public MessageAuthor Author { get; }

    public bool IsUser => Author == MessageAuthor.User;
    public bool IsAgent => Author == MessageAuthor.Agent;
    public bool IsSystem => Author == MessageAuthor.System;

    public string AuthorLabel => Author switch
    {
        MessageAuthor.User => "You",
        MessageAuthor.Agent => "Agent",
        MessageAuthor.System => "System",
        _ => Author.ToString(),
    };

    public void Append(string delta)
    {
        Body += delta;
    }

    public static MessageViewModel From(Message message)
    {
        System.ArgumentNullException.ThrowIfNull(message);
        return new MessageViewModel(message.Id, message.Author, message.Body);
    }
}
