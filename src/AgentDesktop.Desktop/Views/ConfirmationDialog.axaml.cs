using AgentDesktop.Domain.Policies;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AgentDesktop.Desktop.Views;

/// <summary>
/// Single shared dialog used for every dangerous-action confirmation.
/// One component → identical wording and affordances regardless of the
/// origin (constitution Principle III).
/// </summary>
public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Populate the dialog labels for the supplied action.</summary>
    public void Bind(DangerousAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var heading = this.FindControl<TextBlock>("HeadingText");
        var kind = this.FindControl<TextBlock>("KindText");
        var target = this.FindControl<SelectableTextBlock>("TargetText");
        if (heading is not null) { heading.Text = $"Confirm {Humanise(action.Kind.ToString())}?"; }
        if (kind is not null) { kind.Text = action.Kind.ToString(); }
        if (target is not null) { target.Text = action.Target; }
    }

    /// <summary>Latest decision; <c>null</c> until the user clicks Confirm or Decline.</summary>
    public bool? Result { get; private set; }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        var confirm = this.FindControl<Button>("ConfirmButton");
        confirm?.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        Result = true;
        Close(true);
    }

    private void OnDecline(object? sender, RoutedEventArgs e)
    {
        Result = false;
        Close(false);
    }

    private static string Humanise(string kind) => kind switch
    {
        "DeleteFile" => "file deletion",
        "GitPush" => "git push",
        "InstallPackage" => "package install",
        "RunShell" => "shell command",
        "ModuleDeclared" => "module-declared action",
        _ => kind,
    };
}
