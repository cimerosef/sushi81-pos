namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

public enum DirectedTransferState
{
    CloseAndRetain,
    PreparingTransfer,
    RelinquishedPendingGrant,
    TransferReleased,
    NonAuthoritative
}

public sealed record DirectedTransferIdentity(
    HandoffIdentity Handoff,
    string SourceDeviceId,
    string TargetDeviceId,
    string SnapshotChecksum)
{
    public bool IsValid =>
        Handoff.IsValid &&
        !string.IsNullOrWhiteSpace(SourceDeviceId) &&
        !string.IsNullOrWhiteSpace(TargetDeviceId) &&
        !string.Equals(SourceDeviceId, TargetDeviceId, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(SnapshotChecksum);
}

public sealed record DirectedGrantArtifact(
    string GrantId,
    DirectedTransferIdentity Identity,
    bool MetadataValid = true)
{
    public bool IsValid => MetadataValid && !string.IsNullOrWhiteSpace(GrantId) && Identity.IsValid;
}

/// <summary>Durable source-owned state for one directed A-to-B transfer.</summary>
public sealed class DirectedTransferSession
{
    public DirectedTransferSession(DirectedTransferIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsValid)
        {
            throw new ArgumentException("A directed transfer requires valid distinct source and target IDs.", nameof(identity));
        }

        Identity = identity;
    }

    public DirectedTransferIdentity Identity { get; }
    public DirectedTransferState State { get; private set; } = DirectedTransferState.CloseAndRetain;
    public bool SnapshotPublished { get; private set; }
    public bool SnapshotInSync { get; private set; }
    public bool MarkerPublished { get; private set; }
    public bool MarkerInSync { get; private set; }
    public DirectedGrantArtifact? Grant { get; private set; }

    public void BeginPreparingTransfer()
    {
        RequireState(DirectedTransferState.CloseAndRetain);
        State = DirectedTransferState.PreparingTransfer;
    }

    public void PublishSnapshot()
    {
        RequireState(DirectedTransferState.PreparingTransfer);
        SnapshotPublished = true;
    }

    public void ConfirmSnapshotInSync()
    {
        RequireState(DirectedTransferState.PreparingTransfer);
        if (!SnapshotPublished)
        {
            throw new InvalidOperationException("Snapshot must be published before synchronization can be confirmed.");
        }

        SnapshotInSync = true;
    }

    public void PublishReadyMarker()
    {
        RequireState(DirectedTransferState.RelinquishedPendingGrant);
        if (!SnapshotInSync)
        {
            throw new InvalidOperationException("Ready marker cannot be published before snapshot synchronization.");
        }

        MarkerPublished = true;
    }

    public void ConfirmMarkerInSync()
    {
        RequireState(DirectedTransferState.RelinquishedPendingGrant);
        if (!MarkerPublished)
        {
            throw new InvalidOperationException("Ready marker must be published before synchronization can be confirmed.");
        }

        MarkerInSync = true;
    }

    public void RelinquishSource()
    {
        RequireState(DirectedTransferState.PreparingTransfer);
        if (!SnapshotInSync)
        {
            throw new InvalidOperationException("Source can relinquish only after the immutable snapshot is synchronized.");
        }

        State = DirectedTransferState.RelinquishedPendingGrant;
    }

    public void ReleaseTransfer()
    {
        RequireState(DirectedTransferState.RelinquishedPendingGrant);
        if (!MarkerInSync)
        {
            throw new InvalidOperationException("Transfer cannot be released before the target-bound ready marker is synchronized.");
        }

        State = DirectedTransferState.TransferReleased;
    }

    public DirectedGrantArtifact IssueGrant(string grantId)
    {
        RequireState(DirectedTransferState.TransferReleased);
        if (string.IsNullOrWhiteSpace(grantId))
        {
            throw new ArgumentException("Grant ID is required.", nameof(grantId));
        }

        if (Grant is not null)
        {
            if (!string.Equals(Grant.GrantId, grantId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A released transfer cannot be retargeted or issued a second grant.");
            }

            return Grant;
        }

        return Grant = new DirectedGrantArtifact(grantId, Identity);
    }

    public static void Retarget(string targetDeviceId) =>
        throw new InvalidOperationException("A source-directed transfer is immutable and cannot be retargeted.");

    private void RequireState(DirectedTransferState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException($"Expected state {expected}, actual state {State}.");
        }
    }
}

