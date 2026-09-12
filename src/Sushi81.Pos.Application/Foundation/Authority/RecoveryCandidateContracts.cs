namespace Sushi81.Pos.Application.Foundation.Authority;

public enum RecoveryCandidateType
{
    GitHubHandoff,
    OneDriveCheckpoint
}

public enum DisasterRecoveryEntryReason
{
    PairedReplacement,
    NormalAuthorityUnavailable,
    ReleasedTargetUnavailable,
    RelinquishedTransferTargetUnavailable
}

/// <summary>
/// Read-only, phase-derived context shown before a new DR attempt. It is not operator input
/// and carries no authority semantics.
/// </summary>
public sealed record DisasterRecoveryEntryContext(
    AuthorityPhase Phase,
    Guid? LineageId,
    long Generation,
    Guid DeviceId,
    Guid? PriorSourceDeviceId,
    Guid? PriorTargetDeviceId,
    Guid? PriorTransferId,
    long? PriorHandoffVersion,
    DisasterRecoveryEntryReason Reason,
    bool NormalPathUnavailableConfirmationRequired,
    Guid? RecoveryId = null,
    string? RecoveryCandidateId = null,
    string? RecoveryCandidateType = null,
    long? RecoveryBusinessRevision = null,
    long? RecoveryHandoffVersion = null)
{
    public bool IsEligibleForNewRecovery => Phase is AuthorityPhase.PairedUninitializedReadOnly
        or AuthorityPhase.NonAuthoritativeReadOnly
        or AuthorityPhase.ReleasedNonAuthoritative
        or AuthorityPhase.RelinquishedPendingGrant;
}

/// <summary>
/// A candidate is a validated, authority-neutral data source. It never grants authority and
/// carries enough immutable identity for an exact retry after a process restart.
/// </summary>
public sealed record RecoveryCandidate(
    RecoveryCandidateType Type,
    string CandidateId,
    Guid LineageId,
    long Generation,
    Guid SourceDeviceId,
    long BusinessRevision,
    long HandoffVersion,
    DateTimeOffset CreatedAtUtc,
    long PayloadSize,
    string PayloadSha256,
    string StorageReference,
    long? SnapshotAssetId = null,
    long? GrantAssetId = null)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Type) || string.IsNullOrWhiteSpace(CandidateId)
            || LineageId == Guid.Empty || Generation < 1 || SourceDeviceId == Guid.Empty
            || BusinessRevision < 0 || HandoffVersion < 0 || CreatedAtUtc == default
            || PayloadSize <= 0 || !AuthorityProtocolState.IsSha256(PayloadSha256)
            || string.IsNullOrWhiteSpace(StorageReference))
            throw new InvalidDataException("The recovery candidate is incomplete or contradictory.");
        if (Type == RecoveryCandidateType.GitHubHandoff
            && (SnapshotAssetId is not (> 0) || GrantAssetId is not (> 0)))
            throw new InvalidDataException("A GitHub recovery candidate requires exact snapshot and grant assets.");
    }

    public string TypeName => Type switch
    {
        RecoveryCandidateType.GitHubHandoff => "GitHubHandoff",
        RecoveryCandidateType.OneDriveCheckpoint => "OneDriveCheckpoint",
        _ => throw new InvalidDataException("Unsupported recovery candidate type.")
    };
}

public sealed record RecoveryCandidateDiscoveryResult(
    IReadOnlyList<RecoveryCandidate> Candidates,
    string Diagnostic,
    bool IsTemporarilyUnavailable = false)
{
    public RecoveryCandidate? Recommended => Candidates
        .OrderByDescending(candidate => candidate.BusinessRevision)
        .ThenByDescending(candidate => candidate.HandoffVersion)
        .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
        .FirstOrDefault();

    public bool Contains(string candidateId) => Candidates.Any(candidate =>
        string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal));
}

public interface IRecoveryCandidateDiscovery
{
    Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(
        AuthorityProtocolState localState,
        CancellationToken cancellationToken = default);

    Task<RecoveryCandidate?> FindExactAsync(
        AuthorityProtocolState localState,
        string candidateId,
        CancellationToken cancellationToken = default);
}
