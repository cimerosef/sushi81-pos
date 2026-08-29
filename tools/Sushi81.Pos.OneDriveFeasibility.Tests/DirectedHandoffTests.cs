using Microsoft.Data.Sqlite;

namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class DirectedHandoffTests
{
    [TestMethod]
    public void CloseAndRetainWithoutTransferPreservesAuthorityAcrossRestart()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target", "third"]).Succeeded);

        var retained = coordinator.RetainClose();
        Assert.IsTrue(retained.Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.Authoritative, coordinator.Current.Mode);
        Assert.IsNull(coordinator.Current.Transfer);
        Assert.IsNull(coordinator.Current.SnapshotEvidence);
        Assert.IsFalse(coordinator.MayBusinessWrite("source"));
        Assert.IsFalse(Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Any());

        var restarted = fixture.CreateCoordinator();
        Assert.AreEqual(DirectedAuthorityMode.Authoritative, restarted.Current.Mode);
        Assert.IsNull(restarted.Current.Transfer);
        Assert.IsFalse(restarted.MayBusinessWrite("target"));
        var reopened = restarted.ReopenRetainedAuthority();
        Assert.IsTrue(reopened.Succeeded, $"{reopened.Code}: {reopened.Message}");
        Assert.IsTrue(restarted.MayBusinessWrite("source"));
        Assert.IsFalse(Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Any());
    }

    [TestMethod]
    public void RetainCloseCancelsPreparedTransferAndCanReopenForNewTransfer()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target", "third"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);

        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var retained = coordinator.RetainClose();
        Assert.IsTrue(retained.Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.Authoritative, coordinator.Current.Mode);
        Assert.IsNull(coordinator.Current.Transfer);
        Assert.IsNull(coordinator.Current.SnapshotEvidence);
        Assert.IsNull(coordinator.Current.MarkerEvidence);
        Assert.IsTrue(coordinator.Current.ClosedWithAuthority);

        var restarted = fixture.CreateCoordinator();
        var reopened = restarted.ReopenRetainedAuthority();
        Assert.IsTrue(reopened.Succeeded, $"{reopened.Code}: {reopened.Message}");
        Assert.IsTrue(restarted.MayBusinessWrite("source"));
        Assert.IsTrue(restarted.PrepareTransfer(Fixture.Transfer("target", 1)).Succeeded);
        Assert.IsFalse(Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Any());
    }

    [TestMethod]
    public void AuthoritativeCursorRequiresExactNextHandoffVersion()
    {
        using var fixture = new Fixture();
        var lineage = Guid.NewGuid().ToString("D");
        var persisted = new DurableAuthorityState(
            DurableAuthorityState.CurrentFormatVersion,
            1,
            DirectedAuthorityMode.Authoritative,
            "source",
            ["source", "target"],
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            new DirectedLocalAuthorityCursor(lineage, 7, 1));
        new DurableAuthorityStateStore(fixture.StatePath).Save(persisted);
        new DurableLocalAuthorityCursorStore(fixture.LocalCursorPath).Save(new DurableLocalAuthorityCursorState(
            DurableLocalAuthorityCursorState.CurrentFormatVersion,
            1,
            "source",
            lineage,
            7,
            1,
            DurableLocalAuthorityRole.Authoritative,
            null,
            DateTimeOffset.UtcNow));
        var coordinator = fixture.CreateCoordinator();

        var wrong = Fixture.Transfer("target", 3) with { LineageId = lineage, Generation = 7 };
        Assert.AreEqual("non-monotonic-transfer", coordinator.PrepareTransfer(wrong).Code);

        var next = wrong with { HandoffVersion = 2 };
        Assert.IsTrue(coordinator.PrepareTransfer(next).Succeeded);
    }

    [TestMethod]
    public async Task FailedDurableRelinquishmentPreservesAuthorityAndCreatesNoMarkers()
    {
        using var fixture = new Fixture();
        var injector = new SaveFailureInjector();
        var coordinator = fixture.CreateCoordinator(injector);
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);

        injector.FailNext = true;
        var snapshot = fixture.CreateSnapshot();
        var failed = await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true));
        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("durable-write-failed", failed.Code);
        Assert.AreEqual(DirectedAuthorityMode.TransferPrepared, coordinator.Current.Mode);
        Assert.IsTrue(coordinator.AbortBeforeRelinquishment().Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.Authoritative, coordinator.Current.Mode);
        Assert.IsTrue(coordinator.MayBusinessWrite("source"));
    }

    [TestMethod]
    public async Task MarkersCannotBeCreatedBeforeDurableRelinquishment()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();

        var result = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("relinquishment-required", result.Code);
        Assert.IsFalse(Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Any());
    }

    [TestMethod]
    public async Task RelinquishmentRequiresIntegrityAndConfirmedSyncEvidence()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();

        var notSynced = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, syncConfirmed: false);
        var blockedBySync = await coordinator.DurablyRelinquishAsync(notSynced);
        Assert.IsFalse(blockedBySync.Succeeded);
        Assert.AreEqual("invalid-snapshot-evidence", blockedBySync.Code);
        Assert.AreEqual(DirectedAuthorityMode.TransferPrepared, coordinator.Current.Mode);

        File.WriteAllText(snapshot, "corrupt synthetic sqlite");
        var corrupt = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, syncConfirmed: true);
        var blockedByIntegrity = await coordinator.DurablyRelinquishAsync(corrupt);
        Assert.IsFalse(blockedByIntegrity.Succeeded);
        Assert.AreEqual("invalid-snapshot-evidence", blockedByIntegrity.Code);
        Assert.AreEqual(DirectedAuthorityMode.TransferPrepared, coordinator.Current.Mode);
        Assert.IsFalse(Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Any());
    }

    [TestMethod]
    public async Task CrashAfterDurableRelinquishmentLeavesSourceBlockedAndRetryUsesSameTransfer()
    {
        using var fixture = new Fixture();
        var injector = new MarkerFailureInjector(DirectedTransferFailurePoint.BeforeMarkerPublication);
        var coordinator = fixture.CreateCoordinator(markerFailureInjector: injector);
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target", "other"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        var relinquished = await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true));
        Assert.IsTrue(relinquished.Succeeded);
        Assert.IsFalse(coordinator.MayBusinessWrite("source"));

        var crashed = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsFalse(crashed.Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.RelinquishedBlocked, coordinator.Current.Mode);
        Assert.IsFalse(Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Any());

        var restarted = fixture.CreateCoordinator();
        Assert.IsFalse(restarted.MayBusinessWrite("source"));
        Assert.AreEqual("source-blocked", restarted.InitializeAuthoritative("source", ["source", "target", "other"]).Code);
        var retarget = restarted.PrepareTransfer(Fixture.Transfer("other", 5));
        Assert.IsFalse(retarget.Succeeded);
        Assert.AreEqual("source-blocked", retarget.Code);

        var retry = await restarted.PublishReleaseMarkersAsync(fixture.DirectoryPath, fixture.SnapshotPath);
        Assert.IsTrue(retry.Succeeded, retry.Message);
        Assert.AreEqual(DirectedAuthorityMode.Released, restarted.Current.Mode);
        Assert.AreEqual("already-released", (await restarted.PublishReleaseMarkersAsync(fixture.DirectoryPath, fixture.SnapshotPath)).Code);
    }

    [TestMethod]
    public async Task ExactTargetValidationRejectsWrongMissingCorruptAndStaleUnits()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target", "other"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        Assert.IsTrue((await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true))).Succeeded);
        Assert.IsTrue((await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot)).Succeeded);

        var valid = await DirectedTransferMarkerPublisher.ValidateAsync(fixture.DirectoryPath, snapshot, transfer, "target");
        Assert.IsTrue(valid.IsValid, valid.Message);

        var wrongTarget = await DirectedTransferMarkerPublisher.ValidateAsync(fixture.DirectoryPath, snapshot, transfer, "other");
        Assert.IsFalse(wrongTarget.IsValid);
        Assert.AreEqual("wrong-target", wrongTarget.Code);

        var readyPath = Path.Combine(fixture.DirectoryPath, "directed-" + transfer.TransferId + ".ready.json");
        var grantPath = Path.Combine(fixture.DirectoryPath, "directed-" + transfer.TransferId + ".grant.json");
        var validGrant = await File.ReadAllTextAsync(grantPath);
        File.Delete(grantPath);
        var missing = await DirectedTransferMarkerPublisher.ValidateAsync(fixture.DirectoryPath, snapshot, transfer, "target");
        Assert.IsFalse(missing.IsValid);
        Assert.AreEqual("missing-marker", missing.Code);

        await File.WriteAllTextAsync(grantPath, validGrant);
        var validReady = await File.ReadAllTextAsync(readyPath);
        await File.WriteAllTextAsync(readyPath, "{ bad json");
        var corrupt = await DirectedTransferMarkerPublisher.ValidateAsync(fixture.DirectoryPath, snapshot, transfer, "target");
        Assert.IsFalse(corrupt.IsValid);
        Assert.AreEqual("corrupt-marker", corrupt.Code);

        await File.WriteAllTextAsync(readyPath, validReady);
        var grant = await File.ReadAllTextAsync(grantPath);
        await File.WriteAllTextAsync(grantPath, grant.Replace("\"generation\": 1", "\"generation\": 999", StringComparison.Ordinal));
        var stale = await DirectedTransferMarkerPublisher.ValidateAsync(fixture.DirectoryPath, snapshot, transfer, "target");
        Assert.IsFalse(stale.IsValid);
        Assert.AreEqual("stale-or-mismatched", stale.Code);
    }

    [TestMethod]
    public async Task TargetValidationRejectsCorruptSqliteSnapshot()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        var evidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true);
        Assert.IsTrue((await coordinator.DurablyRelinquishAsync(evidence)).Succeeded);
        Assert.IsTrue((await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot)).Succeeded);

        File.WriteAllText(snapshot, "not a sqlite database");
        var result = await DirectedTransferMarkerPublisher.ValidateAsync(fixture.DirectoryPath, snapshot, transfer, "target");
        Assert.IsFalse(result.IsValid);
        Assert.AreEqual("sqlite-integrity-failure", result.Code);
    }

    [TestMethod]
    public async Task TargetValidationRejectsEveryDirectedIdentityMismatch()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target", "other"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        Assert.IsTrue((await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true))).Succeeded);
        Assert.IsTrue((await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot)).Succeeded);

        var readyPath = Path.Combine(fixture.DirectoryPath, "directed-" + transfer.TransferId + ".ready.json");
        var original = await File.ReadAllTextAsync(readyPath);
        var mutations = new[]
        {
            ($"\"lineageId\": \"{transfer.LineageId}\"", "\"lineageId\": \"00000000-0000-0000-0000-000000000001\""),
            ($"\"generation\": {transfer.Generation}", "\"generation\": 999"),
            ($"\"handoffVersion\": {transfer.HandoffVersion}", "\"handoffVersion\": 999"),
            ($"\"sourceDeviceId\": \"{transfer.SourceDeviceId}\"", "\"sourceDeviceId\": \"other-source\""),
            ($"\"targetDeviceId\": \"{transfer.TargetDeviceId}\"", "\"targetDeviceId\": \"other-target\"")
        };

        foreach (var (from, to) in mutations)
        {
            await File.WriteAllTextAsync(readyPath, original.Replace(from, to, StringComparison.Ordinal));
            var result = await DirectedTransferMarkerPublisher.ValidateAsync(fixture.DirectoryPath, snapshot, transfer, "target");
            Assert.IsFalse(result.IsValid, from);
            Assert.AreEqual("stale-or-mismatched", result.Code, from);
        }
    }

    [TestMethod]
    public void SourceTargetAndPairingValidationIsFailClosed()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);

        var sameTarget = Fixture.Transfer("source", 7);
        Assert.AreEqual("invalid-target", coordinator.PrepareTransfer(sameTarget).Code);
        Assert.AreEqual("invalid-target", coordinator.PrepareTransfer(Fixture.Transfer("unpaired", 8)).Code);
        using var missingFixture = new Fixture();
        Assert.AreEqual("state-unresolved", missingFixture.CreateCoordinator().PrepareTransfer(Fixture.Transfer("target", 9)).Code);
    }

    [TestMethod]
    public async Task RelinquishedSourceCannotAbortOrRetargetAndMarkersRemainImmutable()
    {
        using var fixture = new Fixture();
        var coordinator = fixture.CreateCoordinator();
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target", "other"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        Assert.IsTrue((await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true))).Succeeded);
        Assert.IsTrue((await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot)).Succeeded);

        Assert.AreEqual("abort-forbidden", coordinator.AbortBeforeRelinquishment().Code);
        Assert.AreEqual("source-blocked", coordinator.PrepareTransfer(Fixture.Transfer("other", 11)).Code);
        var retry = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsTrue(retry.Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.Released, coordinator.Current.Mode);
        Assert.AreEqual(2, Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Count());
    }

    [TestMethod]
    public async Task MarkerSynchronizationFailureLeavesSourceBlockedAndRetryIsIdempotent()
    {
        using var fixture = new Fixture();
        var injector = new MarkerFailureInjector(DirectedTransferFailurePoint.BeforeMarkerSynchronization);
        var coordinator = fixture.CreateCoordinator(markerFailureInjector: injector);
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        Assert.IsTrue((await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true))).Succeeded);

        var failed = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("marker-publication-failed", failed.Code);
        Assert.AreEqual(DirectedAuthorityMode.RelinquishedBlocked, coordinator.Current.Mode);

        var retry = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsTrue(retry.Succeeded, retry.Message);
        Assert.AreEqual(DirectedAuthorityMode.Released, coordinator.Current.Mode);
        Assert.AreEqual(2, Directory.EnumerateFiles(fixture.DirectoryPath, "directed-*.json").Count());
    }

    [TestMethod]
    public async Task ReadyAndGrantPublicationFailuresLeaveSourceBlocked()
    {
        foreach (var failurePoint in new[]
        {
            DirectedTransferFailurePoint.BeforeReadyMarker,
            DirectedTransferFailurePoint.BeforeGrantMarker
        })
        {
            using var fixture = new Fixture();
            var injector = new MarkerFailureInjector(failurePoint);
            var coordinator = fixture.CreateCoordinator(markerFailureInjector: injector);
            Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
            var transfer = Fixture.Transfer("target", 1);
            Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
            var snapshot = fixture.CreateSnapshot();
            Assert.IsTrue((await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true))).Succeeded);

            var failed = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
            Assert.IsFalse(failed.Succeeded);
            Assert.AreEqual("marker-publication-failed", failed.Code);
            Assert.AreEqual(DirectedAuthorityMode.RelinquishedBlocked, coordinator.Current.Mode);
            Assert.IsFalse(coordinator.MayBusinessWrite("source"));

            var retry = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
            Assert.IsTrue(retry.Succeeded, retry.Message);
            Assert.AreEqual(DirectedAuthorityMode.Released, coordinator.Current.Mode);
        }
    }

    [TestMethod]
    public async Task BothTargetMarkersMustBeConfirmedInSyncBeforeReleasedCommit()
    {
        using var fixture = new Fixture();
        var sync = new DeterministicArtifactSyncObserver();
        var coordinator = fixture.CreateCoordinator(syncObserver: sync);
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        Assert.IsTrue((await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true))).Succeeded);

        var readyPath = Path.Combine(fixture.DirectoryPath, "directed-" + transfer.TransferId + ".ready.json");
        var grantPath = Path.Combine(fixture.DirectoryPath, "directed-" + transfer.TransferId + ".grant.json");
        sync.Set(readyPath, ArtifactSyncStatus.ConfirmedInSync);
        sync.Set(grantPath, ArtifactSyncStatus.Pending);
        var pending = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsFalse(pending.Succeeded);
        Assert.AreEqual("marker-not-synchronized", pending.Code);
        Assert.AreEqual(DirectedAuthorityMode.RelinquishedBlocked, coordinator.Current.Mode);

        sync.Set(grantPath, ArtifactSyncStatus.ConfirmedInSync);
        var released = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsTrue(released.Succeeded, released.Message);
        Assert.AreEqual(DirectedAuthorityMode.Released, coordinator.Current.Mode);
    }

    [TestMethod]
    public async Task CrashBeforeReleasedDurableCommitLeavesBlockedStateAndRetryCompletes()
    {
        using var fixture = new Fixture();
        var injector = new MarkerFailureInjector(DirectedTransferFailurePoint.BeforeReleasedCommit);
        var coordinator = fixture.CreateCoordinator(markerFailureInjector: injector);
        Assert.IsTrue(coordinator.InitializeAuthoritative("source", ["source", "target"]).Succeeded);
        var transfer = Fixture.Transfer("target", 1);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = fixture.CreateSnapshot();
        Assert.IsTrue((await coordinator.DurablyRelinquishAsync(await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshot, true))).Succeeded);

        var failed = await coordinator.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.RelinquishedBlocked, coordinator.Current.Mode);
        Assert.IsFalse(coordinator.MayBusinessWrite("source"));

        var restarted = fixture.CreateCoordinator();
        var retry = await restarted.PublishReleaseMarkersAsync(fixture.DirectoryPath, snapshot);
        Assert.IsTrue(retry.Succeeded, retry.Message);
        Assert.AreEqual(DirectedAuthorityMode.Released, restarted.Current.Mode);
    }

    private sealed class SaveFailureInjector : IDurableAuthorityStateFailureInjector
    {
        public bool FailNext { get; set; }
        public void BeforeCommit(DurableAuthorityState nextState)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new IOException("synthetic durable write failure");
            }
        }
    }

    private sealed class MarkerFailureInjector(DirectedTransferFailurePoint point) : IDirectedTransferFailureInjector
    {
        private bool failed;
        public void OnFailurePoint(DirectedTransferFailurePoint observed)
        {
            if (!failed && observed == point)
            {
                failed = true;
                throw new IOException("synthetic process crash before marker publication");
            }
        }
    }

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), "Sushi81-M02-directed-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            StatePath = Path.Combine(DirectoryPath, "authority-state.json");
        }

        public string DirectoryPath { get; }
        public string StatePath { get; }
        public string LocalCursorPath => Path.Combine(DirectoryPath, "local-authority-cursor.json");
        public string SnapshotPath { get; private set; } = string.Empty;

        public DirectedHandoffCoordinator CreateCoordinator(IDurableAuthorityStateFailureInjector? stateFailureInjector = null, IDirectedTransferFailureInjector? markerFailureInjector = null, IArtifactSyncObserver? syncObserver = null) =>
            new(new DurableAuthorityStateStore(StatePath, stateFailureInjector), markerFailureInjector, syncObserver);

        public static DirectedTransferIdentity Transfer(string target, long version) =>
            new(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, version, "source", target);

        public string CreateSnapshot()
        {
            SnapshotPath = Path.Combine(DirectoryPath, "synthetic.snapshot.bin");
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = SnapshotPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE synthetic_snapshot (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO synthetic_snapshot(value) VALUES ('M02 synthetic snapshot payload');";
            command.ExecuteNonQuery();
            return SnapshotPath;
        }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
