using System.Globalization;
using System.Runtime.CompilerServices;
using AgentDesktop.Application.Chat;
using AgentDesktop.Domain;
using AgentDesktop.Domain.Chat;
using Dapper;
using Microsoft.Data.Sqlite;

namespace AgentDesktop.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Dapper-backed <see cref="IChatRepository"/>. Append-only;
/// messages are inserted in transaction-per-call shape. See
/// data-model.md "Persistence sketch" + research.md R15.
/// </summary>
public sealed class SqliteChatRepository : IChatRepository
{
    private readonly SqliteConnectionFactory _factory;

    public SqliteChatRepository(SqliteConnectionFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    public async Task AddAsync(Conversation conversation, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO conversations (id, title, created_at, last_activity_at)
                VALUES (@Id, @Title, @CreatedAt, @LastActivityAt);
                """,
            parameters: new
            {
                Id = conversation.Id.Value.ToString("N"),
                conversation.Title,
                CreatedAt = ToIso(conversation.CreatedAt),
                LastActivityAt = ToIso(conversation.LastActivityAt),
            },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        foreach (var message in conversation.Messages)
        {
            await InsertMessageAsync(connection, tx, message, ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<Conversation?> GetAsync(ConversationId id, CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);

        var convRow = await connection.QuerySingleOrDefaultAsync<ConversationRow?>(new CommandDefinition(
            commandText: """
                SELECT id              AS Id,
                       title           AS Title,
                       created_at      AS CreatedAt,
                       last_activity_at AS LastActivityAt
                FROM conversations
                WHERE id = @Id;
                """,
            parameters: new { Id = id.Value.ToString("N") },
            cancellationToken: ct)).ConfigureAwait(false);

        if (convRow is null)
        {
            return null;
        }

        var messageRows = await connection.QueryAsync<MessageRow>(new CommandDefinition(
            commandText: """
                SELECT id                       AS Id,
                       conversation_id          AS ConversationId,
                       idx                      AS Idx,
                       author                   AS Author,
                       body                     AS Body,
                       created_at               AS CreatedAt,
                       delegation_module_id     AS DelegationModuleId,
                       delegation_operation_id  AS DelegationOperationId,
                       skill_module_id          AS SkillModuleId,
                       skill_id                 AS SkillId
                FROM messages
                WHERE conversation_id = @Id
                ORDER BY idx ASC;
                """,
            parameters: new { Id = id.Value.ToString("N") },
            cancellationToken: ct)).ConfigureAwait(false);

        var messages = messageRows.Select(MapMessage).ToList();

        return new Conversation(
            new ConversationId(Guid.ParseExact(convRow.Id, "N")),
            convRow.Title,
            FromIso(convRow.CreatedAt),
            FromIso(convRow.LastActivityAt),
            messages);
    }

    public async IAsyncEnumerable<Conversation> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);

        var rows = await connection.QueryAsync<ConversationRow>(new CommandDefinition(
            commandText: """
                SELECT id              AS Id,
                       title           AS Title,
                       created_at      AS CreatedAt,
                       last_activity_at AS LastActivityAt
                FROM conversations
                ORDER BY last_activity_at DESC;
                """,
            cancellationToken: ct)).ConfigureAwait(false);

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            // Eagerly load messages so the returned Conversation is usable
            // immediately. For large histories we'll add pagination later.
            var convo = await GetAsync(new ConversationId(Guid.ParseExact(row.Id, "N")), ct).ConfigureAwait(false);
            if (convo is not null)
            {
                yield return convo;
            }
        }
    }

    public async Task AppendMessageAsync(Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await InsertMessageAsync(connection, tx, message, ct).ConfigureAwait(false);

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE conversations
                SET last_activity_at = @LastActivityAt
                WHERE id = @Id;
                """,
            parameters: new
            {
                Id = message.ConversationId.Value.ToString("N"),
                LastActivityAt = ToIso(message.CreatedAt),
            },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateMetadataAsync(ConversationId id, string title, DateTimeOffset lastActivityAt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(title);

        await using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE conversations
                SET title = @Title, last_activity_at = @LastActivityAt
                WHERE id = @Id;
                """,
            parameters: new
            {
                Id = id.Value.ToString("N"),
                Title = title,
                LastActivityAt = ToIso(lastActivityAt),
            },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static async Task InsertMessageAsync(
        SqliteConnection connection,
        SqliteTransaction tx,
        Message message,
        CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO messages
                    (id, conversation_id, idx, author, body, created_at,
                     delegation_module_id, delegation_operation_id,
                     skill_module_id, skill_id)
                VALUES
                    (@Id, @ConversationId, @Idx, @Author, @Body, @CreatedAt,
                     @DelegationModuleId, @DelegationOperationId,
                     @SkillModuleId, @SkillId);
                """,
            parameters: new
            {
                Id = message.Id.Value.ToString("N"),
                ConversationId = message.ConversationId.Value.ToString("N"),
                Idx = message.Index,
                Author = (int)message.Author,
                message.Body,
                CreatedAt = ToIso(message.CreatedAt),
                DelegationModuleId = message.OriginatingDelegation?.ModuleId.Value,
                DelegationOperationId = message.OriginatingDelegation?.OperationId,
                SkillModuleId = message.OriginatingSkill?.ModuleId.Value,
                SkillId = message.OriginatingSkill?.SkillId.Value,
            },
            transaction: tx,
            cancellationToken: ct)).ConfigureAwait(false);
    }

    private static Message MapMessage(MessageRow row)
    {
        DelegationRef? delegation = row.DelegationModuleId is not null && row.DelegationOperationId is not null
            ? new DelegationRef(new ModuleId(row.DelegationModuleId), row.DelegationOperationId)
            : null;

        SkillRef? skill = row.SkillModuleId is not null && row.SkillId is not null
            ? new SkillRef(new ModuleId(row.SkillModuleId), new SkillId(row.SkillId))
            : null;

        return new Message(
            id: new MessageId(Guid.ParseExact(row.Id, "N")),
            conversationId: new ConversationId(Guid.ParseExact(row.ConversationId, "N")),
            index: (int)row.Idx,
            author: (MessageAuthor)row.Author,
            body: row.Body,
            createdAt: FromIso(row.CreatedAt),
            originatingDelegation: delegation,
            originatingSkill: skill);
    }

    private static string ToIso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset FromIso(string value) =>
        DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record ConversationRow(string Id, string Title, string CreatedAt, string LastActivityAt);

    private sealed record MessageRow(
        string Id,
        string ConversationId,
        long Idx,
        long Author,
        string Body,
        string CreatedAt,
        string? DelegationModuleId,
        string? DelegationOperationId,
        string? SkillModuleId,
        string? SkillId);
}
