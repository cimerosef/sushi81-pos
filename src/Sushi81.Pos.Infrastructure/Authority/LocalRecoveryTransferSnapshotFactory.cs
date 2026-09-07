using System.Security.Cryptography;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.GitHubTransport;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>
/// Adapts the existing validated local recovery snapshot spine for one normal handoff.
/// It creates no authority and keeps the transfer protocol independent of M02 tooling.
/// </summary>
public sealed class LocalRecoveryTransferSnapshotFactory(
    ILocalRecoverySnapshotService recoverySnapshots,
    IBusinessClock clock) : ITransferSnapshotFactory
{
    public async Task<TransferSnapshot> CreateAsync(
        AuthorityProtocolState source,
        Guid transferId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (transferId == Guid.Empty) throw new ArgumentException("A transfer ID is required.", nameof(transferId));
        source.Validate();

        var local = await recoverySnapshots.CreateAsync(
            new DurableChange(source.BusinessRevision, clock.UtcNow), cancellationToken);
        var path = local.DatabasePath;
        var size = new FileInfo(path).Length;
        var hash = await ComputeSha256Async(path, cancellationToken);
        if (!string.Equals(hash, local.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The local transfer snapshot changed after recovery validation.");

        var snapshot = new TransferSnapshot(
            GitHubHandoffAssetNames.CreateSnapshotName(clock.UtcNow),
            path,
            size,
            hash,
            source.BusinessRevision);
        snapshot.Validate();
        return snapshot;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
