using Sushi81.Pos.Application.Foundation.Transactions;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Sushi81.Pos.Infrastructure.Sqlite;

public sealed class SqliteTransactionRunner(SqliteConnectionFactory connectionFactory, Func<Exception?>? commitFailureInjector = null) : ITransactionRunner
{
    public Task ExecuteAsync(
        Func<IApplicationTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<object?>(
            async (transaction, token) =>
            {
                await operation(transaction, token);
                return null;
            },
            cancellationToken);

    public async Task<T> ExecuteAsync<T>(
        Func<IApplicationTransaction, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await using var connection = await connectionFactory.OpenLiveConnectionAsync(cancellationToken);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var transactionContext = new SqliteApplicationTransaction(connection, transaction);

        try
        {
            var changesBefore = await ReadTotalChangesAsync(connection, transaction, cancellationToken);
            var result = await operation(transactionContext, cancellationToken);
            var changesAfter = await ReadTotalChangesAsync(connection, transaction, cancellationToken);
            if (changesAfter > changesBefore)
                await AdvanceBusinessRevisionAsync(connection, transaction, cancellationToken);
            if (commitFailureInjector?.Invoke() is { } exception) throw exception;
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<long> ReadTotalChangesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT total_changes();";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static async Task AdvanceBusinessRevisionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using (var table = connection.CreateCommand())
        {
            table.Transaction = transaction;
            table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='foundation_metadata';";
            if (Convert.ToInt32(await table.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
                return;
        }

        await using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        var current = await read.ExecuteScalarAsync(cancellationToken);
        if (current is null or DBNull)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision','1');";
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        if (!long.TryParse(Convert.ToString(current, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 0)
            throw new InvalidDataException("The canonical business-data revision is malformed.");

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE foundation_metadata SET value=$value WHERE key='business_data_revision';";
        update.Parameters.AddWithValue("$value", checked(revision + 1).ToString(CultureInfo.InvariantCulture));
        await update.ExecuteNonQueryAsync(cancellationToken);
    }
}
