using System.Threading.Channels;
using AgentPlatform.Application.Bridge;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Bridge.FileWatcher;

/// <summary>
/// Sits on top of <see cref="WorkingDirWatcher"/> and forwards FileChanged events into
/// the bridge transport, with extra ignore globs and debounced batching.
/// </summary>
public sealed class TreeEventPublisher : IAsyncDisposable
{
    private static readonly string[] IgnoredDirs = { "node_modules", ".git", "dist", "build", ".next", ".turbo" };
    private readonly WorkingDirWatcher _watcher;
    private readonly Channel<BridgeEvent.FileChanged> _outbound = Channel.CreateUnbounded<BridgeEvent.FileChanged>();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;
    private readonly ILogger<TreeEventPublisher> _log;

    public TreeEventPublisher(string workingDir, ILogger<TreeEventPublisher> log, ILoggerFactory loggerFactory)
    {
        _log = log;
        _watcher = new WorkingDirWatcher(workingDir);
        _watcher.Start();
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public ChannelReader<BridgeEvent.FileChanged> Reader => _outbound.Reader;

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in _watcher.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (ShouldIgnore(evt.RelativePath))
                {
                    continue;
                }
                await _outbound.Writer.WriteAsync(evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Tree publisher pump failed");
        }
        finally
        {
            _outbound.Writer.TryComplete();
        }
    }

    private static bool ShouldIgnore(string relativePath)
    {
        foreach (var d in IgnoredDirs)
        {
            if (relativePath.StartsWith(d + "/", StringComparison.Ordinal) || relativePath == d)
            {
                return true;
            }
        }
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await _pump.ConfigureAwait(false); } catch (OperationCanceledException) { }
        await _watcher.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
