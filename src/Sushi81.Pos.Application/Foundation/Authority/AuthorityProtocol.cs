namespace Sushi81.Pos.Application.Foundation.Authority;

/// <summary>The durable M07 phases; only validated authoritative phases permit business writes.</summary>
public enum AuthorityPhase
{
    Uninitialized,
    PairedUninitializedReadOnly,
    Authoritative,
    ClosedRetainedAuthority,
    TransferPreparing,
    RelinquishedPendingGrant,
    ReleasedNonAuthoritative,
    TargetAcquisitionPending,
    NonAuthoritativeReadOnly,
    StaleGeneration,
    DisasterRecoveryPending,
    RecoveryRequired
}

public sealed record RemoteAssetEvidence(
    long ReleaseId,
    long AssetId,
    string Name,
    long Size,
    string Sha256,
    string State = "uploaded")
{
    public void Validate()
    {
        if (ReleaseId < 1 || AssetId < 1 || string.IsNullOrWhiteSpace(Name) || Size < 0
            || !string.Equals(State, "uploaded", StringComparison.OrdinalIgnoreCase)
            || !AuthorityProtocolState.IsSha256(Sha256))
            throw new InvalidDataException("The remote asset receipt is not a strict uploaded receipt.");
    }
}

public sealed record TransferEvidence(
    Guid TransferId, Guid LineageId, long Generation, long Version,
    Guid SourceDeviceId, Guid TargetDeviceId, long BusinessRevision,
    string SnapshotName, string SnapshotPath, long SnapshotSize, string SnapshotSha256,
    RemoteAssetEvidence? SnapshotReceipt = null, RemoteAssetEvidence? GrantReceipt = null,
    DateTimeOffset? RelinquishedAtUtc = null, bool SnapshotReady = true,
    DateTimeOffset? GrantCreatedAtUtc = null)
{
    public static TransferEvidence Pending(
        Guid transferId,
        Guid lineageId,
        long generation,
        long version,
        Guid sourceDeviceId,
        Guid targetDeviceId,
        long businessRevision) => new(
            transferId, lineageId, generation, version, sourceDeviceId, targetDeviceId, businessRevision,
            string.Empty, string.Empty, 0, string.Empty, SnapshotReady: false);
}

public sealed record RecoveryActivationEvidence(
    Guid RecoveryId, Guid DeviceId, Guid LineageId, long PriorGeneration, long NextGeneration,
    string CandidateId, string CandidateSha256, long BusinessRevision,
    RemoteAssetEvidence? Receipt = null);

/// <summary>Canonical identity and protocol state, embedded in authority-state.json only.</summary>
public sealed record AuthorityProtocolState(
    long Revision, Guid DeviceId, string DisplayName, Guid? LineageId, long Generation,
    long HandoffVersion, long BusinessRevision, AuthorityPhase Phase,
    TransferEvidence? Transfer = null, RecoveryActivationEvidence? Recovery = null)
{
    public WriteAuthorityState WriteState => Phase switch
    {
        AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority => WriteAuthorityState.Authoritative,
        AuthorityPhase.TransferPreparing or AuthorityPhase.RelinquishedPendingGrant
            or AuthorityPhase.TargetAcquisitionPending or AuthorityPhase.DisasterRecoveryPending => WriteAuthorityState.Transitioning,
        AuthorityPhase.Uninitialized or AuthorityPhase.RecoveryRequired => WriteAuthorityState.RecoveryRequired,
        _ => WriteAuthorityState.NonAuthoritativeReadOnly
    };

    public void Validate()
    {
        if (Revision < 1 || DeviceId == Guid.Empty || string.IsNullOrWhiteSpace(DisplayName)
            || Generation < 0 || HandoffVersion < 0 || BusinessRevision < 0 || !Enum.IsDefined(Phase)
            || LineageId is { } lineage && lineage == Guid.Empty)
            throw new InvalidDataException("Invalid canonical authority identity or revision.");
        if (Phase is not (AuthorityPhase.Uninitialized or AuthorityPhase.RecoveryRequired or AuthorityPhase.NonAuthoritativeReadOnly)
            && (LineageId is null || Generation < 1))
            throw new InvalidDataException("An established protocol phase requires lineage and generation.");
        if (Phase == AuthorityPhase.Uninitialized
            && (LineageId is not null || Generation != 0 || HandoffVersion != 0 || BusinessRevision != 0
                || Transfer is not null || Recovery is not null))
            throw new InvalidDataException("Uninitialized authority cannot carry established identity or evidence.");
        if (Phase == AuthorityPhase.NonAuthoritativeReadOnly
            && ((LineageId is null && Generation != 0) || (LineageId is not null && Generation < 1)))
            throw new InvalidDataException("A non-authoritative device cannot carry a contradictory generation.");

        if (Phase is AuthorityPhase.TransferPreparing or AuthorityPhase.RelinquishedPendingGrant
            or AuthorityPhase.ReleasedNonAuthoritative or AuthorityPhase.TargetAcquisitionPending)
        {
            if (Transfer is not { } transfer || transfer.TransferId == Guid.Empty
                || transfer.LineageId != LineageId || transfer.Generation != Generation
                || transfer.Version != HandoffVersion || transfer.Version < 1
                || transfer.SourceDeviceId == Guid.Empty || transfer.TargetDeviceId == Guid.Empty
                || transfer.SourceDeviceId == transfer.TargetDeviceId || transfer.BusinessRevision != BusinessRevision
                || (Phase == AuthorityPhase.TargetAcquisitionPending ? transfer.TargetDeviceId : transfer.SourceDeviceId) != DeviceId)
                throw new InvalidDataException("Authority phase contradicts its transfer binding.");
            transfer.Validate();
            if (Phase != AuthorityPhase.TransferPreparing && (transfer.SnapshotReceipt is null || transfer.RelinquishedAtUtc is null))
                throw new InvalidDataException("Relinquished/acquiring state requires immutable snapshot evidence.");
            if (Phase != AuthorityPhase.TransferPreparing && !transfer.SnapshotReady)
                throw new InvalidDataException("Relinquished/acquiring state requires a prepared snapshot.");
            if (Phase is AuthorityPhase.ReleasedNonAuthoritative or AuthorityPhase.TargetAcquisitionPending && transfer.GrantReceipt is null)
                throw new InvalidDataException("Released/acquiring state requires grant evidence.");
            if (Phase == AuthorityPhase.TransferPreparing && transfer.GrantReceipt is not null)
                throw new InvalidDataException("A grant cannot exist before durable relinquishment.");
        }
        if (Phase == AuthorityPhase.DisasterRecoveryPending
            && (Recovery is not { Receipt: not null } recovery || recovery.DeviceId != DeviceId
                || recovery.LineageId != LineageId || recovery.NextGeneration != recovery.PriorGeneration + 1
                || recovery.RecoveryId == Guid.Empty))
            throw new InvalidDataException("Recovery pending requires exact server activation evidence.");
        if (Phase == AuthorityPhase.DisasterRecoveryPending)
            Recovery!.Validate();
        if (Phase is AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority
            or AuthorityPhase.PairedUninitializedReadOnly or AuthorityPhase.NonAuthoritativeReadOnly
            or AuthorityPhase.StaleGeneration or AuthorityPhase.RecoveryRequired)
        {
            if (Transfer is not null || Recovery is not null)
                throw new InvalidDataException("A settled authority phase cannot carry active transfer or recovery evidence.");
        }
    }

    internal static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64) return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }
        return true;
    }
}

