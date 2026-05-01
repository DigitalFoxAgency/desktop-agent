using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Bridge;
using AgentPlatform.Application.Runs;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Policies;
using Microsoft.Extensions.Logging;

namespace AgentPlatform.Application.Policies;

/// <summary>
/// Persists <see cref="ConfirmationRequest"/> rows raised by the bridge,
/// records audit entries on proposal + decision, and dispatches the user's
/// decision back into the running phase via <see cref="IBridgeChannel"/>.
/// </summary>
public sealed class ConfirmationService(
    IConfirmationRepository repo,
    IAuditLog audit,
    IClock clock,
    IPhaseSessionService sessions,
    ILogger<ConfirmationService> log)
{
    private readonly IConfirmationRepository _repo = repo;
    private readonly IAuditLog _audit = audit;
    private readonly IClock _clock = clock;
    private readonly IPhaseSessionService _sessions = sessions;
    private readonly ILogger<ConfirmationService> _log = log;

    public async Task RecordProposalAsync(
        Guid tenantId,
        Guid phaseRunId,
        Guid? requestedByUserId,
        Guid confirmationId,
        ActionClassification classification,
        string summary,
        string? targetPath,
        string? commandLine,
        CancellationToken cancellationToken)
    {
        var request = new ConfirmationRequest
        {
            Id = confirmationId,
            TenantId = tenantId,
            PhaseRunId = phaseRunId,
            RequestedByUserId = requestedByUserId ?? Guid.Empty,
            Classification = classification,
            ActionSummary = summary,
            TargetPath = targetPath,
            CommandLine = commandLine,
            CreatedAt = _clock.UtcNow,
        };
        await _repo.AddAsync(request, cancellationToken).ConfigureAwait(false);

        await _repo.AddDangerousActionAsync(new DangerousAction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            PhaseRunId = phaseRunId,
            Classification = classification,
            Summary = summary,
            TargetPath = targetPath,
            CommandLine = commandLine,
            DetectedAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = null,
            Category = "confirmation",
            Action = "proposed",
            SubjectType = "confirmation_request",
            SubjectId = confirmationId.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { classification = classification.ToString(), summary, targetPath, commandLine }),
            OccurredAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DecideResult> DecideAsync(
        Guid tenantId,
        Guid userId,
        Guid confirmationId,
        bool confirmed,
        string? note,
        CancellationToken cancellationToken)
    {
        var request = await _repo.GetAsync(tenantId, confirmationId, cancellationToken).ConfigureAwait(false);
        if (request is null)
        {
            return DecideResult.NotFound;
        }
        if (request.Decision is not null)
        {
            return DecideResult.AlreadyDecided;
        }

        request.Decision = new ConfirmationDecision(confirmed, userId, _clock.UtcNow, note);
        await _repo.UpdateAsync(request, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = userId,
            Category = "confirmation",
            Action = confirmed ? "confirmed" : "declined",
            SubjectType = "confirmation_request",
            SubjectId = confirmationId.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { confirmed, note }),
            OccurredAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);

        // Dispatch the decision down to the bridge so claude can resume
        // (or skip) the proposed action. If the session has gone away
        // (phase closed before user decided), drop silently.
        var handle = _sessions.GetActive(request.PhaseRunId);
        if (handle is null)
        {
            _log.LogWarning("No active session for phase {Phase} when resolving confirmation {Id}", request.PhaseRunId, confirmationId);
            return DecideResult.OkSessionGone;
        }

        try
        {
            await handle.Channel.ResolveConfirmationAsync(confirmationId, confirmed, note, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to forward confirmation decision to bridge");
            return DecideResult.OkSessionGone;
        }

        return DecideResult.Ok;
    }
}

public enum DecideResult
{
    Ok = 0,
    NotFound = 1,
    AlreadyDecided = 2,
    OkSessionGone = 3,
}
