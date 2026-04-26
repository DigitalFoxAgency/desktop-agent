using AgentDesktop.Application.Subscription;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AgentDesktop.Desktop.ViewModels;

/// <summary>
/// Captures the subscription session token and validates it through
/// <see cref="ISubscriptionGate.SignInAsync"/>. Raises
/// <see cref="SignedIn"/> on success so the shell can swap views.
/// </summary>
public sealed partial class SignInViewModel : ViewModelBase
{
    private readonly ISubscriptionGate _subscription;

    [ObservableProperty]
    private string _sessionToken = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public SignInViewModel(ISubscriptionGate subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscription = subscription;
    }

    public event EventHandler? SignedIn;

    public bool CanSubmit => !IsBusy && !string.IsNullOrWhiteSpace(SessionToken);

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await _subscription.SignInAsync(SessionToken.Trim(), ct).ConfigureAwait(true);
            if (!_subscription.AgentCapabilitiesEnabled)
            {
                ErrorMessage = $"Subscription is {_subscription.CurrentStatus}; agent capabilities disabled.";
                return;
            }
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
#pragma warning disable CA1031 // Surface every failure to the user; never crash sign-in.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSessionTokenChanged(string value) => SubmitCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => SubmitCommand.NotifyCanExecuteChanged();
}
