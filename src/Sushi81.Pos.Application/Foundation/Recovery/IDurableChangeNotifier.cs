namespace Sushi81.Pos.Application.Foundation.Recovery;

/// <summary>
/// Application-level seam invoked only after a durable business transaction has committed.
/// Implementations must not turn a committed business change into a rollback or a failed user operation.
/// </summary>
public interface IDurableChangeNotifier
{
    Task NotifyCommittedAsync(CancellationToken cancellationToken = default);
}

public sealed class NoOpDurableChangeNotifier : IDurableChangeNotifier
{
    public Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
