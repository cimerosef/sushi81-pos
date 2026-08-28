namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

public enum AcquisitionDecisionKind
{
    ReadOnly,
    Writable,
    Blocked
}

public sealed record AcquisitionDecision(
    string DeviceId,
    AcquisitionDecisionKind Decision,
    string Reason)
{
    public bool IsWritable => Decision == AcquisitionDecisionKind.Writable;
}

/// <summary>
/// A placeholder for a documented server/coordination primitive. The file
/// transport simulator never creates one; callers may inject one only to prove
/// that the evaluator binds authority to exactly one device and handoff.
/// </summary>
public sealed record AtomicExclusiveGrant(HandoffIdentity Handoff, string DeviceId)
{
    public bool IsValid => Handoff.IsValid && !string.IsNullOrWhiteSpace(DeviceId);
}
