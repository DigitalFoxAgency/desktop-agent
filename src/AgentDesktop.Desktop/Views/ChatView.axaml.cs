using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgentDesktop.Desktop.Views;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
