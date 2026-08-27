namespace Sushi81.Pos.Application.Foundation.Recovery;

public sealed record RecoverySnapshotResult(
    string DatabasePath,
    string MetadataPath,
    string Sha256,
    DateTimeOffset CreatedAtUtc,
    long DurableChangeSequence,
    int SchemaVersion);
