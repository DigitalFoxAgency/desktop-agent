using AgentDesktop.Application.Abstractions;
using AgentDesktop.Desktop.Views;
using AgentDesktop.Domain.Policies;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AgentDesktop.Desktop.Adapters;

/// <summary>
/// Avalonia <see cref="IConfirmationPrompt"/> adapter. Shows the
/// shared <see cref="ConfirmationDialog"/> on the UI thread and
/// serialises concurrent requests via a single
/// <see cref="SemaphoreSlim"/> so two dialogs never overlap (FR-014).
/// </summary>
public sealed class AvaloniaConfirmationPrompt : IConfirmationPrompt, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Window? _owner;
    private Func<DangerousAction, CancellationToken, Task<bool>>? _testHook;
    private bool _disposed;

    /// <summary>Called once by <see cref="App.OnFrameworkInitializationCompleted"/>.</summary>
    public void AttachOwner(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
    }

    /// <summary>
    /// Test hook: when set, ConfirmAsync routes through this delegate
    /// instead of opening a real window. Used by headless tests that
    /// want to simulate user choices without the Avalonia dialog
    /// lifecycle.
    /// </summary>
    public void OverrideForTests(Func<DangerousAction, CancellationToken, Task<bool>>? hook)
    {
        _testHook = hook;
    }

    public async Task<bool> ConfirmAsync(DangerousAction action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_testHook is not null)
            {
                return await _testHook(action, ct).ConfigureAwait(false);
            }

            if (_owner is null)
            {
                throw new InvalidOperationException(
                    "AvaloniaConfirmationPrompt has no owner window. Call AttachOwner before showing prompts.");
            }

            return await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dialog = new ConfirmationDialog();
                dialog.Bind(action);
                var result = await dialog.ShowDialog<bool?>(_owner!).ConfigureAwait(true);
                return result ?? false;
            }).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _gate.Dispose();
        _disposed = true;
    }
}
