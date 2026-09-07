namespace Sushi81.Pos.Application.Foundation.Authority;

/// <summary>Staged snapshot bytes that passed exact size/hash/SQLite validation.</summary>
public sealed record TargetSnapshotStaging(string Path, long Size, string Sha256)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Path) || !File.Exists(Path) || Size <= 0
            || !AuthorityProtocolState.IsSha256(Sha256) || new FileInfo(Path).Length != Size)
            throw new InvalidDataException("The staged acquisition snapshot is incomplete.");
    }
}

/// <summary>Data-first installer used by target acquisition; it never changes authority state.</summary>
public interface ITransferSnapshotInstaller
{
    Task<TargetSnapshotStaging> StageAndValidateAsync(
        Guid transferId,
        Stream content,
        string expectedName,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken = default);

    Task InstallAndVerifyAsync(
        TargetSnapshotStaging staging,
        CancellationToken cancellationToken = default);
}
