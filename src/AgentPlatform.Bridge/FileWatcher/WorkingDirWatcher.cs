using System.Threading.Channels;
using AgentPlatform.Application.Bridge;

namespace AgentPlatform.Bridge.FileWatcher;

/// <summary>
/// Debounced file-system watcher that emits <see cref="BridgeEvent.FileChanged"/> events.
/// Phase 4 (T103) extends with glob filters / publisher wiring.
/// </summary>
public sealed class WorkingDirWatcher : IAsyncDisposable
{
    private static readonly string[] DefaultIgnoredFolders = { "node_modules", ".git", "dist", "build" };
    private readonly FileSystemWatcher _watcher;
    private readonly TimeSpan _debounce;
    private readonly Channel<BridgeEvent.FileChanged> _channel = Channel.CreateUnbounded<BridgeEvent.FileChanged>();
    private readonly Dictionary<string, DateTimeOffset> _pending = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pump;

    public WorkingDirWatcher(string workingDir, TimeSpan? debounce = null)
    {
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
        _watcher = new FileSystemWatcher(workingDir)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            EnableRaisingEvents = false,
        };
        _watcher.Created += (_, e) => Enqueue(workingDir, e.FullPath, FileChangeKind.Created);
        _watcher.Changed += (_, e) => Enqueue(workingDir, e.FullPath, FileChangeKind.Modified);
        _watcher.Deleted += (_, e) => Enqueue(workingDir, e.FullPath, FileChangeKind.Deleted);
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public ChannelReader<BridgeEvent.FileChanged> Reader => _channel.Reader;

    public void Start() => _watcher.EnableRaisingEvents = true;

    private void Enqueue(string root, string fullPath, FileChangeKind kind)
    {
        var rel = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (DefaultIgnoredFolders.Any(f => rel.StartsWith(f + "/", StringComparison.Ordinal) || rel == f))
        {
            return;
        }
        lock (_lock)
        {
            _pending[rel] = DateTimeOffset.UtcNow;
        }
        _ = kind; // currently we only emit Modified after debounce; full kind tracking lands in T103
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(_debounce, cancellationToken).ConfigureAwait(false);
            List<string> due;
            lock (_lock)
            {
                var cutoff = DateTimeOffset.UtcNow - _debounce;
                due = _pending.Where(kv => kv.Value <= cutoff).Select(kv => kv.Key).ToList();
                foreach (var k in due)
                {
                    _pending.Remove(k);
                }
            }
            foreach (var rel in due)
            {
                await _channel.Writer.WriteAsync(
                    new BridgeEvent.FileChanged(rel, FileChangeKind.Modified, DateTimeOffset.UtcNow),
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        await _cts.CancelAsync().ConfigureAwait(false);
        try { await _pump.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _watcher.Dispose();
        _cts.Dispose();
        _channel.Writer.TryComplete();
    }
}