public static class AuthorityProtocolValidationExtensions
{
    public static void Validate(this TransferEvidence transfer)
    {
        if (transfer.TransferId == Guid.Empty || transfer.LineageId == Guid.Empty
            || transfer.Generation < 1 || transfer.Version < 1
            || transfer.SourceDeviceId == Guid.Empty || transfer.TargetDeviceId == Guid.Empty
            || transfer.SourceDeviceId == transfer.TargetDeviceId
            || transfer.BusinessRevision < 0)
            throw new InvalidDataException("The transfer evidence is incomplete or contradictory.");
        if (!transfer.SnapshotReady)
        {
            if (!string.IsNullOrEmpty(transfer.SnapshotName) || !string.IsNullOrEmpty(transfer.SnapshotPath)
                || transfer.SnapshotSize != 0 || !string.IsNullOrEmpty(transfer.SnapshotSha256)
                || transfer.SnapshotReceipt is not null || transfer.GrantReceipt is not null
                || transfer.RelinquishedAtUtc is not null || transfer.GrantCreatedAtUtc is not null)
                throw new InvalidDataException("A pending transfer cannot carry partial snapshot or grant evidence.");
            return;
        }
        if (string.IsNullOrWhiteSpace(transfer.SnapshotName) || string.IsNullOrWhiteSpace(transfer.SnapshotPath)
            || transfer.SnapshotSize < 0 || !AuthorityProtocolState.IsSha256(transfer.SnapshotSha256))
            throw new InvalidDataException("The transfer snapshot evidence is incomplete or contradictory.");
        transfer.SnapshotReceipt?.Validate();
        transfer.GrantReceipt?.Validate();
        if (transfer.SnapshotReceipt is { } receipt
            && (receipt.Name != transfer.SnapshotName || receipt.Size != transfer.SnapshotSize
                || !string.Equals(receipt.Sha256, transfer.SnapshotSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The snapshot receipt does not match the local snapshot evidence.");
        if (transfer.GrantReceipt is not null && transfer.SnapshotReceipt is null)
            throw new InvalidDataException("A grant receipt cannot exist without a snapshot receipt.");
    }

    public static void Validate(this RecoveryActivationEvidence recovery)
    {
        if (recovery.RecoveryId == Guid.Empty || recovery.DeviceId == Guid.Empty || recovery.LineageId == Guid.Empty
            || recovery.PriorGeneration < 1 || recovery.NextGeneration != recovery.PriorGeneration + 1
            || string.IsNullOrWhiteSpace(recovery.CandidateId) || !AuthorityProtocolState.IsSha256(recovery.CandidateSha256)
            || recovery.BusinessRevision < 0)
            throw new InvalidDataException("The recovery activation evidence is incomplete or contradictory.");
        recovery.Receipt?.Validate();
    }
}
