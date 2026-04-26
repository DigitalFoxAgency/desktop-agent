using System.Collections.ObjectModel;
using AgentDesktop.Application.Chat;
using AgentDesktop.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentDesktop.Desktop.ViewModels;

/// <summary>
/// Drives a single conversation: loads its history, sends user messages,
/// and renders the agent's streamed reply as it arrives.
/// </summary>
public sealed partial class ChatViewModel : ViewModelBase
{
    private readonly IChatService _chat;

    [ObservableProperty]
    private string _draft = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string _conversationTitle = string.Empty;

    [ObservableProperty]
    private bool _hasConversation;

    private ConversationId _activeConversationId;

    public ChatViewModel(IChatService chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
        Messages = new ObservableCollection<MessageViewModel>();
    }

    public ObservableCollection<MessageViewModel> Messages { get; }

    public ConversationId ActiveConversationId => _activeConversationId;

    public bool CanSend => HasConversation && !IsStreaming && !string.IsNullOrWhiteSpace(Draft);

    public event EventHandler? ConversationChanged;

    public async Task StartNewAsync(CancellationToken ct = default)
    {
        var convo = await _chat.StartConversationAsync(ct).ConfigureAwait(true);
        await OpenAsync(convo.Id, ct).ConfigureAwait(true);
    }

    public async Task OpenAsync(ConversationId id, CancellationToken ct = default)
    {
        var convo = await _chat.GetConversationAsync(id, ct).ConfigureAwait(true);
        _activeConversationId = convo.Id;
        ConversationTitle = convo.Title;
        Messages.Clear();
        foreach (var m in convo.Messages)
        {
            Messages.Add(MessageViewModel.From(m));
        }
        HasConversation = true;
        SendCommand.NotifyCanExecuteChanged();
        ConversationChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken ct)
    {
        if (!HasConversation)
        {
            return;
        }

        var body = Draft.Trim();
        if (string.IsNullOrEmpty(body))
        {
            return;
        }

        Draft = string.Empty;
        IsStreaming = true;

        Messages.Add(new MessageViewModel(MessageId.New(), MessageAuthor.User, body));

        MessageViewModel? agentBubble = null;
        try
        {
            // Do NOT break on IsFinal: the underlying async iterator persists
            // the agent message in code that runs *after* the final yield.
            // Breaking early disposes the iterator and skips that block.
            await foreach (var chunk in _chat.SendMessageAsync(_activeConversationId, body, ct).ConfigureAwait(true))
            {
                if (agentBubble is null)
                {
                    agentBubble = new MessageViewModel(chunk.MessageId, MessageAuthor.Agent, chunk.DeltaText);
                    Messages.Add(agentBubble);
                }
                else if (!string.IsNullOrEmpty(chunk.DeltaText))
                {
                    agentBubble.Append(chunk.DeltaText);
                }
            }
        }
        finally
        {
            IsStreaming = false;
            await ReloadAsync(ct).ConfigureAwait(true);
        }
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        if (!HasConversation)
        {
            return;
        }

        var convo = await _chat.GetConversationAsync(_activeConversationId, ct).ConfigureAwait(true);
        Messages.Clear();
        foreach (var m in convo.Messages)
        {
            Messages.Add(MessageViewModel.From(m));
        }
        ConversationTitle = convo.Title;
    }

    partial void OnDraftChanged(string value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnIsStreamingChanged(bool value) => SendCommand.NotifyCanExecuteChanged();

    partial void OnHasConversationChanged(bool value) => SendCommand.NotifyCanExecuteChanged();
}
