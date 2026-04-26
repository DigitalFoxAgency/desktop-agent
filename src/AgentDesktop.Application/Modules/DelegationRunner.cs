using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Chat;
using AgentDesktop.Application.Policies;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using AgentDesktop.Domain.Policies;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Application.Modules;

/// <summary>
/// Services both the top-level agent's <c>delegate_operation</c>
/// tool call AND the catalogue-pick UI shortcut. Validates the
/// operation, kicks off
/// <see cref="IRuntimeManager.DelegateAsync"/> with a
/// platform-supplied callbacks adapter that:
///   - persists progress events as agent messages with
///     <see cref="DelegationRef"/> set,
///   - routes confirmation requests through
///     <see cref="IPolicyEngine"/>,
///   - routes human-handoff requests through
///     <see cref="IHumanHandoffPrompt"/>,
///   - and routes <c>AskUserAsync</c> requests through
///     <see cref="IConversationQuestionState"/> so the next user
///     chat message becomes the answer (research.md R18).
/// </summary>
public sealed class DelegationRunner
{
    private readonly IModuleRegistry _modules;
    private readonly IRuntimeManager _runtime;
    private readonly IPolicyEngine _policy;
    private readonly IHumanHandoffPrompt _handoff;
    private readonly IConversationQuestionState _questionState;
    private readonly IChatRepository _chat;
    private readonly IClock _clock;
    private readonly ILogger<DelegationRunner> _logger;

    public DelegationRunner(
        IModuleRegistry modules,
        IRuntimeManager runtime,
        IPolicyEngine policy,
        IHumanHandoffPrompt handoff,
        IConversationQuestionState questionState,
        IChatRepository chat,
        IClock clock,
        ILogger<DelegationRunner> logger)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(questionState);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _modules = modules;
        _runtime = runtime;
        _policy = policy;
        _handoff = handoff;
        _questionState = questionState;
        _chat = chat;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Run a delegation. Returns the module's final
    /// <see cref="DelegationResult"/>.
    /// </summary>
    public async Task<DelegationResult> RunAsync(
        ModuleId moduleId,
        string operationId,
        IReadOnlyDictionary<string, object?> inputs,
        ConversationId conversationId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(inputs);

        var operation = _modules.FindOperation(moduleId, operationId);
        if (operation is null)
        {
            var message = $"Operation '{operationId}' not found in module '{moduleId}'.";
            _logger.LogWarning("{Message}", message);
            return new DelegationResult(Succeeded: false, Outputs: new Dictionary<string, object?>(), Error: message);
        }

        var callbacks = new DelegationCallbacks(
            this,
            moduleId,
            operationId,
            conversationId);

        try
        {
            return await _runtime.DelegateAsync(
                moduleId,
                operationId,
                inputs,
                conversationId,
                callbacks,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new DelegationResult(Succeeded: false, Outputs: new Dictionary<string, object?>(), Error: "Cancelled by user.");
        }
    }

    private async Task PersistProgressMessageAsync(
        ConversationId conversationId,
        ModuleId moduleId,
        string operationId,
        string text,
        CancellationToken ct)
    {
        var convo = await _chat.GetAsync(conversationId, ct).ConfigureAwait(false);
        if (convo is null)
        {
            _logger.LogWarning(
                "Delegation tried to persist progress for unknown conversation {ConversationId}.",
                conversationId);
            return;
        }

        var message = new Message(
            id: MessageId.New(),
            conversationId: conversationId,
            index: convo.Messages.Count,
            author: MessageAuthor.Agent,
            body: text,
            createdAt: _clock.UtcNow,
            originatingDelegation: new DelegationRef(moduleId, operationId));

        await _chat.AppendMessageAsync(message, ct).ConfigureAwait(false);
    }

    private async Task PersistSystemMessageAsync(
        ConversationId conversationId,
        string text,
        CancellationToken ct)
    {
        var convo = await _chat.GetAsync(conversationId, ct).ConfigureAwait(false);
        if (convo is null)
        {
            return;
        }

        var message = new Message(
            id: MessageId.New(),
            conversationId: conversationId,
            index: convo.Messages.Count,
            author: MessageAuthor.System,
            body: text,
            createdAt: _clock.UtcNow);
        await _chat.AppendMessageAsync(message, ct).ConfigureAwait(false);
    }

    private sealed class DelegationCallbacks : IDelegationCallbacks
    {
        private readonly DelegationRunner _owner;
        private readonly ModuleId _moduleId;
        private readonly string _operationId;
        private readonly ConversationId _conversationId;

        public DelegationCallbacks(
            DelegationRunner owner,
            ModuleId moduleId,
            string operationId,
            ConversationId conversationId)
        {
            _owner = owner;
            _moduleId = moduleId;
            _operationId = operationId;
            _conversationId = conversationId;
        }

        public Task EmitProgressAsync(string text, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(text);
            return _owner.PersistProgressMessageAsync(_conversationId, _moduleId, _operationId, text, ct);
        }

        public async Task<bool> RequestConfirmationAsync(DangerousAction action, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(action);

            var decision = await _owner._policy.EvaluateAsync(action, ct).ConfigureAwait(false);
            var confirmed = decision.Outcome == PolicyOutcome.Confirmed;

            if (!confirmed)
            {
                await _owner.PersistSystemMessageAsync(
                    _conversationId,
                    $"Action '{action.Kind}' on '{action.Target}' was {decision.Outcome.ToString().ToLowerInvariant()}.",
                    ct).ConfigureAwait(false);
            }

            return confirmed;
        }

        public async Task RequestHumanHandoffAsync(string stepName, string instructions, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(stepName);
            ArgumentNullException.ThrowIfNull(instructions);

            await _owner.PersistProgressMessageAsync(
                _conversationId,
                _moduleId,
                _operationId,
                $"⏸ Waiting for human step: {stepName}\n\n{instructions}",
                ct).ConfigureAwait(false);

            await _owner._handoff.WaitForCompletionAsync(stepName, instructions, ct).ConfigureAwait(false);

            await _owner.PersistProgressMessageAsync(
                _conversationId,
                _moduleId,
                _operationId,
                $"▶ Human step '{stepName}' marked complete; resuming.",
                ct).ConfigureAwait(false);
        }

        public async Task<string> AskUserAsync(string question, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(question);

            await _owner.PersistProgressMessageAsync(
                _conversationId,
                _moduleId,
                _operationId,
                question,
                ct).ConfigureAwait(false);

            return await _owner._questionState.RegisterPendingAsync(_conversationId, question, ct).ConfigureAwait(false);
        }
    }
}
