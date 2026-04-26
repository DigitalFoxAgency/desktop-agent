using AgentDesktop.Application.Abstractions;
using AgentDesktop.Application.Modules;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Modules;
using AgentDesktop.Domain.Policies;
using Microsoft.Extensions.Logging;

namespace AgentDesktop.Application.Policies;

/// <summary>
/// Default <see cref="IPolicyEngine"/>. Combines the baseline
/// classification table with module-declared policy overrides,
/// queues prompt requests so two confirmations are never on screen
/// simultaneously (FR-014 spirit), and writes every decision to
/// the audit log (FR-022). See <c>contracts/IPolicyEngine.md</c>
/// for the behavioural contract.
/// </summary>
public sealed class DefaultPolicyEngine : IPolicyEngine, IDisposable
{
    private readonly IModuleRegistry _modules;
    private readonly IConfirmationPrompt _prompt;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;
    private readonly ILogger<DefaultPolicyEngine> _logger;
    private readonly SemaphoreSlim _promptGate = new(1, 1);
    private bool _disposed;

    public DefaultPolicyEngine(
        IModuleRegistry modules,
        IConfirmationPrompt prompt,
        IAuditLog audit,
        IClock clock,
        ILogger<DefaultPolicyEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _modules = modules;
        _prompt = prompt;
        _audit = audit;
        _clock = clock;
        _logger = logger;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _promptGate.Dispose();
        _disposed = true;
    }

    public async Task<PolicyDecision> EvaluateAsync(DangerousAction action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);

        await _audit.RecordProposedAsync(action, ct).ConfigureAwait(false);

        var classification = ClassifyAction(action);

        if (classification == ActionClassification.Safe)
        {
            // Safe actions don't surface a prompt; they auto-confirm.
            // Recorded for the audit trail nonetheless.
            var safeDecision = new PolicyDecision(
                action.Id,
                action,
                PolicyOutcome.Confirmed,
                _clock.UtcNow);
            await _audit.RecordDecisionAsync(safeDecision, ct).ConfigureAwait(false);
            return safeDecision;
        }

        // Dangerous: queue the prompt so two are never in flight at once.
        await _promptGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            bool confirmed;
            try
            {
                confirmed = await _prompt.ConfirmAsync(action, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation(
                    "Confirmation prompt for {Kind} on '{Target}' was cancelled.",
                    action.Kind,
                    action.Target);

                var expired = new PolicyDecision(
                    action.Id,
                    action,
                    PolicyOutcome.Expired,
                    _clock.UtcNow);
                await _audit.RecordDecisionAsync(expired, ct).ConfigureAwait(false);
                throw;
            }

            var decision = new PolicyDecision(
                action.Id,
                action,
                confirmed ? PolicyOutcome.Confirmed : PolicyOutcome.Declined,
                _clock.UtcNow);

            await _audit.RecordDecisionAsync(decision, ct).ConfigureAwait(false);
            return decision;
        }
        finally
        {
            _promptGate.Release();
        }
    }

    private ActionClassification ClassifyAction(DangerousAction action)
    {
        // 1. Module-declared override takes priority (the module knows its own actions).
        var (moduleId, _) = ResolveOriginIds(action.Origin);
        var module = _modules.Find(moduleId);
        if (module is not null)
        {
            var override_ = FindOverride(module.Policies, action.Kind);
            if (override_ is not null)
            {
                return override_.Classification;
            }
        }

        // 2. Otherwise the baseline table.
        return BaselineClassificationTable.Classify(action.Kind);
    }

    private static ModulePolicy? FindOverride(IReadOnlyList<ModulePolicy> policies, DangerousActionKind kind)
    {
        foreach (var policy in policies)
        {
            if (string.Equals(policy.ActionClass, kind.ToString(), StringComparison.Ordinal))
            {
                return policy;
            }
        }

        return null;
    }

    private static (ModuleId moduleId, string? second) ResolveOriginIds(PolicyOrigin origin)
    {
        return origin switch
        {
            PolicyOrigin.FromSkill s => (s.ModuleId, s.SkillId.Value),
            PolicyOrigin.FromDelegation d => (d.ModuleId, d.OperationId),
            _ => throw new InvalidOperationException($"Unknown PolicyOrigin: {origin.GetType().Name}"),
        };
    }
}