public sealed record DirectedDeviceView(
    string DeviceId,
    DirectedTransferIdentity Identity,
    DirectedTransferState State,
    SimulatedSyncState TransportState,
    bool ParticipantPresent,
    bool AcquisitionValidated,
    bool SnapshotVisible,
    bool SnapshotInSync,
    bool MarkerVisible,
    bool MarkerInSync,
    bool SnapshotIntegrityValid,
    bool MetadataValid,
    bool ReleaseVisible,
    IReadOnlyList<DirectedGrantArtifact> VisibleGrants,
    bool Restarted = false);

/// <summary>
/// Per-device visibility for the directed handoff artifacts. The session is the
/// durable source of truth; this transport deliberately delivers each immutable
/// artifact independently and in caller-selected order.
/// </summary>
public sealed class DirectedVisibilityTransport
{
    private readonly DirectedTransferSession session;
    private readonly Dictionary<string, DeviceArtifacts> devices;

    public DirectedVisibilityTransport(DirectedTransferSession session, IEnumerable<string> deviceIds)
    {
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(deviceIds);
        var ids = deviceIds.ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException("At least one distinct non-empty device ID is required.", nameof(deviceIds));
        }

        devices = ids.ToDictionary(id => id, _ => new DeviceArtifacts(), StringComparer.Ordinal);
    }

    public void SetTransportState(string deviceId, SimulatedSyncState state) => Get(deviceId).TransportState = state;

    public void SetParticipantPresent(string deviceId, bool present) => Get(deviceId).ParticipantPresent = present;

    public void SetAcquisitionValidated(string deviceId, bool validated) => Get(deviceId).AcquisitionValidated = validated;

    public void Restart(string deviceId) => Get(deviceId).Restarted = true;

    public void DeliverSnapshot(string deviceId)
    {
        if (!session.SnapshotPublished)
        {
            throw new InvalidOperationException("The snapshot has not been published.");
        }

        Get(deviceId).SnapshotVisible = true;
    }

    public void DeliverMarker(string deviceId)
    {
        if (!session.MarkerPublished)
        {
            throw new InvalidOperationException("The ready marker has not been published.");
        }

        Get(deviceId).MarkerVisible = true;
    }

    public void DeliverRelease(string deviceId)
    {
        if (session.State != DirectedTransferState.TransferReleased)
        {
            throw new InvalidOperationException("The transfer is not released.");
        }

        Get(deviceId).ReleaseVisible = true;
    }

    public void DeliverGrant(string deviceId)
    {
        if (session.Grant is null)
        {
            throw new InvalidOperationException("The target grant has not been issued.");
        }

        Get(deviceId).VisibleGrants[session.Grant.GrantId] = session.Grant;
    }

    public void ReplayGrant(string deviceId) => DeliverGrant(deviceId);

    public DirectedDeviceView Observe(string deviceId)
    {
        var state = Get(deviceId);
        return new(
            deviceId,
            session.Identity,
            session.State,
            state.TransportState,
            state.ParticipantPresent,
            state.AcquisitionValidated,
            state.SnapshotVisible,
            state.SnapshotVisible && session.SnapshotInSync,
            state.MarkerVisible,
            state.MarkerVisible && session.MarkerInSync,
            state.SnapshotVisible,
            state.SnapshotVisible && state.MarkerVisible,
            state.ReleaseVisible,
            state.VisibleGrants.Values.ToArray(),
            state.Restarted);
    }

    private DeviceArtifacts Get(string deviceId) => devices.TryGetValue(deviceId, out var state)
        ? state
        : throw new KeyNotFoundException($"Unknown device: {deviceId}");

    private sealed class DeviceArtifacts
    {
        public SimulatedSyncState TransportState { get; set; } = SimulatedSyncState.InSync;
        public bool ParticipantPresent { get; set; } = true;
        public bool AcquisitionValidated { get; set; }
        public bool SnapshotVisible { get; set; }
        public bool MarkerVisible { get; set; }
        public bool ReleaseVisible { get; set; }
        public bool Restarted { get; set; }
        public Dictionary<string, DirectedGrantArtifact> VisibleGrants { get; } = new(StringComparer.Ordinal);
    }
}

