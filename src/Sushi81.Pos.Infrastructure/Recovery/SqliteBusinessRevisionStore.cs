using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Recovery;

/// <summary>Reads the transactionally advanced business-data revision from the live SQLite database.</summary>
public sealed class SqliteBusinessRevisionStore(
    IAppPaths paths,
    SqliteConnectionFactory connectionFactory) : IBusinessRevisionReader
{
    public async Task<long> ReadAsync(CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(connectionFactory);
        paths.EnsureInitialized();
        if (!File.Exists(paths.LiveDatabasePath)) return 0;

        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(paths.LiveDatabasePath, cancellationToken);
        return await ReadFromConnectionAsync(connection, cancellationToken);
    }

    public static async Task<long> ReadFromDatabaseAsync(string databasePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(databasePath, cancellationToken);
        return await ReadFromConnectionAsync(connection, cancellationToken);
    }

    private static async Task<long> ReadFromConnectionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        try
        {
            var value = await command.ExecuteScalarAsync(cancellationToken);
            if (value is null or DBNull) return 0;
            if (!long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
                || revision < 0)
                throw new InvalidDataException("The canonical business-data revision is malformed.");
            return revision;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            throw new InvalidDataException("The live database lacks the canonical foundation metadata table.", exception);
        }
    }
}
