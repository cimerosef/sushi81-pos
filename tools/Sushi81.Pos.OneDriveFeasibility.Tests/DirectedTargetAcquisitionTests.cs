namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class DirectedTargetAcquisitionTests
{
    [TestMethod]
    public async Task ValidExactTargetAcquisitionPersistsAndEnablesOnlyThatTarget()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(1);
        var target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");

        var acquired = await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);

        Assert.IsTrue(acquired.Succeeded, acquired.Message);
        Assert.AreEqual("target-acquired", acquired.Code);
        Assert.IsTrue(target.MayBusinessWrite(transfer));
        Assert.IsFalse(new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetStatePath), "third").MayBusinessWrite(transfer));
    }

    [TestMethod]
    public async Task RestartAfterDurableTargetAcquisitionReconstructsWritableState()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(2);
        var first = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsTrue((await first.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer)).Succeeded);

        var restarted = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsNotNull(restarted.Current);
        Assert.IsTrue(restarted.MayBusinessWrite(transfer));
        Assert.AreEqual("already-acquired", (await restarted.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer)).Code);
    }

    [TestMethod]
    public async Task FailedDurableTargetCommitLeavesTargetBlocked()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(3);
        var target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetStatePath, new SaveFailureInjector()), "target");
        var failed = await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);

        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("target-durable-write-failed", failed.Code);
        Assert.IsFalse(File.Exists(fixture.TargetStatePath));
        Assert.IsFalse(target.MayBusinessWrite(transfer));
    }

    [TestMethod]
    public async Task MalformedOrInvalidTargetStateIsFailClosed()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(4);
        await File.WriteAllTextAsync(fixture.TargetStatePath, "{ not valid json");
        var target = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");

        Assert.IsFalse(target.MayBusinessWrite(transfer));
        var result = await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("target-state-unresolved", result.Code);
    }

    [TestMethod]
    public async Task WrongTargetAndNonTargetReuseAreBlocked()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(5);
        var wrong = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "third");
        var result = await wrong.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("wrong-target", result.Code);
        Assert.IsFalse(wrong.MayBusinessWrite(transfer));
    }

    [TestMethod]
    public async Task StaleGenerationAndVersionCannotReuseAcquiredState()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(6);
        var target = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsTrue((await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer)).Succeeded);

        var staleGeneration = transfer with { Generation = transfer.Generation + 1 };
        var staleVersion = transfer with { HandoffVersion = transfer.HandoffVersion + 1 };
        Assert.IsFalse(target.MayBusinessWrite(staleGeneration));
        Assert.IsFalse(target.MayBusinessWrite(staleVersion));
        Assert.AreEqual("stale-target-state", (await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, staleGeneration)).Code);
    }

    [TestMethod]
    public async Task ReplayOlderAcquiredStateCannotOverwriteNewerDurableState()
    {
        using var fixture = new Fixture();
        var newer = Fixture.Transfer("target", 9, generation: 2);
        var existing = new DurableTargetAcquisitionState(
            1, 1, "target", newer.LineageId, newer.Generation, newer.HandoffVersion,
            newer.SourceDeviceId, newer.TargetDeviceId, newer.TransferId,
            Path.Combine(fixture.DirectoryPath, "synthetic-replay.snapshot.db"),
            new string('A', 64), 100, DurableTargetAcquisitionStatus.Acquired, DateTimeOffset.UtcNow);
        new DurableTargetAcquisitionStore(fixture.TargetStatePath).Save(existing);

        var older = Fixture.Transfer("target", 8, generation: 1);
        var target = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        var replay = await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, older);

        Assert.IsFalse(replay.Succeeded);
        Assert.AreEqual("stale-target-state", replay.Code);
        Assert.AreEqual(newer.TransferId, target.Current!.TransferId);
    }

    [TestMethod]
    public async Task SourceDeviceCannotLoadTargetAcquisitionAsWritable()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(10);
        var target = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsTrue((await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer)).Succeeded);

        var source = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "source");
        Assert.IsFalse(source.MayBusinessWrite(transfer));
    }

    [TestMethod]
    public async Task TrueVirginTargetEstablishesItsFirstLocalCursor()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(1);
        Assert.IsFalse(File.Exists(fixture.LocalCursorPath));

        var target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetStatePath),
            "target");
        var acquired = await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);

        Assert.IsTrue(acquired.Succeeded, acquired.Message);
        Assert.IsTrue(File.Exists(fixture.LocalCursorPath));
        var cursor = new DurableLocalAuthorityCursorStore(fixture.LocalCursorPath).Load();
        Assert.AreEqual(DurableLocalAuthorityRole.AcquiredTarget, cursor.CurrentRole);
        Assert.AreEqual(1, cursor.HighWaterHandoffVersion);
        Assert.IsTrue(target.MayBusinessWrite(transfer));
    }

    [TestMethod]
    public async Task CrashAfterTargetEvidenceBeforeCursorCommitStaysBlockedAndExactRetryCompletes()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(1);
        var target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetStatePath),
            "target",
            localCursorStore: new DurableLocalAuthorityCursorStore(fixture.LocalCursorPath, new CursorFailureInjector()));

        var failed = await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);

        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("local-authority-cursor-failed", failed.Code);
        Assert.IsTrue(File.Exists(fixture.TargetStatePath));
        Assert.AreEqual(DurableLocalAuthorityRole.AcquisitionPending, new DurableLocalAuthorityCursorStore(fixture.LocalCursorPath).Load().CurrentRole);
        Assert.IsFalse(target.MayBusinessWrite(transfer));

        var restarted = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetStatePath),
            "target");
        var retried = await restarted.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);
        Assert.IsTrue(retried.Succeeded, retried.Message);
        Assert.IsTrue(restarted.MayBusinessWrite(transfer));
    }

    [TestMethod]
    public async Task MalformedCurrentLocalCursorFailsClosed()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(1);
        var target = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsTrue((await target.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer)).Succeeded);
        await File.WriteAllTextAsync(fixture.LocalCursorPath, "{ malformed cursor");

        Assert.IsFalse(target.MayBusinessWrite(transfer));
        var restarted = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsFalse(restarted.MayBusinessWrite(transfer));
    }

    [TestMethod]
    public async Task MissingCursorCannotTreatRetainedTargetHistoryAsVirgin()
    {
        using var fixture = new Fixture();
        var transfer = await fixture.CreateReleasedTransferAsync(1);
        var first = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsTrue((await first.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer)).Succeeded);

        var historyPath = Path.Combine(Path.GetDirectoryName(fixture.TargetStatePath)!, "target-history.json");
        File.Move(fixture.TargetStatePath, historyPath);
        File.Delete(fixture.LocalCursorPath);

        var restarted = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(fixture.TargetStatePath), "target");
        Assert.IsFalse(restarted.MayBusinessWrite(transfer));
        var retry = await restarted.AcquireAsync(fixture.DirectoryPath, fixture.SnapshotPath, transfer);
        Assert.IsFalse(retry.Succeeded);
        Assert.AreEqual("local-authority-unresolved", retry.Code);
    }

    [TestMethod]
    public async Task MalformedLocalAuthorityCursorFailsClosedWithoutSharedLedger()
    {
        using var fixture = new Fixture();
        var transfer = Fixture.Transfer("target", 1);
        var targetState = new DurableTargetAcquisitionState(
            DurableTargetAcquisitionState.CurrentFormatVersion,
            1,
            "target",
            transfer.LineageId,
            transfer.Generation,
            transfer.HandoffVersion,
            transfer.SourceDeviceId,
            transfer.TargetDeviceId,
            transfer.TransferId,
            Path.Combine(fixture.DirectoryPath, "synthetic.snapshot.db"),
            new string('A', 64),
            100,
            DurableTargetAcquisitionStatus.Acquired,
            DateTimeOffset.UtcNow);
        new DurableTargetAcquisitionStore(fixture.TargetStatePath).Save(targetState);
        await File.WriteAllTextAsync(fixture.SourceStatePath, "{ malformed local cursor");

        var target = new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(fixture.TargetStatePath),
            "target",
            ["source", "target"],
            sourceStateStore: new DurableAuthorityStateStore(fixture.SourceStatePath));

        Assert.IsFalse(target.MayBusinessWrite(transfer));
    }

    private sealed class SaveFailureInjector : IDurableTargetAcquisitionFailureInjector
    {
        public void BeforeCommit(DurableTargetAcquisitionState nextState) => throw new IOException("synthetic target durable write failure");
    }

    private sealed class CursorFailureInjector : IDurableLocalAuthorityCursorFailureInjector
    {
        public void BeforeCommit(DurableLocalAuthorityCursorState nextState)
        {
            if (nextState.CurrentRole == DurableLocalAuthorityRole.AcquiredTarget)
            {
                throw new IOException("synthetic local cursor durable write failure");
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Sushi81-M02-target-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(DirectoryPath);
            var sourceDirectory = Path.Combine(DirectoryPath, "source-device");
            var targetDirectory = Path.Combine(DirectoryPath, "target-device");
            System.IO.Directory.CreateDirectory(sourceDirectory);
            System.IO.Directory.CreateDirectory(targetDirectory);
            TargetStatePath = Path.Combine(targetDirectory, "target-acquisition.json");
            SourceStatePath = Path.Combine(sourceDirectory, "source-authority.json");
            LocalCursorPath = Path.Combine(targetDirectory, "local-authority-cursor.json");
        }

        public string DirectoryPath { get; }
        public string TargetStatePath { get; }
        public string SourceStatePath { get; }
        public string LocalCursorPath { get; }
        public string SnapshotPath { get; private set; } = string.Empty;

        public async Task<DirectedTransferIdentity> CreateReleasedTransferAsync(long version)
        {
            var source = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(SourceStatePath));
            var transfer = Transfer("target", 1);
            Assert.IsTrue(source.InitializeAuthoritative("source", ["source", "target", "third"]).Succeeded);
            Assert.IsTrue(source.PrepareTransfer(transfer).Succeeded);
            SnapshotPath = Path.Combine(DirectoryPath, "synthetic-" + version + ".snapshot.db");
            await DirectedSnapshotEvidence.CreateSyntheticAsync(SnapshotPath);
            var evidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, SnapshotPath, true);
            Assert.IsTrue((await source.DurablyRelinquishAsync(evidence)).Succeeded);
            Assert.IsTrue((await source.PublishReleaseMarkersAsync(DirectoryPath, SnapshotPath)).Succeeded);
            return transfer;
        }

        public static DirectedTransferIdentity Transfer(string target, long version, long generation = 1) =>
            new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), generation, version, "source", target);

        public void Dispose()
        {
            if (!System.IO.Directory.Exists(DirectoryPath)) return;
            foreach (var file in System.IO.Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            System.IO.Directory.Delete(DirectoryPath, true);
        }
    }
}
