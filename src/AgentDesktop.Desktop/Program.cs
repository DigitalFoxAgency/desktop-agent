using AgentDesktop.Desktop.Composition;
using Avalonia;

namespace AgentDesktop.Desktop;

/// <summary>
/// Entry point + composition root for the desktop shell.
/// Parses <c>--fake-runtime</c>, builds the DI container, and hands off
/// to Avalonia. This file is the only place inside <c>AgentDesktop.Desktop</c>
/// that knows about <c>AgentDesktop.Infrastructure</c>; every other class
/// in this project references <c>Application</c> only.
/// </summary>
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = AgentDesktopOptions.Parse(args);
        var services = ServiceCollectionExtensions.BuildServiceProvider(options);
        App.Services = services;
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // ServiceProvider holds at least one IAsyncDisposable-only singleton
            // (EchoRuntimeManager → IRuntimeManager). Sync Dispose() throws on
            // those, so we drain through DisposeAsync().
            DisposeProviderAsync(services).AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>Used by the Avalonia visual designer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static async ValueTask DisposeProviderAsync(IServiceProvider services)
    {
        if (services is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync().ConfigureAwait(false);
        }
        else if (services is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

/// <summary>Parsed CLI flags that affect composition.</summary>
internal sealed record AgentDesktopOptions(bool UseFakeRuntime)
{
    public static AgentDesktopOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var fake = args.Any(a => string.Equals(a, "--fake-runtime", StringComparison.Ordinal));
        return new AgentDesktopOptions(UseFakeRuntime: fake);
    }
}
