using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Modules;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Application.Subscription;
using AgentPlatform.Application.Tenants;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Inbox;
using AgentPlatform.Domain.Runs;
using AgentPlatform.Domain.Tenants;

namespace AgentPlatform.Application.Runs;

public sealed class WorkflowRunService(
    IModuleRegistry modules,
    ISubscriptionGate subscription,
    IWorkflowRunRepository repo,
    PhaseAssignmentService assignments,
    IAuditLog audit,
    IInboxNotifier notifier,
    IWorkingDirectoryProvider workingDir,
    TenantService tenants,
    IRunContainerDriver containers,
    IClock clock)
{
    private readonly IModuleRegistry _modules = modules;
    private readonly ISubscriptionGate _subscription = subscription;
    private readonly IWorkflowRunRepository _repo = repo;
    private readonly PhaseAssignmentService _assignments = assignments;
    private readonly IAuditLog _audit = audit;
    private readonly IInboxNotifier _notifier = notifier;
    private readonly IWorkingDirectoryProvider _workingDir = workingDir;
    private readonly TenantService _tenants = tenants;
    private readonly IRunContainerDriver _containers = containers;
    private readonly IClock _clock = clock;

    public async Task<StartRunResult> StartAsync(StartRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subscription = await _subscription.CheckAsync(request.TenantId, cancellationToken).ConfigureAwait(false);
        if (!subscription.Allowed)
        {
            return StartRunResult.Failure(subscription.Reason ?? "Subscription not active.");
        }

        var workflow = await _modules.GetWorkflowAsync(request.ModuleId, request.WorkflowId, cancellationToken).ConfigureAwait(false);
        if (workflow is null || workflow.Phases.Count == 0)
        {
            return StartRunResult.Failure($"Workflow '{request.ModuleId}/{request.WorkflowId}' not found.");
        }

        var inputErrors = ValidateInputs(workflow.InputsSchemaJson, request.Inputs);
        if (inputErrors.Count > 0)
        {
            return StartRunResult.Failure("Invalid inputs: " + string.Join("; ", inputErrors));
        }

        var runId = Guid.NewGuid();
        var run = new WorkflowRun
        {
            Id = runId,
            TenantId = request.TenantId,
            WorkflowDefId = workflow.Id,
            ModuleId = request.ModuleId,
            WorkflowId = request.WorkflowId,
            StartedByUserId = request.UserId,
            StartedAt = _clock.UtcNow,
            Status = RunStatus.Running,
            InputsJson = JsonSerializer.Serialize(request.Inputs),
            WorkingDirPath = _workingDir.Resolve(request.TenantId, runId),
        };

        var firstPhaseDef = workflow.Phases.OrderBy(p => p.Order).First();
        var firstPhase = new PhaseRun
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            WorkflowRunId = run.Id,
            PhaseDefId = firstPhaseDef.Id,
            Order = firstPhaseDef.Order,
            PhaseId = firstPhaseDef.PhaseId,
            Status = RunStatus.Pending,
            CreatedAt = _clock.UtcNow,
        };

        var assignment = await _assignments.CreateAsync(
            request.TenantId,
            firstPhase,
            firstPhaseDef.Role,
            $"{workflow.DisplayName} — {firstPhaseDef.DisplayName}",
            $"Phase: {firstPhaseDef.PhaseId}",
            cancellationToken).ConfigureAwait(false);

        await _repo.SaveNewRunAsync(run, firstPhase, assignment.Assignment, assignment.InboxItem, cancellationToken).ConfigureAwait(false);

        if (assignment.InboxItem is not null)
        {
            await _notifier.NotifyAsync(assignment.InboxItem, cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ActorUserId = request.UserId,
            Category = "run",
            Action = "start",
            SubjectType = "workflow_run",
            SubjectId = run.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new { request.ModuleId, request.WorkflowId }),
            OccurredAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);

        return StartRunResult.Success(run.Id, firstPhase.Id, assignment.Assignment.AssignedUserId);
    }

    /// <summary>Marks the given phase as Completed (or Failed if not verified), creates the next phase + assignment, and updates run status. Idempotent: a second call for the same phase is a no-op.</summary>
    public async Task<PhaseHandoffResult> OnPhaseCompletedAsync(Guid tenantId, Guid phaseRunId, bool verified, CancellationToken cancellationToken)
    {
        var run = await _repo.GetRunByPhaseAsync(tenantId, phaseRunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return PhaseHandoffResult.Failure("phase not found");
        }

        var completed = run.Phases.FirstOrDefault(p => p.Id == phaseRunId);
        if (completed is null)
        {
            return PhaseHandoffResult.Failure("phase not found in run");
        }
        if (completed.Status == RunStatus.Completed || completed.Status == RunStatus.Failed)
        {
            return PhaseHandoffResult.Idempotent(run.Id, completed.Id);
        }

        var workflow = await _modules.GetWorkflowAsync(run.ModuleId, run.WorkflowId, cancellationToken).ConfigureAwait(false);
        if (workflow is null)
        {
            return PhaseHandoffResult.Failure("workflow definition missing");
        }

        var orderedPhases = workflow.Phases.OrderBy(p => p.Order).ToList();
        var currentIndex = orderedPhases.FindIndex(p => p.PhaseId == completed.PhaseId);
        var nextDef = currentIndex >= 0 && currentIndex + 1 < orderedPhases.Count ? orderedPhases[currentIndex + 1] : null;

        completed.Status = verified ? RunStatus.Completed : RunStatus.Failed;
        completed.CompletedAt = _clock.UtcNow;

        PhaseRun? nextPhase = null;
        Assignment? assignment = null;
        InboxItem? inbox = null;
        Guid? nextAssignedUserId = null;
        bool waiting = false;

        if (verified && nextDef is not null)
        {
            nextPhase = new PhaseRun
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                WorkflowRunId = run.Id,
                PhaseDefId = nextDef.Id,
                Order = nextDef.Order,
                PhaseId = nextDef.PhaseId,
                Status = RunStatus.Pending,
                CreatedAt = _clock.UtcNow,
            };
            var result = await _assignments.CreateAsync(
                tenantId,
                nextPhase,
                nextDef.Role,
                $"{workflow.DisplayName} — {nextDef.DisplayName}",
                $"Phase: {nextDef.PhaseId}",
                cancellationToken).ConfigureAwait(false);
            assignment = result.Assignment;
            inbox = result.InboxItem;
            nextAssignedUserId = assignment.AssignedUserId;
            waiting = assignment.State == AssignmentState.Waiting;
            run.Status = waiting ? RunStatus.Waiting : RunStatus.Running;
        }
        else if (!verified)
        {
            run.Status = RunStatus.Failed;
            run.CompletedAt = _clock.UtcNow;
        }
        else
        {
            // verified && no next phase → run finished
            run.Status = RunStatus.Completed;
            run.CompletedAt = _clock.UtcNow;
        }

        await _repo.AppendNextPhaseAsync(completed, run, nextPhase, assignment, inbox, cancellationToken).ConfigureAwait(false);

        // Archive the run volume off the live path once the run terminates (success or failure).
        if (run.Status == RunStatus.Completed || run.Status == RunStatus.Failed)
        {
            try
            {
                await _containers.ArchiveVolumeAsync(run.Id.ToString("N"), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort; volume archive failure must not abort the hand-off transaction.
            }
        }

        if (inbox is not null)
        {
            await _notifier.NotifyAsync(inbox, cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = null,
            Category = "phase",
            Action = verified ? "complete" : "fail",
            SubjectType = "phase_run",
            SubjectId = completed.Id.ToString(),
            PayloadJson = JsonSerializer.Serialize(new
            {
                completed.PhaseId,
                nextPhaseId = nextPhase?.PhaseId,
                nextPhaseRunId = nextPhase?.Id,
                nextAssignedUserId,
                waiting,
            }),
            OccurredAt = _clock.UtcNow,
        }, cancellationToken).ConfigureAwait(false);

        return new PhaseHandoffResult(true, run.Id, completed.Id, nextPhase?.Id, nextAssignedUserId, waiting, null);
    }

    /// <summary>Reassigns the current assignment for <paramref name="phaseRunId"/> to <paramref name="newUserId"/>. Validates that the user holds the phase's required role; transitions Waiting → Assigned (and run Waiting → Running) when applicable. Records audit + notifies inbox.</summary>
    public async Task<ReassignResult> ReassignAsync(Guid tenantId, Guid actorUserId, Guid phaseRunId, Guid newUserId, CancellationToken cancellationToken)
    {
        var lookup = await _repo.GetCurrentAssignmentAsync(tenantId, phaseRunId, cancellationToken).ConfigureAwait(false);
        if (lookup is null)
        {
            return ReassignResult.Failure("assignment not found");
        }

        var roles = await _tenants.GetRolesAsync(tenantId, newUserId, cancellationToken).ConfigureAwait(false);
        if (!roles.Contains(lookup.RequiredRole))
        {
            return ReassignResult.Failure($"user does not hold required role '{RoleNames.ToSlug(lookup.RequiredRole)}'");
        }

        var now = _clock.UtcNow;
        var current = lookup.Assignment;
        var previousUserId = current.AssignedUserId;
        current.AssignedUserId = newUserId;
        current.State = AssignmentState.Assigned;
        current.AssignedAt = now;

        var inbox = new InboxItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = newUserId,
            PhaseRunId = phaseRunId,
            Kind = InboxItemKind.PhaseAssigned,
            Title = $"Phase: {lookup.Phase.PhaseId}",
            Subtitle = previousUserId is null ? "Assigned to you" : "Reassigned to you",
            CreatedAt = now,
        };

        var run = lookup.Run;
        if (run.Status == RunStatus.Waiting)
        {
            run.Status = RunStatus.Running;
        }

        await _repo.ReassignAsync(current, inbox, run, cancellationToken).ConfigureAwait(false);
        await _notifier.NotifyAsync(inbox, cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(new AuditEntry
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ActorUserId = actorUserId,
            Category = "phase",
            Action = "reassign",
            SubjectType = "phase_run",
            SubjectId = phaseRunId.ToString(),
            PayloadJson = JsonSerializer.Serialize(new
            {
                assignmentId = current.Id,
                previousUserId,
                newUserId,
                requiredRole = RoleNames.ToSlug(lookup.RequiredRole),
            }),
            OccurredAt = now,
        }, cancellationToken).ConfigureAwait(false);

        return ReassignResult.Success(current.Id, newUserId);
    }

    public Task<WorkflowRun?> GetAsync(Guid tenantId, Guid runId, CancellationToken cancellationToken)
        => _repo.GetAsync(tenantId, runId, cancellationToken);

    public Task<IReadOnlyList<WorkflowRun>> ListAsync(Guid tenantId, int limit, CancellationToken cancellationToken)
        => _repo.ListAsync(tenantId, limit <= 0 ? 50 : Math.Min(limit, 200), cancellationToken);

    private static List<string> ValidateInputs(string schemaJson, IReadOnlyDictionary<string, string?> inputs)
    {
        var errors = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return errors;
            }
            foreach (var prop in doc.RootElement.EnumerateArray())
            {
                var name = prop.GetProperty("name").GetString();
                if (name is null) { continue; }
                var required = prop.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True;
                if (required && (!inputs.TryGetValue(name, out var v) || string.IsNullOrEmpty(v)))
                {
                    errors.Add($"missing required input '{name}'");
                }
            }
        }
        catch (JsonException ex)
        {
            errors.Add(ex.Message);
        }
        return errors;
    }
}

