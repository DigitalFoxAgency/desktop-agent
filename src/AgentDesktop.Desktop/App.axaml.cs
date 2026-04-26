using AgentDesktop.Desktop.Adapters;
using AgentDesktop.Desktop.Composition;
using AgentDesktop.Desktop.ViewModels;
using AgentDesktop.Desktop.Views;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace AgentDesktop.Desktop;

public partial class App : Avalonia.Application
{
    /// <summary>
    /// Composition-root service provider. Set by <see cref="Program.Main"/>
    /// before Avalonia starts, or by tests that build their own container.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = Services
                ?? throw new InvalidOperationException(
                    "App.Services has not been initialised. Build the DI container in Program.Main before starting Avalonia.");

            var shell = services.GetRequiredService<ShellViewModel>();
            var window = new MainWindow { DataContext = shell };

            services.GetRequiredService<AvaloniaConfirmationPrompt>().AttachOwner(window);

            desktop.MainWindow = window;

            var bootstrapper = services.GetRequiredService<RuntimeBootstrapper>();
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await bootstrapper.StartAsync(CancellationToken.None);
                shell.OnRuntimeReady();
            });
        }

        base.OnFrameworkInitializationCompleted();
    }
}
