namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class ContinuousAuthorityLifecycleTests
{
    private static readonly string[] PairedWithC = ["device-a", "device-b", "device-c"];
    private static readonly long[] ExpectedVersions = [1, 2, 3];
    private static readonly string[] ExpectedSources = ["device-a", "device-b", "device-a"];
    private static readonly string[] ExpectedTargets = ["device-b", "device-a", "device-b"];

    [TestMethod]
    public async Task DistributedDeviceLocalCursorsSupportABv1ThenBAv2ThenABv3WithoutSharedSafetyLedger()
    {
        using var fixture = new Fixture();
        var lineage = Guid.NewGuid().ToString("D");
        var auditLedger = new DirectedLifecycleLedgerStore(fixture.AuditLedgerPath);

        // Leg 1: A -> B v1. B has no source cursor yet, so its exact durable
        // target acquisition establishes its first local cursor.
        var v1 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 1, "device-a", "device-b");
        await fixture.CompleteSourceLegAsync(v1, fixture.SourceAStatePath);
        var v1Target = fixture.CreateTarget("device-b", fixture.TargetBStatePath, fixture.SourceBStatePath);
        Assert.IsFalse(v1Target.MayBusinessWrite(v1));
        var v1Acquired = await v1Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v1);
        Assert.IsTrue(v1Acquired.Succeeded, v1Acquired.Message);
        Assert.HasCount(3, v1Acquired.SyncObservations!);
        Assert.IsTrue(v1Target.MayBusinessWrite(v1));
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceAStatePath)).MayBusinessWrite("device-a"));
        Assert.IsFalse(fixture.CreateTarget("device-c", fixture.TargetCStatePath, fixture.SourceCStatePath).MayBusinessWrite(v1));
        Assert.AreEqual(1, CountWritable(
            new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceAStatePath)).MayBusinessWrite("device-a"),
            v1Target.MayBusinessWrite(v1),
            fixture.CreateTarget("device-c", fixture.TargetCStatePath, fixture.SourceCStatePath).MayBusinessWrite(v1)));
        var v1Revision = v1Target.Current!.Revision;
        Assert.IsTrue(new DirectedContinuousLifecycleCoordinator(auditLedger, "device-b").RecordCompletedTransfer(v1Target.Current!).Succeeded);
        var targetBBytes = await File.ReadAllBytesAsync(fixture.TargetBStatePath);

        // Promotion is performed on B using only B's acquired target state.
        var promoteB = new DirectedContinuousLifecycleCoordinator(null, "device-b")
            .PromoteAcquiredTargetToSource(v1Target.Current!, fixture.SourceBStatePath, PairedWithC);
        Assert.IsTrue(promoteB.Succeeded, promoteB.Message);
        Assert.AreEqual(1, new DurableAuthorityStateStore(fixture.SourceBStatePath).Load().AuthorityCursor!.HandoffVersion);
        CollectionAssert.AreEqual(targetBBytes, await File.ReadAllBytesAsync(fixture.TargetBStatePath));

        // Leg 2: B -> A v2. B prepares this exact +1 from B's local cursor;
        // A acquires using only A's local source/target state and immutable
        // OneDrive handoff artifacts.
        var v2 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 2, "device-b", "device-a");
        await fixture.CompleteSourceLegAsync(v2, fixture.SourceBStatePath);
        var v2Target = fixture.CreateTarget("device-a", fixture.TargetAStatePath, fixture.SourceAStatePath);
        Assert.IsFalse(v2Target.MayBusinessWrite(v2));
        var v2Acquired = await v2Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v2);
        Assert.IsTrue(v2Acquired.Succeeded, v2Acquired.Message);
        Assert.IsTrue(v2Target.MayBusinessWrite(v2));
        Assert.IsTrue(new DirectedContinuousLifecycleCoordinator(auditLedger, "device-a").RecordCompletedTransfer(v2Target.Current!).Succeeded);

        // B has no shared-ledger knowledge here. Its local Released v2 cursor
        // alone supersedes the historical B target v1 evidence.
        var oldBTarget = fixture.CreateTarget("device-b", fixture.TargetBStatePath, fixture.SourceBStatePath);
        Assert.IsFalse(oldBTarget.MayBusinessWrite(v1));
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceBStatePath)).MayBusinessWrite("device-b"));
        Assert.IsFalse(fixture.CreateTarget("device-c", fixture.TargetCStatePath, fixture.SourceCStatePath).MayBusinessWrite(v2));
        Assert.AreEqual(1, CountWritable(
            v2Target.MayBusinessWrite(v2),
            new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceBStatePath)).MayBusinessWrite("device-b"),
            fixture.CreateTarget("device-c", fixture.TargetCStatePath, fixture.SourceCStatePath).MayBusinessWrite(v2)));

        // A stale target cannot be promoted through the real local authority
        // path: B's Released v2 cursor rejects the retained v1 acquisition.
        var staleV1Promotion = new DirectedContinuousLifecycleCoordinator(null, "device-b")
            .PromoteAcquiredTargetToSource(v1Target.Current!, fixture.SourceBStatePath, PairedWithC);
        Assert.IsFalse(staleV1Promotion.Succeeded);
        Assert.AreEqual("stale-target-promotion", staleV1Promotion.Code);

        var targetABytes = await File.ReadAllBytesAsync(fixture.TargetAStatePath);
        var promoteA = new DirectedContinuousLifecycleCoordinator(null, "device-a")
            .PromoteAcquiredTargetToSource(v2Target.Current!, fixture.SourceAStatePath, PairedWithC);
        Assert.IsTrue(promoteA.Succeeded, promoteA.Message);
        Assert.AreEqual(2, new DurableAuthorityStateStore(fixture.SourceAStatePath).Load().AuthorityCursor!.HandoffVersion);
        CollectionAssert.AreEqual(targetABytes, await File.ReadAllBytesAsync(fixture.TargetAStatePath));

        // Leg 3: A -> B v3. B advances its retained target cursor using B's
        // local Released v2 source cursor; no shared ledger is provided.
        var v3 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 3, "device-a", "device-b");
        await fixture.CompleteSourceLegAsync(v3, fixture.SourceAStatePath);
        var v3Target = fixture.CreateTarget("device-b", fixture.TargetBStatePath, fixture.SourceBStatePath);
        Assert.IsFalse(v3Target.MayBusinessWrite(v1));
        var v3Acquired = await v3Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v3);
        Assert.IsTrue(v3Acquired.Succeeded, v3Acquired.Message);
        Assert.AreEqual(v1Revision + 1, v3Target.Current!.Revision);
        Assert.IsTrue(v3Target.MayBusinessWrite(v3));
        Assert.IsTrue(new DirectedContinuousLifecycleCoordinator(auditLedger, "device-b").RecordCompletedTransfer(v3Target.Current!).Succeeded);

        var currentASource = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceAStatePath));
        var currentATarget = fixture.CreateTarget("device-a", fixture.TargetAStatePath, fixture.SourceAStatePath);
        var currentBTarget = fixture.CreateTarget("device-b", fixture.TargetBStatePath, fixture.SourceBStatePath);
        Assert.IsFalse(currentASource.MayBusinessWrite("device-a"));
        Assert.IsFalse(currentATarget.MayBusinessWrite(v2));
        Assert.IsTrue(currentBTarget.MayBusinessWrite(v3));
        Assert.IsFalse(fixture.CreateTarget("device-c", fixture.TargetCStatePath, fixture.SourceCStatePath).MayBusinessWrite(v3));
        Assert.AreEqual(1, CountWritable(
            currentASource.MayBusinessWrite("device-a"),
            currentATarget.MayBusinessWrite(v2),
            currentBTarget.MayBusinessWrite(v3),
            fixture.CreateTarget("device-c", fixture.TargetCStatePath, fixture.SourceCStatePath).MayBusinessWrite(v3)));

        // Delayed-knowledge proof: omit the audit ledger entirely. Local B's
        // Released v2 cursor blocks old v1, and local A's Released v3 cursor
        // blocks old v2, regardless of any hypothetical global update.
        Assert.IsFalse(fixture.CreateTarget("device-b", fixture.TargetBStatePath, fixture.SourceBStatePath).MayBusinessWrite(v1));
        Assert.IsFalse(fixture.CreateTarget("device-a", fixture.TargetAStatePath, fixture.SourceAStatePath).MayBusinessWrite(v2));

        // Replay/equal/conflicting progression is rejected from local state.
        var conflictingV3 = v3 with { TransferId = Guid.NewGuid().ToString("D") };
        var conflictingAcquire = await v3Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, conflictingV3);
        Assert.IsFalse(conflictingAcquire.Succeeded);
        Assert.AreEqual("stale-target-state", conflictingAcquire.Code);
        var wrongV4 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 4, "device-b", "device-a");
        Assert.IsFalse(new DirectedLifecycleAuthorityGate(
            "device-a",
            sourceStateStore: new DurableAuthorityStateStore(fixture.SourceAStatePath),
            targetStateStore: new DurableTargetAcquisitionStore(fixture.TargetAStatePath))
            .Evaluate(wrongV4, new DurableAuthorityStateStore(fixture.SourceAStatePath).Load(), currentATarget.Current).MayWrite);

        var entries = auditLedger.Load();
        CollectionAssert.AreEqual(ExpectedVersions, entries.Select(entry => entry.HandoffVersion).ToArray());
        CollectionAssert.AreEqual(ExpectedSources, entries.Select(entry => entry.SourceDeviceId).ToArray());
        CollectionAssert.AreEqual(ExpectedTargets, entries.Select(entry => entry.TargetDeviceId).ToArray());
        Assert.AreEqual(DirectedAuthorityMode.Released, new DurableAuthorityStateStore(fixture.SourceBStatePath).Load().Mode);
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceBStatePath)).MayBusinessWrite("device-b"));
        Assert.IsTrue(File.Exists(fixture.TargetBStatePath));
    }

    [TestMethod]
    public async Task PairedSetAndCloudObservationFailuresRemainFailClosed()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CompleteSourceLegAsync(
            new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), 1, 1, "device-a", "device-b"),
            fixture.SourceAStatePath);
        var pendingObserver = new FixedSyncObserver(ArtifactSyncStatus.Pending);
        var unpaired = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetBStatePath), "device-b", ["device-b"], pendingObserver);
        Assert.AreEqual("unpaired-target", (await unpaired.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, transfer)).Code);

        var observerBlocked = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetBStatePath), "device-b", ["device-a", "device-b"], pendingObserver);
        var blocked = await observerBlocked.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, transfer);
        Assert.IsFalse(blocked.Succeeded);
        Assert.AreEqual("target-artifacts-not-synchronized", blocked.Code);
        Assert.IsFalse(observerBlocked.MayBusinessWrite(transfer));
    }

    private static int CountWritable(params bool[] decisions) => decisions.Count(decision => decision);

    private sealed class FixedSyncObserver(ArtifactSyncStatus status) : IArtifactSyncObserver
    {
        public ArtifactSyncObservation Observe(string path) => new(Path.GetFullPath(path), status);
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81-M02-distributed-lifecycle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);
            DeviceADirectory = Path.Combine(RootDirectory, "device-a");
            DeviceBDirectory = Path.Combine(RootDirectory, "device-b");
            DeviceCDirectory = Path.Combine(RootDirectory, "device-c");
            Directory.CreateDirectory(DeviceADirectory);
            Directory.CreateDirectory(DeviceBDirectory);
            Directory.CreateDirectory(DeviceCDirectory);
            HandoffDirectory = Path.Combine(RootDirectory, "onedrive-handoff");
            SourceAStatePath = Path.Combine(DeviceADirectory, "source-authority.json");
            SourceBStatePath = Path.Combine(DeviceBDirectory, "source-authority.json");
            SourceCStatePath = Path.Combine(DeviceCDirectory, "source-authority.json");
            TargetAStatePath = Path.Combine(DeviceADirectory, "target-device-a.json");
            TargetBStatePath = Path.Combine(DeviceBDirectory, "target-device-b.json");
            TargetCStatePath = Path.Combine(DeviceCDirectory, "target-device-c.json");
            AuditLedgerPath = Path.Combine(RootDirectory, "diagnostic-audit.jsonl");
        }

        public string RootDirectory { get; }
        public string DeviceADirectory { get; }
        public string DeviceBDirectory { get; }
        public string DeviceCDirectory { get; }
        public string HandoffDirectory { get; }
        public string AuditLedgerPath { get; }
        public string SourceAStatePath { get; }
        public string SourceBStatePath { get; }
        public string SourceCStatePath { get; }
        public string TargetAStatePath { get; }
        public string TargetBStatePath { get; }
        public string TargetCStatePath { get; }
        public string SnapshotPath { get; private set; } = string.Empty;
        public IArtifactSyncObserver ConfirmedObserver { get; } = new FixedSyncObserver(ArtifactSyncStatus.ConfirmedInSync);

        public DirectedTargetAcquisitionCoordinator CreateTarget(string device, string targetStatePath, string localSourceStatePath) =>
            new(
                new DurableTargetAcquisitionStore(targetStatePath),
                device,
                PairedWithC,
                ConfirmedObserver,
                lifecycleLedger: null,
                sourceStateStore: new DurableAuthorityStateStore(localSourceStatePath));

        public async Task<DirectedTransferIdentity> CompleteSourceLegAsync(DirectedTransferIdentity transfer, string sourceStatePath)
        {
            var source = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(sourceStatePath));
            if (File.Exists(sourceStatePath))
            {
                Assert.AreEqual(DirectedAuthorityMode.Authoritative, source.Current.Mode);
                Assert.IsTrue(source.MayBusinessWrite(transfer.SourceDeviceId));
            }
            else
            {
                Assert.IsTrue(source.InitializeAuthoritative(transfer.SourceDeviceId, PairedWithC).Succeeded);
            }

            var prepared = source.PrepareTransfer(transfer);
            Assert.IsTrue(prepared.Succeeded, prepared.Message);
            SnapshotPath = Path.Combine(HandoffDirectory, "directed-" + transfer.TransferId + ".snapshot.db");
            await DirectedSnapshotEvidence.CreateSyntheticAsync(SnapshotPath);
            var evidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, SnapshotPath, true);
            Assert.IsTrue((await source.DurablyRelinquishAsync(evidence)).Succeeded);
            Assert.IsFalse(source.MayBusinessWrite(transfer.SourceDeviceId));
            Assert.IsTrue((await source.PublishReleaseMarkersAsync(HandoffDirectory, SnapshotPath)).Succeeded);
            Assert.AreEqual(DirectedAuthorityMode.Released, source.Current.Mode);
            return transfer;
        }

        public void Dispose()
        {
            if (!Directory.Exists(RootDirectory)) return;
            foreach (var file in Directory.EnumerateFiles(RootDirectory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(RootDirectory, true);
        }
    }
}