public sealed record BusinessWriteDecision(
    bool MayWrite,
    AcquisitionDecisionKind Mode,
    string Reason)
{
    public static BusinessWriteDecision Writable(string reason) => new(true, AcquisitionDecisionKind.Writable, reason);
    public static BusinessWriteDecision Blocked(string reason) => new(false, AcquisitionDecisionKind.Blocked, reason);
}

/// <summary>
/// The one authority gate used by the simulator. It is intentionally centralized:
/// no state transition may infer writability outside this decision.
/// </summary>
public static class DirectedAuthorityProtocol
{
    public static bool MayBusinessWrite(DirectedDeviceView view) => Evaluate(view).MayWrite;

    public static BusinessWriteDecision Evaluate(DirectedDeviceView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (!view.Identity.IsValid || string.IsNullOrWhiteSpace(view.DeviceId))
        {
            return BusinessWriteDecision.Blocked("invalid transfer identity");
        }

        if (!view.ParticipantPresent || view.TransportState != SimulatedSyncState.InSync)
        {
            return BusinessWriteDecision.Blocked("participant or transport state is unresolved");
        }

        if (view.State == DirectedTransferState.CloseAndRetain)
        {
            // The source may retain authority before it enters transfer preparation,
            // but a stale local state must not override any observed release artifact.
            return view.DeviceId == view.Identity.SourceDeviceId &&
                !view.ReleaseVisible && !view.MarkerVisible && view.VisibleGrants.Count == 0
                ? BusinessWriteDecision.Writable("source retains authority before transfer preparation")
                : BusinessWriteDecision.Blocked("source state is stale or this device is not the source");
        }

        if (view.State != DirectedTransferState.TransferReleased)
        {
            return BusinessWriteDecision.Blocked("transfer is not released for target acquisition");
        }

        if (view.DeviceId != view.Identity.TargetDeviceId)
        {
            return BusinessWriteDecision.Blocked("transfer is source-directed to another target device");
        }

        if (!view.AcquisitionValidated || !view.SnapshotVisible || !view.MarkerVisible ||
            !view.SnapshotInSync || !view.MarkerInSync || !view.SnapshotIntegrityValid || !view.MetadataValid ||
            !view.ReleaseVisible)
        {
            return BusinessWriteDecision.Blocked("complete synchronized handoff artifacts and validation are required");
        }

        var sameLineage = view.VisibleGrants
            .Where(grant => grant.Identity.Handoff.LineageId == view.Identity.Handoff.LineageId)
            .DistinctBy(grant => grant.GrantId, StringComparer.Ordinal)
            .ToArray();
        if (sameLineage.Any(grant => !grant.IsValid || grant.Identity != view.Identity))
        {
            return BusinessWriteDecision.Blocked("malformed, stale or conflicting grant is visible");
        }

        var matchingGrants = sameLineage.Where(grant => grant.Identity == view.Identity).ToArray();
        return matchingGrants.Length == 1
            ? BusinessWriteDecision.Writable("target has one validated grant for the released directed handoff")
            : BusinessWriteDecision.Blocked("target grant is missing or ambiguous");
    }
}
