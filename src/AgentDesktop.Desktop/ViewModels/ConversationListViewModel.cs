using System.Collections.ObjectModel;
using AgentDesktop.Application.Chat;
using AgentDesktop.Domain;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentDesktop.Desktop.ViewModels;

/// <summary>
/// Sidebar list of past conversations. Loads from
/// <see cref="IChatService.ListConversationsAsync"/> and raises
/// <see cref="ConversationOpened"/> when the user selects a row.
/// </summary>
public sealed partial class ConversationListViewModel : ViewModelBase
{
    private readonly IChatService _chat;
    private bool _suppressSelectionEvent;

    [ObservableProperty]
    private ConversationListItemViewModel? _selected;

    public ConversationListViewModel(IChatService chat)
    {
        ArgumentNullException.ThrowIfNull(chat);
        _chat = chat;
        Items = new ObservableCollection<ConversationListItemViewModel>();
    }

    public ObservableCollection<ConversationListItemViewModel> Items { get; }

    public ConversationId? SelectedConversationId => Selected?.Id;

    public event EventHandler<ConversationId>? ConversationOpened;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var previous = Selected?.Id;
        _suppressSelectionEvent = true;
        try
        {
            Items.Clear();
            await foreach (var c in _chat.ListConversationsAsync(ct).ConfigureAwait(true))
            {
                Items.Add(ConversationListItemViewModel.From(c));
            }
            if (previous is { } id)
            {
                foreach (var item in Items)
                {
                    if (item.Id == id)
                    {
                        Selected = item;
                        break;
                    }
                }
            }
            else if (Items.Count > 0)
            {
                Selected = Items[0];
            }
        }
        finally
        {
            _suppressSelectionEvent = false;
        }
    }

    [RelayCommand]
    private async Task NewConversationAsync(CancellationToken ct)
    {
        var convo = await _chat.StartConversationAsync(ct).ConfigureAwait(true);
        await LoadAsync(ct).ConfigureAwait(true);

        // Select the new row in the sidebar so the user sees the switch.
        // OnSelectedChanged will raise ConversationOpened; we suppress the
        // duplicate event we'd otherwise raise below.
        _suppressSelectionEvent = true;
        try
        {
            foreach (var item in Items)
            {
                if (item.Id == convo.Id)
                {
                    Selected = item;
                    break;
                }
            }
        }
        finally
        {
            _suppressSelectionEvent = false;
        }

        ConversationOpened?.Invoke(this, convo.Id);
    }

    partial void OnSelectedChanged(ConversationListItemViewModel? value)
    {
        if (_suppressSelectionEvent || value is null)
        {
            return;
        }
        ConversationOpened?.Invoke(this, value.Id);
    }
}
