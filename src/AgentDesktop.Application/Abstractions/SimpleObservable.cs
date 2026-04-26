namespace AgentDesktop.Application.Abstractions;

/// <summary>
/// Tiny <see cref="IObservable{T}"/> implementation that lets a
/// producer push values to attached observers. Used where we want
/// the IObservable contract on a public surface without pulling in
/// the full System.Reactive package.
/// </summary>
public sealed class SimpleObservable<T> : IObservable<T>
{
    private readonly object _gate = new();
    private readonly List<IObserver<T>> _observers = new();

    public IDisposable Subscribe(IObserver<T> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        lock (_gate)
        {
            _observers.Add(observer);
        }

        return new Subscription(this, observer);
    }

    public void OnNext(T value)
    {
        IObserver<T>[] snapshot;
        lock (_gate)
        {
            snapshot = _observers.ToArray();
        }

        foreach (var observer in snapshot)
        {
            observer.OnNext(value);
        }
    }

    public void OnCompleted()
    {
        IObserver<T>[] snapshot;
        lock (_gate)
        {
            snapshot = _observers.ToArray();
            _observers.Clear();
        }

        foreach (var observer in snapshot)
        {
            observer.OnCompleted();
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly SimpleObservable<T> _owner;
        private readonly IObserver<T> _observer;
        private bool _disposed;

        public Subscription(SimpleObservable<T> owner, IObserver<T> observer)
        {
            _owner = owner;
            _observer = observer;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_owner._gate)
            {
                _owner._observers.Remove(_observer);
            }

            _disposed = true;
        }
    }
}
