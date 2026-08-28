namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

/// <summary>
/// Deliberately unsafe baseline: local claim visibility is treated as a global
/// lock. It is retained only to make the double-writer counterexample executable.
/// </summary>
public static class ClaimOnlyAcquisitionProtocol
{
    public static AcquisitionDecision Evaluate(DeviceView view, HandoffIdentity expected, bool releasedHandoff = true)
    {
        if (!releasedHandoff || !view.ParticipantPresent || !SyncObservation.IsConfirmedInSync(view.SyncState))
        {
            return new(view.DeviceId, AcquisitionDecisionKind.Blocked, "release or transport state is not proven");
        }

        var matching = view.VisibleClaims
            .Where(x => x.Handoff == expected && x.DeviceId == view.DeviceId)
            .DistinctBy(x => x.ClaimId, StringComparer.Ordinal)
            .ToArray();
        return matching.Length == 1
            ? new(view.DeviceId, AcquisitionDecisionKind.Writable, "local claim appears to be the only matching claim")
            : new(view.DeviceId, AcquisitionDecisionKind.Blocked, "matching claim is absent or ambiguous");
    }
}

/// <summary>
/// Safety-preserving candidate for the frozen local-filesystem model. It permits
/// a sole participant, but never turns a locally observed claim into a distributed
/// lock. For N&gt;1 it requires an explicit documented atomic grant that this
/// simulator intentionally cannot manufacture from file synchronization.
/// </summary>
public static class FailClosedAcquisitionProtocol
{
    public static AcquisitionDecision Evaluate(
        DeviceView view,
        IReadOnlyCollection<string> participants,
        HandoffIdentity expected,
        bool releasedHandoff = true,
        bool formalHandoffValid = true,
        AtomicExclusiveGrant? documentedAtomicExclusiveGrant = null)
    {
        if (!expected.IsValid || !releasedHandoff || !formalHandoffValid)
        {
            return Blocked(view.DeviceId, "handoff identity or formal release is not valid");
        }

        if (participants.Count == 0 || participants.Any(string.IsNullOrWhiteSpace) ||
            participants.Distinct(StringComparer.Ordinal).Count() != participants.Count ||
            !participants.Contains(view.DeviceId, StringComparer.Ordinal))
        {
            return Blocked(view.DeviceId, "participant set is invalid or device is not a participant");
        }

        if (!view.ParticipantPresent || !SyncObservation.IsConfirmedInSync(view.SyncState))
        {
            return Blocked(view.DeviceId, "participant presence or transport state is unresolved");
        }

        var sameLineage = view.VisibleClaims
            .Where(x => string.Equals(x.Handoff.LineageId, expected.LineageId, StringComparison.Ordinal))
            .DistinctBy(x => x.ClaimId, StringComparer.Ordinal)
            .ToArray();

        if (sameLineage.Any(x => x.Handoff.Generation != expected.Generation || x.Handoff.HandoffVersion != expected.HandoffVersion))
        {
            return Blocked(view.DeviceId, "stale or conflicting generation/version is visible");
        }

        var matching = sameLineage.Where(x => x.Handoff == expected).ToArray();
        if (matching.Length != 1)
        {
            return Blocked(view.DeviceId, "matching claim is absent or contention is visible");
        }

        if (participants.Count == 1)
        {
            return matching[0].DeviceId == view.DeviceId
                ? new(view.DeviceId, AcquisitionDecisionKind.Writable, "single known participant and one valid claim")
                : Blocked(view.DeviceId, "claim source is not this participant");
        }

        if (documentedAtomicExclusiveGrant is null ||
            !documentedAtomicExclusiveGrant.IsValid ||
            documentedAtomicExclusiveGrant.Handoff != expected)
        {
            return Blocked(view.DeviceId, "file transport provides no documented global exclusive grant");
        }

        return documentedAtomicExclusiveGrant.DeviceId == view.DeviceId && matching[0].DeviceId == view.DeviceId
            ? new(view.DeviceId, AcquisitionDecisionKind.Writable, "explicit atomic exclusive grant confirmed")
            : Blocked(view.DeviceId, "claim source does not match exclusive grant");
    }

    private static AcquisitionDecision Blocked(string deviceId, string reason) =>
        new(deviceId, AcquisitionDecisionKind.Blocked, reason);
}
