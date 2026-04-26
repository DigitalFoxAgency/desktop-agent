using System.Runtime.CompilerServices;
using AgentDesktop.Application.Chat;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Desktop.Composition;

/// <summary>
/// Demo-only <see cref="IRuntimeManager"/> wired when <c>--fake-runtime</c>
/// is passed. Mirrors the default behaviour of the test
/// <c>FakeRuntimeManager</c>: a static state machine and an echo chat
/// responder. The headless test suite uses the real
/// <c>FakeRuntimeManager</c> via the contract-test project; this class
/// only exists so <c>dotnet run --project src/AgentDesktop.Desktop --
/// --fake-runtime</c> renders something useful.
/// </summary>
internal sealed class EchoRuntimeManager : IRuntimeManager
{
    private readonly object _lock = new();
    private readonly SimpleStatusObservable _statusObservable = new();
    private RuntimeStatus _status = RuntimeStatus.NotInstalled;
    private bool _disposed;

    public RuntimeStatus Status
    {
        get { lock (_lock) { return _status; } }
    }

    public IObservable<RuntimeStatus> StatusChanged => _statusObservable;

    public Task EnsureInstalledAsync(CancellationToken ct)
    {
        Transition(RuntimeStatus.Installing);
        Transition(RuntimeStatus.NotInstalled);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (Status == RuntimeStatus.Ready)
        {
            return Task.CompletedTask;
        }
        Transition(RuntimeStatus.Starting);
        Transition(RuntimeStatus.Ready);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        Transition(RuntimeStatus.Stopped);
        return Task.CompletedTask;
    }

    public Task<DelegationResult> DelegateAsync(
        ModuleId moduleId,
        string operationId,
        IReadOnlyDictionary<string, object?> inputs,
        ConversationId conversationId,
        IDelegationCallbacks callbacks,
        CancellationToken ct)
    {
        EnsureReady();
        return Task.FromResult(new DelegationResult(
            Succeeded: false,
            Outputs: new Dictionary<string, object?>(),
            Error: "EchoRuntimeManager does not execute real delegations; use the live runtime."));
    }

    public Task<SkillInvocationResult> InvokeSkillAsync(
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct)
    {
        EnsureReady();
        return Task.FromResult(new SkillInvocationResult(
            Succeeded: true,
            Outputs: new Dictionary<string, object?>(),
            Error: null));
    }

    public IAsyncEnumerable<MessageChunk> RunChatTurnAsync(
        ConversationId conversationId,
        IReadOnlyList<Message> history,
        string userMessage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(userMessage);
        EnsureReady();
        return EchoAsync(userMessage, ct);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }
        _disposed = true;
        _statusObservable.OnCompleted();
        return ValueTask.CompletedTask;
    }

    private static async IAsyncEnumerable<MessageChunk> EchoAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var id = MessageId.New();
        await Task.Yield();
        yield return new MessageChunk(id, "echo: ", IsFinal: false);
        await Task.Delay(20, ct).ConfigureAwait(false);
        yield return new MessageChunk(id, userMessage, IsFinal: false);
        yield return new MessageChunk(id, string.Empty, IsFinal: true);
    }

    private void EnsureReady()
    {
        if (Status != RuntimeStatus.Ready)
        {
            throw new InvalidOperationException(
                $"EchoRuntimeManager not Ready (Status={Status}). Call StartAsync first.");
        }
    }

    private void Transition(RuntimeStatus next)
    {
        lock (_lock)
        {
            _status = next;
        }
        _statusObservable.OnNext(next);
    }

    private sealed class SimpleStatusObservable : IObservable<RuntimeStatus>
    {
        private readonly List<IObserver<RuntimeStatus>> _observers = new();
        private readonly object _gate = new();

        public IDisposable Subscribe(IObserver<RuntimeStatus> observer)
        {
            ArgumentNullException.ThrowIfNull(observer);
            lock (_gate) { _observers.Add(observer); }
            return new Subscription(this, observer);
        }

        public void OnNext(RuntimeStatus value)
        {
            IObserver<RuntimeStatus>[] snapshot;
            lock (_gate) { snapshot = _observers.ToArray(); }
            foreach (var o in snapshot) { o.OnNext(value); }
        }

        public void OnCompleted()
        {
            IObserver<RuntimeStatus>[] snapshot;
            lock (_gate) { snapshot = _observers.ToArray(); _observers.Clear(); }
            foreach (var o in snapshot) { o.OnCompleted(); }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly SimpleStatusObservable _owner;
            private readonly IObserver<RuntimeStatus> _observer;
            public Subscription(SimpleStatusObservable owner, IObserver<RuntimeStatus> observer)
            {
                _owner = owner;
                _observer = observer;
            }
            public void Dispose()
            {
                lock (_owner._gate) { _owner._observers.Remove(_observer); }
            }
        }
    }
}
