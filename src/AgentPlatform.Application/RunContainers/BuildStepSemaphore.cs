namespace AgentPlatform.Application.RunContainers;

public sealed class BuildStepSemaphore : IDisposable
{
    public const int DefaultCapacity = 2;

    private readonly SemaphoreSlim _semaphore;

    public BuildStepSemaphore(int capacity = DefaultCapacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be >= 1");
        }

        Capacity = capacity;
        _semaphore = new SemaphoreSlim(capacity, capacity);
    }

    public int Capacity { get; }

    public int CurrentCount => _semaphore.CurrentCount;

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser : IDisposable
    {
        private SemaphoreSlim? _semaphore;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            var s = Interlocked.Exchange(ref _semaphore, null);
            s?.Release();
        }
    }
}
