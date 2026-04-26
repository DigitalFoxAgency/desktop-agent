using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(AgentDesktop.Desktop.Tests.HeadlessTestAppBuilder))]

namespace AgentDesktop.Desktop.Tests;

/// <summary>
/// Avalonia.Headless.XUnit entry point. Hosts the real
/// <see cref="App"/> against a headless platform so views can be
/// instantiated, measured, focused, and interacted with from xUnit
/// tests via the <c>[AvaloniaFact]</c> attribute.
/// </summary>
public static class HeadlessTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true,
            });
}
