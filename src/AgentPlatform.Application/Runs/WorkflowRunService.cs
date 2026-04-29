using System.Text.Json;
using AgentPlatform.Application.Abstractions;
using AgentPlatform.Application.Modules;
using AgentPlatform.Application.RunContainers;
using AgentPlatform.Application.Subscription;
using AgentPlatform.Domain.Audit;
using AgentPlatform.Domain.Runs;

namespace AgentPlatform.Application.Runs;

public sealed class WorkflowRunService
{
    private readonly IModuleRegistry _modules;
    private readonly ISubscriptionGate _subscription;
    private readonly IWorkflowRunRepository _repo;
    private readonly PhaseAssignmentService _assignments;
    private readonly IAuditLog _audit;
    private readonly IInboxNotifier _notifier;
    private readonly IWorkingDirectoryProvider _workingDir;
    private readonly IClock _clock;

    public WorkflowRunService(
        IModuleRegistry modules,
        ISubscriptionGate subscription,
        IWorkflowRunRepository repo,
        PhaseAssignmentService assignments,
        IAuditLog audit,
        IInboxNotifier notifier,
        IWorkingDirectoryProvider workingDir,
        IClock clock)
    {
        _modules = modules;
        _subscription = subscription;
        _repo = repo;
        _assignments = assignments;
        _audit = audit;
        _notifier = notifier;
        _workingDir = workingDir;
        _clock = clock;
    }

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
