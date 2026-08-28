using Sushi81.Pos.OneDriveFeasibility.Protocol;
using ProtocolClaim = Sushi81.Pos.OneDriveFeasibility.Protocol.AcquisitionClaim;

namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class ProtocolSimulatorTests
{
    private static readonly HandoffIdentity Handoff = new("lineage-1", 3, 9);
    private static readonly string[] TwoDevices = ["device-a", "device-b"];

    [TestMethod]
    public void OneDeviceUncontestedAcquisitionIsWritable()
    {
        var transport = new DeterministicSyncTransport(["device-a"]);
        transport.PublishClaim(new ProtocolClaim("claim-a", Handoff, "device-a"));
        transport.DeliverClaim("device-a", "claim-a");

        var decision = FailClosedAcquisitionProtocol.Evaluate(
            transport.Observe("device-a"), ["device-a"], Handoff);

        Assert.AreEqual(AcquisitionDecisionKind.Writable, decision.Decision);
    }

    [TestMethod]
    public void ADocumentedAtomicGrantBindsAuthorityToExactlyOneDevice()
    {
        var transport = NewContentionTransport(TwoDevices);
        var grant = new AtomicExclusiveGrant(Handoff, "device-a");

        var a = FailClosedAcquisitionProtocol.Evaluate(transport.Observe("device-a"), TwoDevices, Handoff, documentedAtomicExclusiveGrant: grant);
        var b = FailClosedAcquisitionProtocol.Evaluate(transport.Observe("device-b"), TwoDevices, Handoff, documentedAtomicExclusiveGrant: grant);

        Assert.AreEqual(AcquisitionDecisionKind.Writable, a.Decision);
        Assert.AreEqual(AcquisitionDecisionKind.Blocked, b.Decision);
    }

    [TestMethod]
    public void TwoDevicesSeeingOnlyTheirOwnDelayedClaimsNeverBecomeWritable()
    {
        var transport = NewContentionTransport(TwoDevices);
        var decisions = TwoDevices
            .Select(id => FailClosedAcquisitionProtocol.Evaluate(transport.Observe(id), TwoDevices, Handoff))
            .ToArray();

        Assert.IsTrue(decisions.All(x => x.Decision == AcquisitionDecisionKind.Blocked));
        Assert.AreEqual(0, decisions.Count(x => x.IsWritable));
    }

    [TestMethod]
    public void ThreeDeviceReorderedVisibilityIsNDeviceAndFailClosed()
    {
        var result = ProtocolSimulation.RunContentionAudit();

        Assert.AreEqual(3, result.DeviceCount);
        Assert.IsTrue(result.ClaimOnlyDoubleWriterObserved);
        Assert.IsFalse(result.FailClosedDoubleWriterObserved);
        Assert.IsTrue(result.FailClosedDecisions.All(x => x.Decision == AcquisitionDecisionKind.Blocked));
    }

    [TestMethod]
    public void EveryTwoDevicePublishDeliverInterleavingExposesUnsafeRaceButNeverSafeDoubleWriter()
    {
        var result = ProtocolSimulation.RunAdversarialInterleavingAudit(["device-a", "device-b"]);

        Assert.AreEqual(6, result.ScheduleCount);
        Assert.IsTrue(result.ClaimOnlyDoubleWriterObserved);
        Assert.IsFalse(result.FailClosedDoubleWriterObserved);
        Assert.IsFalse(result.FailClosedWritableObserved);
        Assert.IsNotEmpty(result.ClaimOnlyWitness);
        StringAssert.Contains(string.Join(' ', result.ClaimOnlyWitness), "publish:");
    }

    [TestMethod]
    public void EveryThreeDeviceInterleavingRemainsNDeviceAndFailClosed()
    {
        var result = ProtocolSimulation.RunAdversarialInterleavingAudit(["device-a", "device-b", "device-c"]);

        Assert.AreEqual(90, result.ScheduleCount);
        Assert.IsTrue(result.ClaimOnlyDoubleWriterObserved);
        Assert.IsFalse(result.FailClosedDoubleWriterObserved);
        Assert.IsFalse(result.FailClosedWritableObserved);
    }

    [TestMethod]
    public void ClaimOnlyBaselineProducesExecutableDoubleWriterCounterexample()
    {
        var transport = NewContentionTransport(TwoDevices);
        var decisions = TwoDevices
            .Select(id => ClaimOnlyAcquisitionProtocol.Evaluate(transport.Observe(id), Handoff))
            .ToArray();

        Assert.AreEqual(2, decisions.Count(x => x.IsWritable));
    }

    [TestMethod]
    public void UnresolvedTransportStatesNeverBecomeWritable()
    {
        foreach (var state in Enum.GetValues<SimulatedSyncState>().Where(state => state != SimulatedSyncState.InSync))
        {
            var transport = new DeterministicSyncTransport(["device-a"]);
            transport.SetSyncState("device-a", state);
            transport.PublishClaim(new ProtocolClaim("claim-a", Handoff, "device-a"));
            transport.DeliverClaim("device-a", "claim-a");

            var decision = FailClosedAcquisitionProtocol.Evaluate(
                transport.Observe("device-a"), ["device-a"], Handoff);

            Assert.AreEqual(AcquisitionDecisionKind.Blocked, decision.Decision, state.ToString());
        }
    }

    [TestMethod]
    public void DropoutNeverTurnsUncertainTwoDeviceStateWritable()
    {
        var transport = NewContentionTransport(TwoDevices);
        transport.SetParticipantPresent("device-b", false);
        var decision = FailClosedAcquisitionProtocol.Evaluate(
            transport.Observe("device-a"), ["device-a", "device-b"], Handoff);

        Assert.AreEqual(AcquisitionDecisionKind.Blocked, decision.Decision);
    }

    [TestMethod]
    public void StaleGenerationAndVersionAreRejected()
    {
        var staleGeneration = new HandoffIdentity(Handoff.LineageId, Handoff.Generation - 1, Handoff.HandoffVersion);
        var staleVersion = new HandoffIdentity(Handoff.LineageId, Handoff.Generation, Handoff.HandoffVersion - 1);

        foreach (var stale in new[] { staleGeneration, staleVersion })
        {
            var transport = new DeterministicSyncTransport(["device-a"]);
            transport.PublishClaim(new ProtocolClaim("stale", stale, "device-a"));
            transport.DeliverClaim("device-a", "stale");
            var decision = FailClosedAcquisitionProtocol.Evaluate(
                transport.Observe("device-a"), ["device-a"], Handoff);
            Assert.AreEqual(AcquisitionDecisionKind.Blocked, decision.Decision);
        }
    }

    [TestMethod]
    public void ConflictingClaimsAndReplayRemainBlockedAndDeterministic()
    {
        var transport = NewContentionTransport(TwoDevices);
        transport.DeliverClaim("device-a", "claim-device-b");
        transport.ReplayClaim("device-a", "claim-device-b");
        var first = FailClosedAcquisitionProtocol.Evaluate(transport.Observe("device-a"), ["device-a", "device-b"], Handoff);
        var second = FailClosedAcquisitionProtocol.Evaluate(transport.Observe("device-a"), ["device-a", "device-b"], Handoff);

        Assert.AreEqual(AcquisitionDecisionKind.Blocked, first.Decision);
        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void ReorderedClaimVisibilityProducesTheSameDecision()
    {
        var firstTransport = NewContentionTransport(TwoDevices);
        var secondTransport = NewContentionTransport(TwoDevices);

        // Both devices eventually see both claims, but each transport receives
        // them in a different order. Evaluation must not depend on arrival order.
        firstTransport.DeliverClaim("device-a", "claim-device-b");
        secondTransport.DeliverClaim("device-b", "claim-device-a");
        firstTransport.DeliverClaim("device-a", "claim-device-a");
        secondTransport.DeliverClaim("device-b", "claim-device-b");

        var first = FailClosedAcquisitionProtocol.Evaluate(firstTransport.Observe("device-a"), TwoDevices, Handoff);
        var second = FailClosedAcquisitionProtocol.Evaluate(secondTransport.Observe("device-b"), TwoDevices, Handoff);

        Assert.AreEqual(AcquisitionDecisionKind.Blocked, first.Decision);
        Assert.AreEqual(AcquisitionDecisionKind.Blocked, second.Decision);
        Assert.AreEqual(first.Reason, second.Reason);
    }

    [TestMethod]
    public void StaleOrConflictingLineageVersionVisibleAlongsideClaimBlocks()
    {
        var transport = new DeterministicSyncTransport(TwoDevices);
        transport.PublishClaim(new ProtocolClaim("current", Handoff, "device-a"));
        transport.PublishClaim(new ProtocolClaim("future", new HandoffIdentity(Handoff.LineageId, Handoff.Generation, Handoff.HandoffVersion + 1), "device-b"));
        transport.DeliverClaim("device-a", "current");
        transport.DeliverClaim("device-a", "future");

        var decision = FailClosedAcquisitionProtocol.Evaluate(
            transport.Observe("device-a"), ["device-a", "device-b"], Handoff);

        Assert.AreEqual(AcquisitionDecisionKind.Blocked, decision.Decision);
    }

    private static DeterministicSyncTransport NewContentionTransport(IReadOnlyList<string> devices)
    {
        var transport = new DeterministicSyncTransport(devices);
        foreach (var id in devices)
        {
            transport.PublishClaim(new ProtocolClaim($"claim-{id}", Handoff, id));
            transport.DeliverClaim(id, $"claim-{id}");
        }

        return transport;
    }
}
