using System.Runtime.CompilerServices;
using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Chat;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;

namespace AgentDesktop.Contracts.Tests.Fakes;

/// <summary>
/// Programmable runtime manager used by every test in the suite.
/// Honours the documented state machine; lets tests pre-program
/// per-(moduleId, operationId) responders for
/// <see cref="DelegateAsync"/>, per-(moduleId, skillId) responders
/// for <see cref="InvokeSkillAsync"/>, and a chat streamer for
/// <see cref="StreamChatAsync"/>.
/// </summary>
public sealed class FakeRuntimeManager : IRuntimeManager
{
    private readonly object _lock = new();
    private readonly SimpleObservable<RuntimeStatus> _statusSubject = new();
    private readonly Dictionary<(ModuleId, string), Func<DelegationContext, Task<DelegationResult>>> _operationResponders = new();
    private readonly Dictionary<(ModuleId, SkillId), Func<IReadOnlyDictionary<string, object?>, Task<SkillInvocationResult>>> _skillResponders = new();
    private Func<IReadOnlyList<Message>, string, IAsyncEnumerable<MessageChunk>> _chatResponder;
    private RuntimeStatus _status = RuntimeStatus.NotInstalled;
    private bool _disposed;

    public FakeRuntimeManager()
    {
        _chatResponder = DefaultChatResponder;
    }

    public RuntimeStatus Status
    {
        get { lock (_lock) { return _status; } }
    }

    public IObservable<RuntimeStatus> StatusChanged => _statusSubject;

    public List<(ModuleId, string OperationId, IReadOnlyDictionary<string, object?> Inputs)> DelegationLog { get; } = new();

    public List<(ModuleId, SkillId, IReadOnlyDictionary<string, object?>)> InvocationLog { get; } = new();

    public List<string> ChatLog { get; } = new();

    public void ProgramOperation(
        ModuleId moduleId,
        string operationId,
        Func<DelegationContext, Task<DelegationResult>> responder)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(responder);
        _operationResponders[(moduleId, operationId)] = responder;
    }

    public void ProgramSkill(
        ModuleId moduleId,
        SkillId skillId,
        Func<IReadOnlyDictionary<string, object?>, Task<SkillInvocationResult>> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _skillResponders[(moduleId, skillId)] = responder;
    }

    public void ProgramChat(Func<IReadOnlyList<Message>, string, IAsyncEnumerable<MessageChunk>> responder)
    {
        ArgumentNullException.ThrowIfNull(responder);
        _chatResponder = responder;
    }

    public void TransitionTo(RuntimeStatus status)
    {
        lock (_lock)
        {
            EnsureLegalTransition(_status, status);
            _status = status;
        }

        _statusSubject.OnNext(status);
    }

    public Task EnsureInstalledAsync(CancellationToken ct)
    {
        TransitionTo(RuntimeStatus.Installing);
        TransitionTo(RuntimeStatus.NotInstalled);
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken ct)
    {
        TransitionTo(RuntimeStatus.Starting);
        TransitionTo(RuntimeStatus.Ready);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        TransitionTo(RuntimeStatus.Stopped);
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
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(callbacks);

        if (Status != RuntimeStatus.Ready)
        {
            throw new InvalidOperationException(
                $"Cannot delegate while runtime status is {Status}; must be Ready.");
        }

        DelegationLog.Add((moduleId, operationId, inputs));

        if (!_operationResponders.TryGetValue((moduleId, operationId), out var responder))
        {
            return Task.FromResult(new DelegationResult(
                Succeeded: false,
                Outputs: new Dictionary<string, object?>(),
                Error: $"No programmed delegation responder for ({moduleId}, {operationId})"));
        }

        return responder(new DelegationContext(moduleId, operationId, inputs, conversationId, callbacks, ct));
    }

    public Task<SkillInvocationResult> InvokeSkillAsync(
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (Status != RuntimeStatus.Ready)
        {
            throw new InvalidOperationException(
                $"Cannot invoke skill while runtime status is {Status}; must be Ready.");
        }

        InvocationLog.Add((moduleId, skillId, inputs));

        if (_skillResponders.TryGetValue((moduleId, skillId), out var responder))
        {
            return responder(inputs);
        }

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
        ChatLog.Add($"chat[{conversationId}]: '{userMessage}' (history={history.Count})");

        if (Status != RuntimeStatus.Ready)
        {
            throw new InvalidOperationException(
                $"Cannot run chat turn while runtime status is {Status}; must be Ready.");
        }

        return _chatResponder(history, userMessage);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _statusSubject.OnCompleted();
        return ValueTask.CompletedTask;
    }

    private static async IAsyncEnumerable<MessageChunk> DefaultChatResponder(
        IReadOnlyList<Message> history,
        string userMessage)
    {
        await Task.Yield();
        var id = MessageId.New();
        yield return new MessageChunk(id, "fake reply: ", IsFinal: false);
        yield return new MessageChunk(id, userMessage, IsFinal: false);
        yield return new MessageChunk(id, string.Empty, IsFinal: true);
    }

    private static void EnsureLegalTransition(RuntimeStatus from, RuntimeStatus to)
    {
        var legal = (from, to) switch
        {
            (RuntimeStatus.NotInstalled, RuntimeStatus.Installing) => true,
            (RuntimeStatus.Installing, RuntimeStatus.NotInstalled) => true,
            (RuntimeStatus.Installing, RuntimeStatus.Starting) => true,
            (RuntimeStatus.NotInstalled, RuntimeStatus.Starting) => true,
            (RuntimeStatus.Starting, RuntimeStatus.Ready) => true,
            (RuntimeStatus.Starting, RuntimeStatus.Degraded) => true,
            (RuntimeStatus.Ready, RuntimeStatus.Degraded) => true,
            (RuntimeStatus.Degraded, RuntimeStatus.Ready) => true,
            (RuntimeStatus.Ready, RuntimeStatus.Stopped) => true,
            (RuntimeStatus.Degraded, RuntimeStatus.Stopped) => true,
            (RuntimeStatus.Stopped, RuntimeStatus.Stopped) => true,
            (var f, var t) when f == t => true,
            _ => false,
        };

        if (!legal)
        {
            throw new InvalidOperationException(
                $"Illegal runtime transition: {from} → {to}.");
        }
    }
}

/// <summary>Context handed to a programmed delegation responder.</summary>
public sealed record DelegationContext(
    ModuleId ModuleId,
    string OperationId,
    IReadOnlyDictionary<string, object?> Inputs,
    ConversationId ConversationId,
    IDelegationCallbacks Callbacks,
    CancellationToken Cancellation);
