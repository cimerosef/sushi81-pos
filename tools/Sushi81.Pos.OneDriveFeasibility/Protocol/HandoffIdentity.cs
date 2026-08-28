namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

/// <summary>
/// Technical identity of one released handoff. It is deliberately not a business
/// data model; the simulator only exercises lineage/version safety.
/// </summary>
public sealed record HandoffIdentity(string LineageId, long Generation, long HandoffVersion)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(LineageId) &&
        Generation >= 0 &&
        HandoffVersion >= 1;
}

public sealed record AcquisitionClaim(
    string ClaimId,
    HandoffIdentity Handoff,
    string DeviceId)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ClaimId) &&
        !string.IsNullOrWhiteSpace(DeviceId) &&
        Handoff.IsValid;
}
