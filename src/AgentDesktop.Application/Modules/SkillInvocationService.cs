using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Chat;
using AgentDesktop.Application.Policies;
using AgentDesktop.Application.Runtime;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using AgentDesktop.Domain.Modules;
using AgentDesktop.Domain.Policies;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Application.Modules;

/// <summary>
/// Power-user direct skill invocation (US4 advanced path). Validates
/// inputs against the skill's declared <see cref="SkillParameter"/>
/// list, evaluates the action through <see cref="IPolicyEngine"/>
/// before dispatching, and persists the skill output as an Agent
/// message with <see cref="SkillRef"/> set.
///
/// NOTE: this is NOT used to compose multi-step flows — that is the
/// module's responsibility. Operations use
/// <see cref="DelegationRunner"/>.
/// </summary>
public sealed class SkillInvocationService
{
    private readonly IModuleRegistry _modules;
    private readonly IRuntimeManager _runtime;
    private readonly IPolicyEngine _policy;
    private readonly IChatRepository _chat;
    private readonly IClock _clock;
    private readonly ILogger<SkillInvocationService> _logger;

    public SkillInvocationService(
        IModuleRegistry modules,
        IRuntimeManager runtime,
        IPolicyEngine policy,
        IChatRepository chat,
        IClock clock,
        ILogger<SkillInvocationService> logger)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _modules = modules;
        _runtime = runtime;
        _policy = policy;
        _chat = chat;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SkillInvocationResult> InvokeAsync(
        ModuleId moduleId,
        SkillId skillId,
        IReadOnlyDictionary<string, object?> inputs,
        ConversationId conversationId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var skill = _modules.FindSkill(moduleId, skillId);
        if (skill is null)
        {
            return new SkillInvocationResult(
                Succeeded: false,
                Outputs: new Dictionary<string, object?>(),
                Error: $"Skill '{skillId}' not found in module '{moduleId}'.");
        }

        // Required-input validation.
        var missing = skill.Inputs
            .Where(p => p.Required && !inputs.ContainsKey(p.Name))
            .Select(p => p.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return new SkillInvocationResult(
                Succeeded: false,
                Outputs: new Dictionary<string, object?>(),
                Error: $"Missing required inputs: {string.Join(", ", missing)}");
        }

        // Policy gate: if the skill is classified Dangerous, route through the engine.
        if (skill.Classification == ActionClassification.Dangerous)
        {
            var action = new DangerousAction(
                PolicyDecisionId.New(),
                DangerousActionKind.ModuleDeclared,
                target: $"{moduleId}/{skillId}",
                origin: new PolicyOrigin.FromSkill(moduleId, skillId),
                requestedAt: _clock.UtcNow);

            var decision = await _policy.EvaluateAsync(action, ct).ConfigureAwait(false);
            if (decision.Outcome != PolicyOutcome.Confirmed)
            {
                _logger.LogInformation(
                    "Skill '{Skill}' invocation declined: {Outcome}.",
                    skillId,
                    decision.Outcome);
                return new SkillInvocationResult(
                    Succeeded: false,
                    Outputs: new Dictionary<string, object?>(),
                    Error: $"Skill invocation {decision.Outcome.ToString().ToLowerInvariant()}.");
            }
        }

        var result = await _runtime.InvokeSkillAsync(moduleId, skillId, inputs, ct).ConfigureAwait(false);

        // Persist a chat message recording the invocation.
        var convo = await _chat.GetAsync(conversationId, ct).ConfigureAwait(false);
        if (convo is not null)
        {
            var body = result.Succeeded
                ? FormatOutputs(skillId, result.Outputs)
                : $"Skill '{skillId}' failed: {result.Error}";

            var message = new Message(
                id: MessageId.New(),
                conversationId: conversationId,
                index: convo.Messages.Count,
                author: result.Succeeded ? MessageAuthor.Agent : MessageAuthor.System,
                body: body,
                createdAt: _clock.UtcNow,
                originatingSkill: new SkillRef(moduleId, skillId));
            await _chat.AppendMessageAsync(message, ct).ConfigureAwait(false);
        }

        return result;
    }

    private static string FormatOutputs(SkillId skillId, IReadOnlyDictionary<string, object?> outputs)
    {
        if (outputs.Count == 0)
        {
            return $"Skill '{skillId}' completed.";
        }

        var lines = outputs.Select(kv => $"  {kv.Key}: {kv.Value}");
        return $"Skill '{skillId}' completed:\n" + string.Join("\n", lines);
    }
}
