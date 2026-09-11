using Sushi81.Pos.Application.Foundation.GitHubTransport;

namespace Sushi81.Pos.Application.Foundation.Authority;

/// <summary>Validated local snapshot bytes prepared for one immutable source transfer.</summary>
public sealed record TransferSnapshot(
    string Name,
    string Path,
    long Size,
    string Sha256,
    long BusinessRevision)
{
    public void Validate()
    {
        if (!GitHubHandoffAssetNames.IsSnapshotName(Name)
            || string.IsNullOrWhiteSpace(Path)
            || Size <= 0
            || !AuthorityProtocolState.IsSha256(Sha256)
            || BusinessRevision < 0
            || !File.Exists(Path))
            throw new InvalidDataException("The transfer snapshot is incomplete or unavailable.");
        var actualSize = new FileInfo(Path).Length;
        if (actualSize != Size)
            throw new InvalidDataException("The transfer snapshot size changed after preparation.");
    }
}

/// <summary>Production adapters create a complete, SQLite-safe snapshot without changing authority.</summary>
public interface ITransferSnapshotFactory
{
    Task<TransferSnapshot> CreateAsync(
        AuthorityProtocolState source,
        Guid transferId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Manual-acceptance-only seam at the irreversible normal-handoff boundary.
/// Implementations must not change authority state or perform remote I/O.
/// </summary>
public interface INormalHandoffFaultProbe
{
    Task BeforeTargetReleasingGrantAsync(
        AuthorityProtocolState pendingState,
        CancellationToken cancellationToken = default);
}

/// <summary>Immutable target-bound grant bytes emitted only after source relinquishment is durable.</summary>
public sealed record NormalHandoffGrant(
    string ProtocolVersion,
    Guid TransferId,
    Guid LineageId,
    long Generation,
    long HandoffVersion,
    Guid SourceDeviceId,
    Guid TargetDeviceId,
    long BusinessRevision,
    RemoteAssetEvidence SnapshotReceipt,
    DateTimeOffset RelinquishedAtUtc,
    DateTimeOffset CreatedAtUtc)
{
    public void Validate()
    {
        if (!string.Equals(ProtocolVersion, "M07", StringComparison.Ordinal)
            || TransferId == Guid.Empty || LineageId == Guid.Empty || Generation < 1 || HandoffVersion < 1
            || SourceDeviceId == Guid.Empty || TargetDeviceId == Guid.Empty || SourceDeviceId == TargetDeviceId
            || BusinessRevision < 0 || RelinquishedAtUtc == default || CreatedAtUtc == default)
            throw new InvalidDataException("The normal handoff grant is incomplete or contradictory.");
        SnapshotReceipt.Validate();
    }
}
