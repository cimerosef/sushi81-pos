namespace Sushi81.Pos.Application.Foundation.Recovery;

public interface ILocalRecoverySnapshotService
{
    Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default);
}
