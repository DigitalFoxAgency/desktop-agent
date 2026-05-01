using System.Collections.Concurrent;
using AgentPlatform.Application.RunContainers;

namespace AgentPlatform.Contracts.Tests.Fakes;

/// <summary>
/// In-process fake driver. Tracks Start/Stop calls and lets tests pre-program
/// behavior (failures, container ids, observed env injection).
/// </summary>
public sealed class FakeRunContainerDriver : IRunContainerDriver
{
    private int _counter;
    private readonly ConcurrentDictionary<string, RunContainerSpec> _running = new();
    private readonly List<RunContainerSpec> _started = [];
    private readonly List<string> _stopped = [];
    private readonly HashSet<string> _ensuredVolumes = new(StringComparer.Ordinal);

    public IReadOnlyList<RunContainerSpec> Started
    {
        get { lock (_started) { return _started.ToArray(); } }
    }

    public IReadOnlyList<string> Stopped
    {
        get { lock (_stopped) { return _stopped.ToArray(); } }
    }

    public IReadOnlyDictionary<string, RunContainerSpec> Running => _running;

    public Func<RunContainerSpec, Exception?>? FailNextStartWith { get; set; }

    public Task<RunContainerHandle> StartAsync(RunContainerSpec spec, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (FailNextStartWith?.Invoke(spec) is { } ex)
        {
            FailNextStartWith = null;
            throw ex;
        }

        var id = $"fake-container-{Interlocked.Increment(ref _counter)}";
        _running[id] = spec;
        lock (_started) { _started.Add(spec); }
        return Task.FromResult(new RunContainerHandle(id, DateTimeOffset.UtcNow));
    }

    public Task StopAsync(string containerId, CancellationToken cancellationToken)
    {
        _running.TryRemove(containerId, out _);
        lock (_stopped) { _stopped.Add(containerId); }
        return Task.CompletedTask;
    }

    public Task EnsureVolumeAsync(string runId, CancellationToken cancellationToken)
    {
        lock (_ensuredVolumes) { _ensuredVolumes.Add(runId); }
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> Archived
    {
        get { lock (_archived) { return _archived.ToArray(); } }
    }
    private readonly List<string> _archived = [];

    public Task ArchiveVolumeAsync(string runId, CancellationToken cancellationToken)
    {
        lock (_archived) { _archived.Add(runId); }
        return Task.CompletedTask;
    }
}
