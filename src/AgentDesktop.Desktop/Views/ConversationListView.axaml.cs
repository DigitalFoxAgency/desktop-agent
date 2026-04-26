using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AgentDesktop.Desktop.Views;

public partial class ConversationListView : UserControl
{
    public ConversationListView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
