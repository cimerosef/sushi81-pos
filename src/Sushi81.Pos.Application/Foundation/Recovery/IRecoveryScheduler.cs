namespace Sushi81.Pos.Application.Foundation.Recovery;

public interface IRecoveryScheduler
{
    void NotifyCommitted(DurableChange change);

    Task FlushAsync(CancellationToken cancellationToken = default);
}
