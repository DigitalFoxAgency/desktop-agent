using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Bridge.PhaseCompletion;

/// <summary>
/// Watches the working directory's SESSION-LOG.md (and any nested SESSION-LOG.md a
/// launchpad skill chooses to write) for the launchpad's verification marker —
/// <c>&lt;skill&gt;: verified</c> — and emits a single <see cref="PhaseCompletionSignal"/>
/// the first time it is seen.
/// </summary>
public sealed class CompletionDetector : IAsyncDisposable
{
    private const string SessionLogName = "SESSION-LOG.md";

    private readonly string _workingDir;
    private readonly string _skill;
    // The launchpad pattern: skills auto-write `[<skill>: completed]` on
    // success; a human reviewer later promotes that to `[<skill>: verified]`.
    // For automated phases the platform should fire on either marker.
    private readonly string[] _completionMarkers;
    private readonly ILogger<CompletionDetector> _log;
    private readonly Channel<PhaseCompletionSignal> _channel = Channel.CreateBounded<PhaseCompletionSignal>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
    });
    private readonly FileSystemWatcher _watcher;
    private readonly CancellationTokenSource _cts = new();
    private readonly TimeSpan _debounce;
    private readonly object _lock = new();
    private DateTimeOffset _lastChange = DateTimeOffset.MinValue;
    private bool _signalled;
    private readonly Task _pump;

    public CompletionDetector(string workingDir, string skill, ILogger<CompletionDetector> log, TimeSpan? debounce = null)
    {
        _workingDir = workingDir;
        _skill = skill;
        _completionMarkers = new[] { $"{skill}: completed", $"{skill}: verified" };
        _log = log;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(500);

        _watcher = new FileSystemWatcher(workingDir)
        {
            IncludeSubdirectories = true,
            Filter = SessionLogName,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = false,
        };
        _watcher.Changed += (_, _) => Touch();
        _watcher.Created += (_, _) => Touch();
        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public ChannelReader<PhaseCompletionSignal> Reader => _channel.Reader;

    public void Start()
    {
        _watcher.EnableRaisingEvents = true;
        // One immediate scan in case the marker was written before we started.
        Touch();
    }

    private void Touch()
    {
        lock (_lock) { _lastChange = DateTimeOffset.UtcNow; }
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_debounce, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { return; }

            DateTimeOffset last;
            lock (_lock) { last = _lastChange; }
            if (last == DateTimeOffset.MinValue) { continue; }
            if (DateTimeOffset.UtcNow - last < _debounce) { continue; }

            if (await ScanAsync(cancellationToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>Scans every <c>SESSION-LOG.md</c> under the working dir for the verification marker. Returns true if it found the marker (and emitted the signal).</summary>
    private async Task<bool> ScanAsync(CancellationToken cancellationToken)
    {
        if (_signalled) { return true; }
        IEnumerable<string> logs;
        try
        {
            logs = Directory.EnumerateFiles(_workingDir, SessionLogName, SearchOption.AllDirectories);
        }
        catch (DirectoryNotFoundException) { return false; }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "SESSION-LOG.md scan failed");
            return false;
        }

        foreach (var path in logs)
        {
            string text;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                text = await sr.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var marker in _completionMarkers)
            {
                if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    _signalled = true;
                    _log.LogInformation("CompletionDetector matched '{Marker}' in {Path}", marker, path);
                    _channel.Writer.TryWrite(new PhaseCompletionSignal(_skill, true, DateTimeOffset.UtcNow));
                    _channel.Writer.TryComplete();
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Records that the underlying claude session exited; if no verification marker has been seen we emit an unverified completion so the API can decide what to do (typically: mark phase failed).</summary>
    public void SignalSessionExited()
    {
        if (_signalled) { return; }
        _signalled = true;
        _channel.Writer.TryWrite(new PhaseCompletionSignal(_skill, false, DateTimeOffset.UtcNow));
        _channel.Writer.TryComplete();
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

public sealed record PhaseCompletionSignal(string Skill, bool Verified, DateTimeOffset OccurredAt);