public sealed record StartRunRequest(
    Guid TenantId,
    Guid UserId,
    string ModuleId,
    string WorkflowId,
    IReadOnlyDictionary<string, string?> Inputs);

public sealed record StartRunResult(bool Succeeded, Guid? RunId, Guid? PhaseRunId, Guid? AssignedUserId, string? Error)
{
    public static StartRunResult Success(Guid runId, Guid phaseRunId, Guid? assignedUserId) =>
        new(true, runId, phaseRunId, assignedUserId, null);

    public static StartRunResult Failure(string error) => new(false, null, null, null, error);
}

public sealed record ReassignResult(bool Succeeded, Guid? AssignmentId, Guid? NewUserId, string? Error)
{
    public static ReassignResult Success(Guid assignmentId, Guid newUserId) =>
        new(true, assignmentId, newUserId, null);
    public static ReassignResult Failure(string error) => new(false, null, null, error);
}

public sealed record PhaseHandoffResult(
    bool Succeeded,
    Guid? RunId,
    Guid? CompletedPhaseRunId,
    Guid? NextPhaseRunId,
    Guid? NextAssignedUserId,
    bool Waiting,
    string? Error)
{
    public static PhaseHandoffResult Failure(string error) => new(false, null, null, null, null, false, error);
    public static PhaseHandoffResult Idempotent(Guid runId, Guid completedPhaseRunId) =>
        new(true, runId, completedPhaseRunId, null, null, false, null);
}
