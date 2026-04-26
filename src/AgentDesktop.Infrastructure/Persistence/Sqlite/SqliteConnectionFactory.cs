using AgentDesktop.Application.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgentDesktop.Infrastructure.Persistence.Sqlite;

/// <summary>
/// Owns the per-process SQLite database file and applies embedded
/// SQL migrations from <c>Migrations/*.sql</c> on first connection.
/// Each repository requests a fresh open connection via
/// <see cref="OpenAsync"/>; connections are not pooled in code (the
/// Microsoft.Data.Sqlite driver pools internally).
/// </summary>
public sealed class SqliteConnectionFactory : IDisposable
{
    private readonly SqliteDatabaseOptions _options;
    private readonly ILogger<SqliteConnectionFactory> _logger;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _migrationGate = new(1, 1);
    private bool _migrationsApplied;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _migrationGate.Dispose();
        _disposed = true;
    }

    public SqliteConnectionFactory(
        IOptions<SqliteDatabaseOptions> options,
        IClock clock,
        ILogger<SqliteConnectionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Open a new connection and ensure all pending migrations have run.</summary>
    public async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        await EnsureMigrationsAppliedAsync(ct).ConfigureAwait(false);
        var connection = new SqliteConnection(BuildConnectionString());
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }

    private string BuildConnectionString()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
        };

        return builder.ConnectionString;
    }

    private async Task EnsureMigrationsAppliedAsync(CancellationToken ct)
    {
        if (_migrationsApplied)
        {
            return;
        }

        await _migrationGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_migrationsApplied)
            {
                return;
            }

            EnsureDirectoryExists(_options.DatabasePath);

            await using var connection = new SqliteConnection(BuildConnectionString());
            await connection.OpenAsync(ct).ConfigureAwait(false);

            await using var enableForeignKeys = connection.CreateCommand();
            enableForeignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            await enableForeignKeys.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

            var appliedVersions = await ReadAppliedVersionsAsync(connection, ct).ConfigureAwait(false);

            foreach (var migration in DiscoverMigrations())
            {
                if (appliedVersions.Contains(migration.Version))
                {
                    continue;
                }

                _logger.LogInformation(
                    "Applying SQLite migration {Version} ({Name}) to {DatabasePath}",
                    migration.Version,
                    migration.Name,
                    _options.DatabasePath);

                await using var transaction = (SqliteTransaction)await connection
                    .BeginTransactionAsync(ct)
                    .ConfigureAwait(false);

                await using (var migrationCommand = connection.CreateCommand())
                {
                    migrationCommand.Transaction = transaction;
                    migrationCommand.CommandText = migration.Sql;
                    await migrationCommand.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var record = connection.CreateCommand())
                {
                    record.Transaction = transaction;
                    record.CommandText = "INSERT INTO schema_version (version, applied_at) VALUES ($v, $t);";
                    record.Parameters.AddWithValue("$v", migration.Version);
                    record.Parameters.AddWithValue("$t", _clock.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                    await record.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await transaction.CommitAsync(ct).ConfigureAwait(false);
            }

            _migrationsApplied = true;
        }
        finally
        {
            _migrationGate.Release();
        }
    }

    private static void EnsureDirectoryExists(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static async Task<HashSet<int>> ReadAppliedVersionsAsync(SqliteConnection connection, CancellationToken ct)
    {
        var applied = new HashSet<int>();

        await using var existsCmd = connection.CreateCommand();
        existsCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='schema_version';";
        var schemaTable = await existsCmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        if (schemaTable is null)
        {
            return applied;
        }

        await using var readCmd = connection.CreateCommand();
        readCmd.CommandText = "SELECT version FROM schema_version;";
        await using var reader = await readCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            applied.Add(reader.GetInt32(0));
        }

        return applied;
    }

    private static List<MigrationScript> DiscoverMigrations()
    {
        var assembly = typeof(SqliteConnectionFactory).Assembly;
        const string ResourcePrefix = "AgentDesktop.Infrastructure.Persistence.Sqlite.Migrations.";

        var migrations = new List<MigrationScript>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resource.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            var name = resource[ResourcePrefix.Length..^4];
            var underscoreIndex = name.IndexOf('_', StringComparison.Ordinal);
            if (underscoreIndex < 0 ||
                !int.TryParse(name.AsSpan(0, underscoreIndex), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var version))
            {
                throw new InvalidOperationException(
                    $"Migration resource '{resource}' must follow NNNN_name.sql format.");
            }

            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Cannot read migration resource: {resource}");
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();
            migrations.Add(new MigrationScript(version, name, sql));
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }

    private sealed record MigrationScript(int Version, string Name, string Sql);
}

/// <summary>Configuration for the SQLite database file.</summary>
public sealed class SqliteDatabaseOptions
{
    /// <summary>Absolute path to the SQLite database file.</summary>
    public string DatabasePath { get; set; } = "agent-desktop.db";
}
