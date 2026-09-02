using Sushi81.Pos.Application.Foundation.Transactions;

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
            var result = await operation(transactionContext, cancellationToken);
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
}
