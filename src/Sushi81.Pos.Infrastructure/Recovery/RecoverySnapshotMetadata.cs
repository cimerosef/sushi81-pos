namespace Sushi81.Pos.Infrastructure.Recovery;

public sealed record RecoverySnapshotMetadata(
    string Sha256,
    DateTimeOffset CreatedAtUtc,
    int SchemaVersion,
    long DurableChangeSequence);
