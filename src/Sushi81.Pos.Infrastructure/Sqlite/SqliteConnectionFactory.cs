using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Infrastructure.Sqlite;

/// <summary>Creates short-lived, fully configured SQLite connections; it never caches a mutable connection.</summary>
public sealed class SqliteConnectionFactory(IAppPaths paths)
{
    private const int BusyTimeoutMilliseconds = 5000;

    public string LiveDatabasePath => paths.LiveDatabasePath;

    public async Task<SqliteConnection> OpenLiveConnectionAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.LiveDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000
        }.ToString());

        try
        {
            await connection.OpenAsync(cancellationToken);
            await ApplyLivePragmasAsync(connection, cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public static async Task<SqliteConnection> OpenReadOnlyConnectionAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = BusyTimeoutMilliseconds / 1000
        }.ToString());

        try
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;", cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ApplyLivePragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteNonQueryAsync(
            connection,
            "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA synchronous = FULL; PRAGMA busy_timeout = 5000;",
            cancellationToken);

        var foreignKeys = await ExecuteScalarIntAsync(connection, "PRAGMA foreign_keys;", cancellationToken);
        var journalMode = await ExecuteScalarStringAsync(connection, "PRAGMA journal_mode;", cancellationToken);
        var synchronous = await ExecuteScalarIntAsync(connection, "PRAGMA synchronous;", cancellationToken);
        var busyTimeout = await ExecuteScalarIntAsync(connection, "PRAGMA busy_timeout;", cancellationToken);

        if (foreignKeys != 1 || !string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase) || synchronous != 2 || busyTimeout != BusyTimeoutMilliseconds)
        {
            throw new InvalidOperationException("SQLite connection durability PRAGMAs could not be verified.");
        }
    }

    internal static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task<int> ExecuteScalarIntAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static async Task<string> ExecuteScalarStringAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException($"SQLite PRAGMA '{sql}' returned no value.");
    }
}
