namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class ContinuousAuthorityLifecycleTests
{
    private static readonly string[] Paired = ["device-a", "device-b"];
    private static readonly string[] ExpectedSources = ["device-a", "device-b"];
    private static readonly string[] ExpectedTargets = ["device-b", "device-a"];

    [TestMethod]
    public async Task SameLineageGenerationSupportsABv1ThenBAv2WithoutDeletingDurableState()
    {
        using var fixture = new Fixture();
        var lineage = Guid.NewGuid().ToString("D");
        var lifecycle = new DirectedContinuousLifecycleCoordinator(new DirectedLifecycleLedgerStore(fixture.LedgerPath), "device-a");
        var v1 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 1, "device-a", "device-b");

        var v1Source = await fixture.CompleteSourceLegAsync(v1, fixture.SourceAStatePath);
        var v1TargetBeforeRestart = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetBStatePath), "device-b", Paired, fixture.ConfirmedObserver);
        Assert.IsFalse(v1TargetBeforeRestart.MayBusinessWrite(v1));
        var v1Acquired = await v1TargetBeforeRestart.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v1);
        Assert.IsTrue(v1Acquired.Succeeded, v1Acquired.Message);
        Assert.HasCount(3, v1Acquired.SyncObservations!);
        Assert.IsTrue(v1TargetBeforeRestart.MayBusinessWrite(v1));
        var targetStateBytes = await File.ReadAllBytesAsync(fixture.TargetBStatePath);
        Assert.IsTrue(lifecycle.RecordCompletedTransfer(new DurableAuthorityStateStore(fixture.SourceAStatePath), v1TargetBeforeRestart.Current!).Succeeded);

        var promote = new DirectedContinuousLifecycleCoordinator(new DirectedLifecycleLedgerStore(fixture.LedgerPath), "device-b")
            .PromoteAcquiredTargetToSource(v1TargetBeforeRestart.Current!, fixture.SourceBStatePath, Paired);
        Assert.IsTrue(promote.Succeeded, promote.Message);
        CollectionAssert.AreEqual(targetStateBytes, await File.ReadAllBytesAsync(fixture.TargetBStatePath));

        var v2 = new DirectedTransferIdentity(Guid.NewGuid().ToString("D"), lineage, 7, 2, "device-b", "device-a");
        var bLifecycle = new DirectedContinuousLifecycleCoordinator(new DirectedLifecycleLedgerStore(fixture.LedgerPath), "device-b");
        Assert.IsTrue(bLifecycle.ValidateNextTransfer(v2, Paired).Succeeded);
        var v2Source = await fixture.CompleteSourceLegAsync(v2, fixture.SourceBStatePath);
        var v2Target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetAStatePath), "device-a", Paired, fixture.ConfirmedObserver);
        var v2Acquired = await v2Target.AcquireAsync(fixture.HandoffDirectory, fixture.SnapshotPath, v2);
        Assert.IsTrue(v2Acquired.Succeeded, v2Acquired.Message);
        Assert.IsTrue(v2Target.MayBusinessWrite(v2));
        Assert.IsTrue(bLifecycle.RecordCompletedTransfer(new DurableAuthorityStateStore(fixture.SourceBStatePath), v2Target.Current!).Succeeded);

        var entries = new DirectedLifecycleLedgerStore(fixture.LedgerPath).Load();
        CollectionAssert.AreEqual(new long[] { 1, 2 }, entries.Select(entry => entry.HandoffVersion).ToArray());
        CollectionAssert.AreEqual(ExpectedSources, entries.Select(entry => entry.SourceDeviceId).ToArray());
        CollectionAssert.AreEqual(ExpectedTargets, entries.Select(entry => entry.TargetDeviceId).ToArray());
        var replayV1 = v1 with { TransferId = Guid.NewGuid().ToString("D"), SourceDeviceId = "device-b", TargetDeviceId = "device-a" };
        Assert.AreEqual("stale-or-replayed-version", bLifecycle.ValidateNextTransfer(replayV1, Paired).Code);

        var restartedBSource = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(fixture.SourceBStatePath));
        Assert.AreEqual(DirectedAuthorityMode.Released, restartedBSource.Current.Mode);
        Assert.IsFalse(restartedBSource.MayBusinessWrite("device-b"));
        var restartedATarget = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetAStatePath), "device-a", Paired);
        Assert.IsTrue(restartedATarget.MayBusinessWrite(v2));
        Assert.IsTrue(File.Exists(fixture.TargetBStatePath));
        _ = v1Source;
        _ = v2Source;
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
        }

        public string DirectoryPath { get; }
        public string HandoffDirectory { get; }
        public string LedgerPath { get; }
        public string SourceAStatePath { get; }
        public string SourceBStatePath { get; }
        public string TargetAStatePath { get; }
        public string TargetBStatePath { get; }
        public string SnapshotPath { get; private set; } = string.Empty;
        public IArtifactSyncObserver ConfirmedObserver { get; } = new FixedSyncObserver(ArtifactSyncStatus.ConfirmedInSync);

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
                Assert.IsTrue(source.InitializeAuthoritative(transfer.SourceDeviceId, Paired).Succeeded);
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
