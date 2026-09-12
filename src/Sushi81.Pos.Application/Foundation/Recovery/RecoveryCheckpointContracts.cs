namespace Sushi81.Pos.Application.Foundation.Recovery;

public sealed record RecoveryCheckpointMetadata(
    int SchemaVersion,
    string ProtocolVersion,
    Guid CheckpointId,
    Guid LineageId,
    long Generation,
    Guid SourceDeviceId,
    long BusinessRevision,
    long HandoffVersion,
    DateTimeOffset CreatedAtUtc,
    string DatabaseFileName,
    long DatabaseSize,
    string DatabaseSha256)
{
    public void Validate()
    {
        if (SchemaVersion != 1 || !string.Equals(ProtocolVersion, "M07", StringComparison.Ordinal)
            || CheckpointId == Guid.Empty || LineageId == Guid.Empty || Generation < 1
            || SourceDeviceId == Guid.Empty || BusinessRevision < 0 || HandoffVersion < 0
            || CreatedAtUtc == default || !string.Equals(DatabaseFileName, "checkpoint.db", StringComparison.Ordinal)
            || DatabaseSize <= 0 || !IsSha256(DatabaseSha256))
            throw new InvalidDataException("The OneDrive recovery checkpoint metadata is invalid.");
    }

    private static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64) return false;
        return value.All(Uri.IsHexDigit);
    }
}

public sealed record RecoveryCheckpointPublicationResult(
    RecoveryCheckpointMetadata Metadata,
    string DatabasePath,
    string MetadataPath,
    bool RetentionCompleted);

public sealed record RecoveryCheckpointWatermark(
    int SchemaVersion,
    long BusinessRevision,
    Guid CheckpointId,
    DateTimeOffset CompletedAtUtc)
{
    public void Validate()
    {
        if (SchemaVersion != 1 || BusinessRevision < 0 || CheckpointId == Guid.Empty || CompletedAtUtc == default)
            throw new InvalidDataException("The recovery checkpoint watermark is invalid.");
    }
}

public interface IRecoveryCheckpointPublisher
{
    Task<RecoveryCheckpointPublicationResult> PublishAsync(
        long businessRevision,
        CancellationToken cancellationToken = default);
}

public interface IRecoveryCheckpointScheduler
{
    void NotifyCommitted(DurableChange change);

    Task<bool> TryPublishDueAsync(
        bool force,
        CancellationToken cancellationToken = default);
}
