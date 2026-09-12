using Sushi81.Pos.Application.Foundation.GitHubTransport;

namespace Sushi81.Pos.Application.Foundation.Authority;

public sealed record RecoveryActivationRequest(
    Guid RecoveryId,
    Guid LineageId,
    long PriorGeneration,
    long NextGeneration,
    Guid DeviceId,
    string CandidateId,
    string CandidateSha256,
    long BusinessRevision)
{
    public string AssetName => GitHubHandoffAssetNames.CreateActivationName(LineageId, NextGeneration);

    public void Validate()
    {
        if (RecoveryId == Guid.Empty || LineageId == Guid.Empty || PriorGeneration < 1
            || NextGeneration != PriorGeneration + 1 || DeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(CandidateId) || !AuthorityProtocolState.IsSha256(CandidateSha256)
            || BusinessRevision < 0)
            throw new InvalidDataException("The recovery activation request is incomplete or contradictory.");
    }
}

public sealed record RecoveryActivationArtifact(
    string ProtocolVersion,
    Guid RecoveryId,
    Guid LineageId,
    long PriorGeneration,
    long NextGeneration,
    Guid WinnerDeviceId,
    string CandidateId,
    string CandidateSha256,
    long BusinessRevision,
    DateTimeOffset CreatedAtUtc)
{
    public void Validate()
    {
        if (!string.Equals(ProtocolVersion, "M07", StringComparison.Ordinal)
            || RecoveryId == Guid.Empty || LineageId == Guid.Empty || PriorGeneration < 1
            || NextGeneration != PriorGeneration + 1 || WinnerDeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(CandidateId) || !AuthorityProtocolState.IsSha256(CandidateSha256)
            || BusinessRevision < 0 || CreatedAtUtc == default)
            throw new InvalidDataException("The recovery activation artifact is incomplete or contradictory.");
    }
}

public enum RecoveryActivationOutcome
{
    Won,
    ResumedSameWinner,
    LostToExistingWinner,
    BlockedNoWinner
}

public sealed record RecoveryActivationResult(
    RecoveryActivationOutcome Outcome,
    RemoteAssetEvidence? Receipt,
    string Diagnostic)
{
    public bool Accepted => Outcome is RecoveryActivationOutcome.Won or RecoveryActivationOutcome.ResumedSameWinner;
}
