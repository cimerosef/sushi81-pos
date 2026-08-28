namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class ContinuousAuthorityLifecycleTests
{
    private static readonly string[] Paired = ["device-a", "device-b"];
    private static readonly string[] PairedWithC = ["device-a", "device-b", "device-c"];
    private static readonly long[] ExpectedVersions = [1, 2, 3];
    private static readonly string[] ExpectedSources = ["device-a", "device-b", "device-a"];
    private static readonly string[] ExpectedTargets = ["device-b", "device-a", "device-b"];

    [TestMethod]
    public async Task SameLineageGenerationSupportsABv1ThenBAv2ThenABv3WithoutDeletingDurableState()
    {
        using var fixture = new Fixture();
        var lineage = Guid.NewGuid().ToString("D");
        var ledger = new DirectedLifecycleLedgerStore(fixture.LedgerPath);
        var lifecycle = new DirectedContinuousLifecycleCoordinator(ledger, "device-a");

        var v1 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 1, "device-a", "device-b");
        var v1Source = await fixture.CompleteSourceLegAsync(v1, fixture.SourceAStatePath);
        var v1Target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetBStatePath), "device-b", PairedWithC, fixture.ConfirmedObserver, ledger);
        Assert.IsFalse(v1Target.MayBusinessWrite(v1));
        var v1Acquired = await v1Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v1);
        Assert.IsTrue(v1Acquired.Succeeded, v1Acquired.Message);
        Assert.HasCount(3, v1Acquired.SyncObservations!);
        var v1State = v1Target.Current!;
        Assert.IsTrue(lifecycle.RecordCompletedTransfer(new DurableAuthorityStateStore(fixture.SourceAStatePath), v1State).Succeeded);
        Assert.IsTrue(v1Target.MayBusinessWrite(v1));
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceAStatePath), lifecycleLedger: ledger).MayBusinessWrite("device-a"));
        Assert.IsFalse(new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetCStatePath), "device-c", PairedWithC, lifecycleLedger: ledger).MayBusinessWrite(v1));
        Assert.AreEqual(1, CountWritable(
            new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceAStatePath), lifecycleLedger: ledger).MayBusinessWrite("device-a"),
            v1Target.MayBusinessWrite(v1),
            new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetCStatePath), "device-c", PairedWithC, lifecycleLedger: ledger).MayBusinessWrite(v1)));
        var targetBBytes = await File.ReadAllBytesAsync(fixture.TargetBStatePath);

        var promoteB = new DirectedContinuousLifecycleCoordinator(ledger, "device-b")
            .PromoteAcquiredTargetToSource(v1State, fixture.SourceBStatePath, PairedWithC);
        Assert.IsTrue(promoteB.Succeeded, promoteB.Message);
        CollectionAssert.AreEqual(targetBBytes, await File.ReadAllBytesAsync(fixture.TargetBStatePath));

        var v2 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 2, "device-b", "device-a");
        var bLifecycle = new DirectedContinuousLifecycleCoordinator(ledger, "device-b");
        Assert.IsTrue(bLifecycle.ValidateNextTransfer(v2, PairedWithC).Succeeded);
        var v2Source = await fixture.CompleteSourceLegAsync(v2, fixture.SourceBStatePath);
        var v2Target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetAStatePath), "device-a", PairedWithC, fixture.ConfirmedObserver, ledger);
        var v2Acquired = await v2Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v2);
        Assert.IsTrue(v2Acquired.Succeeded, v2Acquired.Message);
        var v2State = v2Target.Current!;
        Assert.IsTrue(bLifecycle.RecordCompletedTransfer(new DurableAuthorityStateStore(fixture.SourceBStatePath), v2State).Succeeded);
        Assert.IsTrue(v2Target.MayBusinessWrite(v2));
        var oldBTarget = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetBStatePath), "device-b", PairedWithC, lifecycleLedger: ledger);
        Assert.IsFalse(oldBTarget.MayBusinessWrite(v1));
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceBStatePath), lifecycleLedger: ledger).MayBusinessWrite("device-b"));
        Assert.IsFalse(new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetCStatePath), "device-c", PairedWithC, lifecycleLedger: ledger).MayBusinessWrite(v2));
        Assert.AreEqual(1, CountWritable(
            v2Target.MayBusinessWrite(v2),
            new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceBStatePath), lifecycleLedger: ledger).MayBusinessWrite("device-b"),
            new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetCStatePath), "device-c", PairedWithC, lifecycleLedger: ledger).MayBusinessWrite(v2)));

        var staleV1Promotion = new DirectedContinuousLifecycleCoordinator(ledger, "device-b")
            .PromoteAcquiredTargetToSource(v1State, Path.Combine(fixture.DirectoryPath, "stale-v1-authority.json"), PairedWithC);
        Assert.IsFalse(staleV1Promotion.Succeeded);
        Assert.AreEqual("stale-target-promotion", staleV1Promotion.Code);

        var targetABytes = await File.ReadAllBytesAsync(fixture.TargetAStatePath);
        var promoteA = new DirectedContinuousLifecycleCoordinator(ledger, "device-a")
            .PromoteAcquiredTargetToSource(v2State, fixture.SourceAStatePath, PairedWithC);
        Assert.IsTrue(promoteA.Succeeded, promoteA.Message);
        CollectionAssert.AreEqual(targetABytes, await File.ReadAllBytesAsync(fixture.TargetAStatePath));

        var v3 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 3, "device-a", "device-b");
        var aLifecycle = new DirectedContinuousLifecycleCoordinator(ledger, "device-a");
        Assert.IsTrue(aLifecycle.ValidateNextTransfer(v3, PairedWithC).Succeeded);
        var v3Source = await fixture.CompleteSourceLegAsync(v3, fixture.SourceAStatePath);
        var v3Target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetBStatePath), "device-b", PairedWithC, fixture.ConfirmedObserver, ledger);
        var v3Acquired = await v3Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v3);
        Assert.IsTrue(v3Acquired.Succeeded, v3Acquired.Message);
        var v3State = v3Target.Current!;
        Assert.AreEqual(v1State.Revision + 1, v3State.Revision);
        Assert.IsTrue(aLifecycle.RecordCompletedTransfer(new DurableAuthorityStateStore(fixture.SourceAStatePath), v3State).Succeeded);

        var currentBTarget = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetBStatePath), "device-b", PairedWithC, lifecycleLedger: ledger);
        var currentATarget = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetAStatePath), "device-a", PairedWithC, lifecycleLedger: ledger);
        var currentASource = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceAStatePath), lifecycleLedger: ledger);
        Assert.IsFalse(currentASource.MayBusinessWrite("device-a"));
        Assert.IsTrue(currentBTarget.MayBusinessWrite(v3));
        Assert.IsFalse(currentATarget.MayBusinessWrite(v2));
        Assert.IsFalse(oldBTarget.MayBusinessWrite(v1));
        Assert.IsFalse(new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetCStatePath), "device-c", PairedWithC, lifecycleLedger: ledger).MayBusinessWrite(v3));
        Assert.AreEqual(1, CountWritable(
            currentASource.MayBusinessWrite("device-a"),
            currentBTarget.MayBusinessWrite(v3),
            new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetCStatePath), "device-c", PairedWithC, lifecycleLedger: ledger).MayBusinessWrite(v3)));

        var entries = ledger.Load();
        CollectionAssert.AreEqual(ExpectedVersions, entries.Select(entry => entry.HandoffVersion).ToArray());
        CollectionAssert.AreEqual(ExpectedSources, entries.Select(entry => entry.SourceDeviceId).ToArray());
        CollectionAssert.AreEqual(ExpectedTargets, entries.Select(entry => entry.TargetDeviceId).ToArray());

        var replayV1 = v1 with { TransferId = Guid.NewGuid().ToString("D"), SourceDeviceId = "device-b", TargetDeviceId = "device-a" };
        Assert.AreEqual("stale-or-replayed-version", bLifecycle.ValidateNextTransfer(replayV1, PairedWithC).Code);
        Assert.AreEqual("stale-or-replayed-version", bLifecycle.ValidateNextTransfer(v2, PairedWithC).Code);
        var conflictingV3 = v3 with { TransferId = Guid.NewGuid().ToString("D") };
        var conflictingAcquire = await v3Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, conflictingV3);
        Assert.IsFalse(conflictingAcquire.Succeeded);
        Assert.AreEqual("stale-target-state", conflictingAcquire.Code);
        var wrongV4 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 4, "device-a", "device-b");
        Assert.IsFalse(new DirectedContinuousLifecycleCoordinator(ledger, "device-a").ValidateNextTransfer(wrongV4, PairedWithC).Succeeded);
        Assert.IsFalse(currentATarget.MayBusinessWrite(v2));
        var staleV2Promotion = new DirectedContinuousLifecycleCoordinator(ledger, "device-a")
            .PromoteAcquiredTargetToSource(v2State, Path.Combine(fixture.DirectoryPath, "stale-v2-authority.json"), PairedWithC);
        Assert.IsFalse(staleV2Promotion.Succeeded);
        Assert.AreEqual("stale-target-promotion", staleV2Promotion.Code);
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceAStatePath), lifecycleLedger: ledger).MayBusinessWrite("device-a"));

        var restartedBSource = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceBStatePath));
        Assert.AreEqual(DirectedAuthorityMode.Released, restartedBSource.Current.Mode);
        Assert.IsFalse(restartedBSource.MayBusinessWrite("device-b"));
        Assert.IsTrue(File.Exists(fixture.TargetBStatePath));
        _ = v1Source;
        _ = v2Source;
        _ = v3Source;
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
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Sushi81-M02-lifecycle-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            HandoffDirectory = Path.Combine(DirectoryPath, "handoff");
            LedgerPath = Path.Combine(DirectoryPath, "lifecycle.jsonl");
            SourceAStatePath = Path.Combine(DirectoryPath, "source-a.json");
            SourceBStatePath = Path.Combine(DirectoryPath, "source-b.json");
            TargetAStatePath = Path.Combine(DirectoryPath, "target-a.json");
            TargetBStatePath = Path.Combine(DirectoryPath, "target-b.json");
            TargetCStatePath = Path.Combine(DirectoryPath, "target-c.json");
        }

        public string DirectoryPath { get; }
        public string HandoffDirectory { get; }
        public string LedgerPath { get; }
        public string SourceAStatePath { get; }
        public string SourceBStatePath { get; }
        public string TargetAStatePath { get; }
        public string TargetBStatePath { get; }
        public string TargetCStatePath { get; }
        public string SnapshotPath { get; private set; } = string.Empty;
        public IArtifactSyncObserver ConfirmedObserver { get; } = new FixedSyncObserver(ArtifactSyncStatus.ConfirmedInSync);

        public async Task<DirectedTransferIdentity> CompleteSourceLegAsync(DirectedTransferIdentity transfer, string sourceStatePath)
        {
            var source = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(sourceStatePath), lifecycleLedger: new DirectedLifecycleLedgerStore(LedgerPath));
            if (File.Exists(sourceStatePath))
            {
                Assert.AreEqual(DirectedAuthorityMode.Authoritative, source.Current.Mode);
                Assert.IsTrue(source.MayBusinessWrite(transfer.SourceDeviceId));
            }
            else
            {
                Assert.IsTrue(source.InitializeAuthoritative(transfer.SourceDeviceId, PairedWithC).Succeeded);
            }
            Assert.IsTrue(source.PrepareTransfer(transfer).Succeeded);
            SnapshotPath = Path.Combine(HandoffDirectory, "directed-" + transfer.TransferId + ".snapshot.db");
            await DirectedSnapshotEvidence.CreateSyntheticAsync(SnapshotPath);
            var evidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, SnapshotPath, true);
            Assert.IsTrue((await source.DurablyRelinquishAsync(evidence)).Succeeded);
            Assert.IsTrue((await source.PublishReleaseMarkersAsync(HandoffDirectory, SnapshotPath)).Succeeded);
            return transfer;
        }

        public void Dispose()
        {
            if (!Directory.Exists(DirectoryPath)) return;
            foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(DirectoryPath, true);
        }
    }
}
