namespace AgentDesktop.Desktop.ViewModels;

/// <summary>
/// Retained as a thin alias of <see cref="ShellViewModel"/> so existing
/// designer markup keeps resolving. The real shell lives in
/// <see cref="ShellViewModel"/>.
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    public string Greeting { get; } = "Welcome to AgentDesktop.";
}
