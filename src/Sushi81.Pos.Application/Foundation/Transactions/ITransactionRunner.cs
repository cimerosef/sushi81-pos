namespace Sushi81.Pos.Application.Foundation.Transactions;

public interface ITransactionRunner
{
    Task ExecuteAsync(
        Func<IApplicationTransaction, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);

    Task<T> ExecuteAsync<T>(
        Func<IApplicationTransaction, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
