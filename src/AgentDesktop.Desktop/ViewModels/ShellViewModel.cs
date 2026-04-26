using AgentDesktop.Application.Subscription;
using AgentDesktop.Domain;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AgentDesktop.Desktop.ViewModels;

/// <summary>
/// Top-level UI state. Owns the sign-in / signed-in switch, the
/// conversation list, and the active chat. Held as a singleton in DI
/// so the same instance backs MainWindow for the entire app lifetime.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSignedIn;

    [ObservableProperty]
    private bool _runtimeReady;

    [ObservableProperty]
    private string _statusBanner = "Starting agent runtime…";

    public ShellViewModel(
        ISubscriptionGate subscription,
        SignInViewModel signIn,
        ConversationListViewModel conversations,
        ChatViewModel chat)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(conversations);
        ArgumentNullException.ThrowIfNull(chat);

        SignIn = signIn;
        Conversations = conversations;
        Chat = chat;

        SignIn.SignedIn += HandleSignedIn;
        Conversations.ConversationOpened += HandleConversationOpened;
        IsSignedIn = subscription.AgentCapabilitiesEnabled;
    }

    public SignInViewModel SignIn { get; }

    public ConversationListViewModel Conversations { get; }

    public ChatViewModel Chat { get; }

    public void OnRuntimeReady()
    {
        RuntimeReady = true;
        StatusBanner = IsSignedIn ? "Ready." : "Sign in with your subscription token to start.";
    }

    private async void HandleSignedIn(object? sender, EventArgs e)
    {
        IsSignedIn = true;
        StatusBanner = RuntimeReady ? "Ready." : "Waiting for runtime…";
        await Conversations.LoadAsync().ConfigureAwait(true);
        if (Conversations.SelectedConversationId is { } id)
        {
            await Chat.OpenAsync(id).ConfigureAwait(true);
        }
        else
        {
            await Chat.StartNewAsync().ConfigureAwait(true);
            await Conversations.LoadAsync().ConfigureAwait(true);
        }
    }

    private async void HandleConversationOpened(object? sender, ConversationId id)
    {
        await Chat.OpenAsync(id).ConfigureAwait(true);
    }
}
