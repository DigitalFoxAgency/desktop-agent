using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentDesktop.Application.Abstractions;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Policies;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AgentDesktop.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Append-only SQLite-backed <see cref="IAuditLog"/>. Records
/// each <see cref="DangerousAction"/> proposal, decision, and
/// execution result in dedicated tables and additionally writes a
/// JSON breadcrumb to <c>audit_events</c> for FR-022 traceability.
/// </summary>
public sealed class SqliteAuditLog : IAuditLog
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteAuditLog(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task RecordProposedAsync(DangerousAction action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(action);

        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO policy_decisions (
                    id, kind, target,
                    origin_kind, origin_module_id, origin_operation_id, origin_skill_id,
                    requested_at, outcome, decided_at,
                    execution_result, execution_error
                ) VALUES (
                    @Id, @Kind, @Target,
                    @OriginKind, @OriginModuleId, @OriginOperationId, @OriginSkillId,
                    @RequestedAt, @Outcome, @DecidedAt,
                    @ExecutionResult, @ExecutionError
                );
                """,
            parameters: BuildProposedParameters(action),
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await AppendBreadcrumbAsync(
            connection,
            tx,
            kind: "policy.proposed",
            payload: new { action.Id, Kind = action.Kind.ToString(), action.Target },
            requestedAt: action.RequestedAt,
            ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordDecisionAsync(PolicyDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);

        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE policy_decisions
                SET outcome = @Outcome,
                    decided_at = @DecidedAt
                WHERE id = @Id;
                """,
            parameters: new
            {
                Id = decision.Id.Value.ToString("N"),
                Outcome = (int)decision.Outcome,
                DecidedAt = ToIso(decision.DecidedAt),
            },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await AppendBreadcrumbAsync(
            connection,
            tx,
            kind: "policy.decision",
            payload: new
            {
                decision.Id,
                Outcome = decision.Outcome.ToString(),
                Kind = decision.Action.Kind.ToString(),
                decision.Action.Target,
            },
            requestedAt: decision.DecidedAt,
            ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordExecutionAsync(PolicyDecision decision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(decision);

        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE policy_decisions
                SET execution_result = @ExecutionResult,
                    execution_error  = @ExecutionError
                WHERE id = @Id;
                """,
            parameters: new
            {
                Id = decision.Id.Value.ToString("N"),
                ExecutionResult = decision.ExecutionResult is null ? (int?)null : (int)decision.ExecutionResult.Value,
                decision.ExecutionError,
            },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await AppendBreadcrumbAsync(
            connection,
            tx,
            kind: "policy.execution",
            payload: new
            {
                decision.Id,
                Result = decision.ExecutionResult?.ToString() ?? "Unknown",
                decision.ExecutionError,
            },
            requestedAt: decision.DecidedAt,
            ct).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<PolicyDecision> EnumerateAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<DecisionRow>(new CommandDefinition(
            commandText: """
                SELECT id                  AS Id,
                       kind                AS Kind,
                       target              AS Target,
                       origin_kind         AS OriginKind,
                       origin_module_id    AS OriginModuleId,
                       origin_operation_id AS OriginOperationId,
                       origin_skill_id     AS OriginSkillId,
                       requested_at        AS RequestedAt,
                       outcome             AS Outcome,
                       decided_at          AS DecidedAt,
                       execution_result    AS ExecutionResult,
                       execution_error     AS ExecutionError
                FROM policy_decisions
                ORDER BY decided_at DESC;
                """,
            cancellationToken: ct)).ConfigureAwait(false);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return MapRow(row);
            await Task.Yield();
        }
    }

    private static object BuildProposedParameters(DangerousAction action)
    {
        var (moduleId, originOperationId, originSkillId) = action.Origin switch
        {
            PolicyOrigin.FromSkill s => (s.ModuleId.Value, (string?)null, s.SkillId.Value),
            PolicyOrigin.FromDelegation d => (d.ModuleId.Value, d.OperationId, (string?)null),
            _ => throw new InvalidOperationException($"Unknown PolicyOrigin: {action.Origin.GetType().Name}"),
        };

        var originKind = action.Origin switch
        {
            PolicyOrigin.FromSkill => (int)PolicyOriginKind.Skill,
            PolicyOrigin.FromDelegation => (int)PolicyOriginKind.ScenarioStep, // re-used as "delegation" for now
            _ => -1,
        };

        return new
        {
            Id = action.Id.Value.ToString("N"),
            Kind = (int)action.Kind,
            action.Target,
            OriginKind = originKind,
            OriginModuleId = moduleId,
            OriginOperationId = originOperationId,
            OriginSkillId = originSkillId,
            RequestedAt = ToIso(action.RequestedAt),
            // NOTE: outcome + decided_at filled in on RecordDecisionAsync.
            // SQLite NOT NULL constraints require placeholders here.
            Outcome = (int)PolicyOutcome.Expired,
            DecidedAt = ToIso(action.RequestedAt),
            ExecutionResult = (int?)null,
            ExecutionError = (string?)null,
        };
    }

    private static async Task AppendBreadcrumbAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        string kind,
        object payload,
        DateTimeOffset requestedAt,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO audit_events (created_at, kind, payload_json)
                VALUES (@CreatedAt, @Kind, @Payload);
                """,
            parameters: new
            {
                CreatedAt = ToIso(requestedAt),
                Kind = kind,
                Payload = JsonSerializer.Serialize(payload, JsonOptions),
            },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static PolicyDecision MapRow(DecisionRow row)
    {
        var origin = row.OriginKind == (int)PolicyOriginKind.Skill && row.OriginSkillId is not null
            ? (PolicyOrigin)new PolicyOrigin.FromSkill(new ModuleId(row.OriginModuleId), new SkillId(row.OriginSkillId))
            : new PolicyOrigin.FromDelegation(new ModuleId(row.OriginModuleId), row.OriginOperationId ?? string.Empty);

        var action = new DangerousAction(
            new PolicyDecisionId(Guid.ParseExact(row.Id, "N")),
            (DangerousActionKind)row.Kind,
            row.Target,
            origin,
            FromIso(row.RequestedAt));

        var executionResult = row.ExecutionResult is null
            ? (PolicyExecutionResult?)null
            : (PolicyExecutionResult)row.ExecutionResult.Value;

        return new PolicyDecision(
            action.Id,
            action,
            (PolicyOutcome)row.Outcome,
            FromIso(row.DecidedAt),
            executionResult,
            row.ExecutionError);
    }

    private static string ToIso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromIso(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private sealed record DecisionRow(
        string Id,
        long Kind,
        string Target,
        long OriginKind,
        string OriginModuleId,
        string? OriginOperationId,
        string? OriginSkillId,
        string RequestedAt,
        long Outcome,
        string DecidedAt,
        long? ExecutionResult,
        string? ExecutionError);
}
