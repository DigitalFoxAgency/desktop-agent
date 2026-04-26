using Avalonia;

namespace AgentDesktop.Desktop;

internal static class Program
{
    /// <summary>
    /// Entry point. Avalonia composition root. Keep this minimal — heavy
    /// wiring lives in <see cref="App.OnFrameworkInitializationCompleted"/>.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    /// <summary>Used by the Avalonia visual designer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
