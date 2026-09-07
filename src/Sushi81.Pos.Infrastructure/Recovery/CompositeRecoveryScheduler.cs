using Sushi81.Pos.Application.Foundation.Recovery;

namespace Sushi81.Pos.Infrastructure.Recovery;

/// <summary>
/// Keeps M06 local recovery and M07 cloud checkpoint scheduling independent while forwarding
/// one successful durable business-change notification to both safety spines.
/// </summary>
public sealed class CompositeRecoveryScheduler(
    IRecoveryScheduler localScheduler,
    IRecoveryCheckpointScheduler? cloudScheduler = null) : IRecoveryScheduler, IAsyncDisposable
{
    public void NotifyCommitted(DurableChange change)
    {
        localScheduler.NotifyCommitted(change);
        cloudScheduler?.NotifyCommitted(change);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        await localScheduler.FlushAsync(cancellationToken);
        if (cloudScheduler is not null)
            await cloudScheduler.TryPublishDueAsync(force: true, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (cloudScheduler is IAsyncDisposable cloudDisposable)
            await cloudDisposable.DisposeAsync();
        if (localScheduler is IAsyncDisposable localDisposable)
            await localDisposable.DisposeAsync();
    }
}
