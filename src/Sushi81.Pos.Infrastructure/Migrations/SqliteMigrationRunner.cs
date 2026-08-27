using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Migrations;

/// <summary>Ordered fail-closed SQL migration runner. It never replaces a failed live database.</summary>
public sealed class SqliteMigrationRunner
{
    private readonly SqliteConnectionFactory connectionFactory;
    private readonly SqliteMigration[] migrations;
    private readonly IBusinessClock clock;
    private readonly ILocalRecoverySnapshotService? snapshotService;

    public SqliteMigrationRunner(
        SqliteConnectionFactory connectionFactory,
        IEnumerable<SqliteMigration> migrations,
        IBusinessClock clock,
        ILocalRecoverySnapshotService? snapshotService = null)
    {
        this.connectionFactory = connectionFactory;
        this.migrations = migrations.OrderBy(migration => migration.Version).ToArray();
        this.clock = clock;
        this.snapshotService = snapshotService;
        ValidateMigrations(this.migrations);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var existingDatabase = File.Exists(connectionFactory.LiveDatabasePath)
            && new FileInfo(connectionFactory.LiveDatabasePath).Length > 0;
        await using var connection = await connectionFactory.OpenLiveConnectionAsync(cancellationToken);
        var currentVersion = await GetKnownCurrentVersionAsync(connection, cancellationToken);
        var latestVersion = migrations.Length == 0 ? 0 : migrations[^1].Version;

        if (currentVersion > latestVersion)
        {
            throw new DatabaseMigrationException($"The live database schema version {currentVersion} is newer than this application supports ({latestVersion}). Startup is blocked to protect data.");
        }

        await ValidateAppliedHistoryAsync(connection, currentVersion, cancellationToken);

        var pending = migrations.Where(migration => migration.Version > currentVersion).ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        if (existingDatabase && snapshotService is null)
        {
            throw new DatabaseMigrationException("The existing live database requires a schema upgrade, but no validated pre-migration recovery snapshot service is configured. Startup is blocked to protect data.");
        }

        if (existingDatabase)
        {
            await snapshotService!.CreateAsync(new DurableChange(currentVersion, clock.UtcNow), cancellationToken);
        }

        await EnsureMigrationMetadataAsync(connection, cancellationToken);

        foreach (var migration in pending)
        {
            try
            {
                await ApplyMigrationAsync(connection, migration, cancellationToken);
            }
            catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
            {
                throw new DatabaseMigrationException($"Migration {migration.Version} '{migration.Name}' failed. The live database was not reset or replaced; correct the failure before restarting.", exception);
            }
        }
    }

    private async Task ApplyMigrationAsync(SqliteConnection connection, SqliteMigration migration, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var migrationCommand = connection.CreateCommand())
            {
                migrationCommand.Transaction = transaction;
                migrationCommand.CommandText = migration.Sql;
                await migrationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var recordCommand = connection.CreateCommand())
            {
                recordCommand.Transaction = transaction;
                recordCommand.CommandText = "INSERT INTO schema_migrations(version, name, applied_at_utc) VALUES ($version, $name, $appliedAtUtc);";
                recordCommand.Parameters.AddWithValue("$version", migration.Version);
                recordCommand.Parameters.AddWithValue("$name", migration.Name);
                recordCommand.Parameters.AddWithValue("$appliedAtUtc", clock.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                await recordCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task EnsureMigrationMetadataAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await SqliteConnectionFactory.ExecuteNonQueryAsync(
            connection,
            "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER NOT NULL PRIMARY KEY, name TEXT NOT NULL, applied_at_utc TEXT NOT NULL);",
            cancellationToken);
    }

    private static async Task<int> GetKnownCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var tableCommand = connection.CreateCommand();
        tableCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_migrations';";
        var schemaMigrationsExists = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) == 1;
        return schemaMigrationsExists
            ? await SqliteConnectionFactory.ExecuteScalarIntAsync(connection, "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;", cancellationToken)
            : 0;
    }

    private async Task ValidateAppliedHistoryAsync(SqliteConnection connection, int currentVersion, CancellationToken cancellationToken)
    {
        if (currentVersion == 0)
        {
            return;
        }

        var applied = new Dictionary<int, string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT version, name FROM schema_migrations ORDER BY version;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetInt32(0), reader.GetString(1));
            }
        }

        foreach (var expected in migrations.Where(migration => migration.Version <= currentVersion))
        {
            if (!applied.TryGetValue(expected.Version, out var appliedName) || !string.Equals(appliedName, expected.Name, StringComparison.Ordinal))
            {
                throw new DatabaseMigrationException($"The applied migration history is not contiguous and stable at version {expected.Version}. Startup is blocked to protect data.");
            }
        }

        if (applied.Count != migrations.Count(migration => migration.Version <= currentVersion))
        {
            throw new DatabaseMigrationException("The applied migration history contains an unknown or non-contiguous version. Startup is blocked to protect data.");
        }
    }

    private static void ValidateMigrations(IReadOnlyList<SqliteMigration> migrations)
    {
        var previous = 0;
        foreach (var migration in migrations)
        {
            migration.Validate();
            if (migration.Version != previous + 1)
            {
                throw new ArgumentException("Migration versions must be contiguous increasing integers starting at 1.", nameof(migrations));
            }

            previous = migration.Version;
        }
    }
}
