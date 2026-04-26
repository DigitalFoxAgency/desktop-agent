using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgentDesktop.Desktop.Views;

public partial class SignInView : UserControl
{
    public SignInView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
