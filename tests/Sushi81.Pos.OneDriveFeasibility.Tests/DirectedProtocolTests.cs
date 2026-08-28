using Sushi81.Pos.OneDriveFeasibility.Protocol;
using ProtocolDirectedTransferIdentity = Sushi81.Pos.OneDriveFeasibility.Protocol.DirectedTransferIdentity;

namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class DirectedProtocolTests
{
    private static readonly HandoffIdentity Handoff = new("lineage-directed", 4, 12);
    private static readonly string[] DeviceIds = ["device-a", "device-b", "device-c"];
    private static readonly string[] ExpectedWritableTarget = ["device-b"];

    [TestMethod]
    public void CloseAndRetainSourceMayWriteBeforePreparation()
    {
        var session = NewSession();
        var view = View(session, "device-a", DirectedTransferState.CloseAndRetain);

        Assert.IsTrue(DirectedAuthorityProtocol.MayBusinessWrite(view));
    }

    [TestMethod]
    public void CloseAndRetainStaleViewWithReleaseMarkerIsBlocked()
    {
        var session = NewSession();
        var view = View(session, "device-a", DirectedTransferState.CloseAndRetain) with
        {
            MarkerVisible = true,
            MarkerInSync = true
        };

        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(view));
    }

    [TestMethod]
    public void PreparationAndRelinquishedPendingGrantAreNotWritable()
    {
        var session = NewSession();
        session.BeginPreparingTransfer();
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(View(session, "device-a", DirectedTransferState.PreparingTransfer)));

        session.PublishSnapshot();
        session.ConfirmSnapshotInSync();
        session.RelinquishSource();
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(View(session, "device-a", DirectedTransferState.RelinquishedPendingGrant)));
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(View(session, "device-b", DirectedTransferState.RelinquishedPendingGrant)));

        session.PublishReadyMarker();
        session.ConfirmMarkerInSync();
    }

    [TestMethod]
    public void SnapshotMustBeConfirmedBeforeReadyMarker()
    {
        var session = NewSession();
        session.BeginPreparingTransfer();
        session.PublishSnapshot();

        Assert.Throws<InvalidOperationException>(session.PublishReadyMarker);
        Assert.IsFalse(session.MarkerPublished);

        session.ConfirmSnapshotInSync();
        Assert.Throws<InvalidOperationException>(session.PublishReadyMarker);
        Assert.IsFalse(session.MarkerPublished);
    }

    [TestMethod]
    public void MarkerSyncPendingKeepsAllDevicesBlocked()
    {
        var session = NewSession();
        session.BeginPreparingTransfer();
        session.PublishSnapshot();
        session.ConfirmSnapshotInSync();
        session.RelinquishSource();
        session.PublishReadyMarker();

        var transport = new DirectedVisibilityTransport(session, DeviceIds);
        foreach (var id in DeviceIds)
        {
            transport.DeliverSnapshot(id);
            transport.DeliverMarker(id);
            transport.SetAcquisitionValidated(id, true);
        }

        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(transport.Observe("device-a")));
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(transport.Observe("device-b")));
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(transport.Observe("device-c")));
    }

    [TestMethod]
    public void NormalDirectedPathHasExactlyOneWritableTarget()
    {
        var session = ReleasedSession();
        var transport = new DirectedVisibilityTransport(session, DeviceIds);
        foreach (var id in DeviceIds)
        {
            transport.DeliverSnapshot(id);
            transport.DeliverMarker(id);
            transport.DeliverRelease(id);
            transport.DeliverGrant(id);
            transport.SetAcquisitionValidated(id, true);
        }

        var views = DeviceIds
            .Select(transport.Observe)
            .ToArray();

        var writable = views.Where(DirectedAuthorityProtocol.MayBusinessWrite).Select(x => x.DeviceId).ToArray();

        CollectionAssert.AreEqual(ExpectedWritableTarget, writable);
    }

    [TestMethod]
    public void ArbitraryArtifactVisibilityCannotReplaceTheDirectedTarget()
    {
        var session = ReleasedSession();
        var transport = new DirectedVisibilityTransport(session, ["device-a", "device-b", "device-c"]);

        // C sees a complete release first; B sees only the snapshot. Neither can
        // write until B receives and validates its target-bound marker and grant.
        transport.DeliverSnapshot("device-c");
        transport.DeliverMarker("device-c");
        transport.DeliverRelease("device-c");
        transport.DeliverGrant("device-c");
        transport.SetAcquisitionValidated("device-c", true);
        transport.DeliverSnapshot("device-b");

        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(transport.Observe("device-c")));
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(transport.Observe("device-b")));

        // Delayed/reordered delivery of the remaining B artifacts eventually
        // establishes the one valid writable path.
        transport.DeliverGrant("device-b");
        transport.DeliverRelease("device-b");
        transport.DeliverMarker("device-b");
        transport.SetAcquisitionValidated("device-b", true);
        Assert.IsTrue(DirectedAuthorityProtocol.MayBusinessWrite(transport.Observe("device-b")));
    }

    [TestMethod]
    public void ThirdDeviceSeeingCompleteArtifactsStillCannotSubstituteForTarget()
    {
        var session = ReleasedSession();
        var c = View(session, "device-c", DirectedTransferState.TransferReleased);

        var result = DirectedAuthorityProtocol.Evaluate(c);

        Assert.IsFalse(result.MayWrite);
        StringAssert.Contains(result.Reason, "target");
    }

    [TestMethod]
    public void TargetSeeingOnlySnapshotRemainsBlocked()
    {
        var session = ReleasedSession();
        var b = View(session, "device-b", DirectedTransferState.TransferReleased) with
        {
            MarkerVisible = false,
            MarkerInSync = false,
            ReleaseVisible = false,
            VisibleGrants = []
        };

        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(b));
    }

    [TestMethod]
    public void TargetOfflineCannotWriteAndThirdDeviceCannotSubstitute()
    {
        var session = ReleasedSession();
        var offlineTarget = View(session, "device-b", DirectedTransferState.TransferReleased) with
        {
            ParticipantPresent = false,
            TransportState = SimulatedSyncState.Unknown
        };
        var thirdDevice = View(session, "device-c", DirectedTransferState.TransferReleased);

        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(offlineTarget));
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(thirdDevice));
    }

    [TestMethod]
    public void DuplicateReplayOfTheSameGrantIsIdempotent()
    {
        var session = ReleasedSession();
        var b = View(session, "device-b", DirectedTransferState.TransferReleased);
        var grant = b.VisibleGrants.Single();
        b = b with { VisibleGrants = [grant, grant] };

        Assert.IsTrue(DirectedAuthorityProtocol.MayBusinessWrite(b));
    }

    [TestMethod]
    public void StaleReplayOrMalformedGrantBlocksTarget()
    {
        var session = ReleasedSession();
        var b = View(session, "device-b", DirectedTransferState.TransferReleased);
        var stale = new DirectedGrantArtifact(
            "stale-grant",
            new ProtocolDirectedTransferIdentity(new HandoffIdentity(Handoff.LineageId, Handoff.Generation, Handoff.HandoffVersion - 1), "device-a", "device-b", Handoff.LineageId));
        var malformed = new DirectedGrantArtifact("malformed", session.Identity, MetadataValid: false);

        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(b with { VisibleGrants = [stale] }));
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(b with { VisibleGrants = [malformed] }));
    }

    [TestMethod]
    public void RestartRequiresRevalidationButDoesNotCreateASecondWriter()
    {
        var session = ReleasedSession();
        var restartedTarget = View(session, "device-b", DirectedTransferState.TransferReleased) with { Restarted = true };
        var restartedSource = View(session, "device-a", DirectedTransferState.TransferReleased) with { Restarted = true };

        Assert.IsTrue(DirectedAuthorityProtocol.MayBusinessWrite(restartedTarget));
        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(restartedSource));
    }

    [TestMethod]
    public void RetryIsIdempotentAndRetargetIsRejected()
    {
        var session = ReleasedSession();
        var first = session.IssueGrant("grant-1");
        var retry = session.IssueGrant("grant-1");

        Assert.AreSame(first, retry);
        Assert.Throws<InvalidOperationException>(() => session.IssueGrant("grant-2"));
        Assert.Throws<InvalidOperationException>(() => DirectedTransferSession.Retarget("device-c"));
    }

    [TestMethod]
    public void SelfTargetIsRejectedBeforeAnyTransfer()
    {
        var identity = new ProtocolDirectedTransferIdentity(Handoff, "device-a", "device-a", "checksum");

        Assert.Throws<ArgumentException>(() => new DirectedTransferSession(identity));
    }

    [TestMethod]
    public void SourceCannotRelinquishOrReleaseOutOfOrder()
    {
        var session = NewSession();

        Assert.Throws<InvalidOperationException>(session.RelinquishSource);
        Assert.Throws<InvalidOperationException>(session.ReleaseTransfer);
    }

    [TestMethod]
    public void InvalidArtifactAndUnknownTransportStateNeverWrite()
    {
        var session = ReleasedSession();
        var b = View(session, "device-b", DirectedTransferState.TransferReleased) with
        {
            TransportState = SimulatedSyncState.Unknown,
            SnapshotIntegrityValid = false,
            MetadataValid = false
        };

        Assert.IsFalse(DirectedAuthorityProtocol.MayBusinessWrite(b));
    }

    private static DirectedTransferSession NewSession() =>
        new(new ProtocolDirectedTransferIdentity(Handoff, "device-a", "device-b", "snapshot-checksum"));

    private static DirectedTransferSession ReleasedSession()
    {
        var session = NewSession();
        session.BeginPreparingTransfer();
        session.PublishSnapshot();
        session.ConfirmSnapshotInSync();
        session.RelinquishSource();
        session.PublishReadyMarker();
        session.ConfirmMarkerInSync();
        session.ReleaseTransfer();
        session.IssueGrant("grant-1");
        return session;
    }

    private static DirectedDeviceView View(
        DirectedTransferSession session,
        string deviceId,
        DirectedTransferState state,
        bool acquisitionValidated = true) =>
        new(
            deviceId,
            session.Identity,
            state,
            SimulatedSyncState.InSync,
            ParticipantPresent: true,
            acquisitionValidated,
            SnapshotVisible: session.SnapshotPublished,
            SnapshotInSync: session.SnapshotInSync,
            MarkerVisible: session.MarkerPublished,
            MarkerInSync: session.MarkerInSync,
            SnapshotIntegrityValid: true,
            MetadataValid: true,
            ReleaseVisible: session.State == DirectedTransferState.TransferReleased,
            VisibleGrants: session.Grant is null ? [] : [session.Grant],
            Restarted: false);
}
