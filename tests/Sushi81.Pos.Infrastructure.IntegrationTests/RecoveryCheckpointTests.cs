using System.Security.Cryptography;
using System.Text.Json;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Recovery;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class RecoveryCheckpointTests
{
    private static readonly string[] ExpectedRetainedRevisions = ["3", "4", "5", "6", "7"];

    [TestMethod]
    public async Task CheckpointPublicationIsValidatedAndKeepsAuthority()
    {
        using var fixture = new CheckpointFixture();
        await fixture.SaveAuthorityAsync(4);
        await using var publisher = fixture.CreatePublisher();

        var result = await publisher.PublishAsync(4);

        result.Metadata.Validate();
        Assert.IsTrue(File.Exists(result.DatabasePath));
        Assert.IsTrue(File.Exists(result.MetadataPath));
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(1, Directory.GetDirectories(fixture.OneDriveRoot, "*", SearchOption.AllDirectories).Count(path => File.Exists(Path.Combine(path, "metadata.json"))));
    }

    [TestMethod]
    public async Task NonAuthoritativePublicationFailsClosedWithoutCreatingCheckpoint()
    {
        using var fixture = new CheckpointFixture();
        fixture.Guard.SetState(WriteAuthorityState.NonAuthoritativeReadOnly);
        await fixture.SaveAuthorityAsync(4, AuthorityPhase.NonAuthoritativeReadOnly);
        await using var publisher = fixture.CreatePublisher();

        await Assert.ThrowsAsync<WriteAuthorityException>(() => publisher.PublishAsync(4));

        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.OneDriveRoot, "DisasterRecovery")));
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task RetentionKeepsNewestFiveValidUnitsAndIgnoresCorruptUnits()
    {
        using var fixture = new CheckpointFixture();
        await using var publisher = fixture.CreatePublisher();

        for (var revision = 1; revision <= 6; revision++)
        {
            await fixture.SaveAuthorityAsync(revision);
            await publisher.PublishAsync(revision);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        var generationRoot = Path.Combine(fixture.OneDriveRoot, "DisasterRecovery", "Checkpoints", "1");
        var corrupt = Path.Combine(generationRoot, "corrupt");
        Directory.CreateDirectory(corrupt);
        await File.WriteAllTextAsync(Path.Combine(corrupt, "metadata.json"), "{}");
        var incomplete = Path.Combine(generationRoot, "incomplete");
        Directory.CreateDirectory(incomplete);

        await fixture.SaveAuthorityAsync(7);
        await publisher.PublishAsync(7);

        var validUnits = Directory.GetDirectories(generationRoot)
            .Where(path => File.Exists(Path.Combine(path, "checkpoint.db")) && File.Exists(Path.Combine(path, "metadata.json")))
            .ToArray();
        Assert.HasCount(5, validUnits);
        CollectionAssert.AreEquivalent(ExpectedRetainedRevisions, validUnits.Select(ReadBusinessRevision).OrderBy(value => value).Select(value => value.ToString(CultureInfo.InvariantCulture)).ToArray());
        Assert.IsTrue(Directory.Exists(corrupt));
        Assert.IsTrue(Directory.Exists(incomplete));
    }

    [TestMethod]
    public async Task SchedulerCoalescesChangesAndHonorsFifteenMinuteBoundaryAcrossRestart()
    {
        using var fixture = new CheckpointFixture();
        var publisher = new RecordingCheckpointPublisher(fixture.Clock);
        var watermarkPath = Path.Combine(fixture.Root, "Config", "m07-checkpoint-watermark.json");

        await using (var scheduler = new OneDriveRecoveryCheckpointScheduler(publisher, watermarkPath, fixture.Clock))
        {
            scheduler.NotifyCommitted(new DurableChange(1, fixture.Clock.UtcNow));
            Assert.IsTrue(await scheduler.TryPublishDueAsync(force: false));
            Assert.AreEqual(1L, publisher.Published.Single());

            scheduler.NotifyCommitted(new DurableChange(2, fixture.Clock.UtcNow));
            fixture.Clock.Advance(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(59));
            Assert.IsFalse(await scheduler.TryPublishDueAsync(force: false));

            scheduler.NotifyCommitted(new DurableChange(3, fixture.Clock.UtcNow));
            scheduler.NotifyCommitted(new DurableChange(4, fixture.Clock.UtcNow));
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            Assert.IsTrue(await scheduler.TryPublishDueAsync(force: false));
            CollectionAssert.AreEqual(new long[] { 1, 4 }, publisher.Published.ToArray());
        }

        await using (var restarted = new OneDriveRecoveryCheckpointScheduler(publisher, watermarkPath, fixture.Clock))
        {
            restarted.NotifyCommitted(new DurableChange(4, fixture.Clock.UtcNow));
            Assert.IsFalse(await restarted.TryPublishDueAsync(force: true));
            restarted.NotifyCommitted(new DurableChange(5, fixture.Clock.UtcNow));
            Assert.IsTrue(await restarted.TryPublishDueAsync(force: true));
        }

        CollectionAssert.AreEqual(new long[] { 1, 4, 5 }, publisher.Published.ToArray());
    }

    [TestMethod]
    public async Task SchedulerFailureRetainsPendingChangeAndLeavesAuthorityUntouched()
    {
        using var fixture = new CheckpointFixture();
        var publisher = new RecordingCheckpointPublisher(fixture.Clock) { Fail = true };
        await fixture.SaveAuthorityAsync(8);
        var watermarkPath = Path.Combine(fixture.Root, "Config", "m07-checkpoint-watermark.json");
        await using var scheduler = new OneDriveRecoveryCheckpointScheduler(publisher, watermarkPath, fixture.Clock);
        scheduler.NotifyCommitted(new DurableChange(8, fixture.Clock.UtcNow));

        Assert.IsFalse(await scheduler.TryPublishDueAsync(force: true));
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        publisher.Fail = false;
        Assert.IsTrue(await scheduler.TryPublishDueAsync(force: true));
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
    }

    [TestMethod]
    public async Task CompositeSchedulerKeepsLocalRecoveryAndCloudRevisionSpinesIndependent()
    {
        var local = new RecordingRecoveryScheduler();
        var cloud = new RecordingCloudScheduler();
        await using var scheduler = new CompositeRecoveryScheduler(local, cloud);
        var change = new DurableChange(42, new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));

        scheduler.NotifyCommitted(change);
        await scheduler.FlushAsync();

        CollectionAssert.AreEqual(new long[] { 42 }, local.Changes.Select(item => item.Sequence).ToArray());
        CollectionAssert.AreEqual(new long[] { 42 }, cloud.Published.ToArray());
    }

    private static long ReadBusinessRevision(string directory)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "metadata.json")));
        return document.RootElement.GetProperty("businessRevision").GetInt64();
    }

    private sealed class CheckpointFixture : IDisposable
    {
        public CheckpointFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Checkpoints", Guid.NewGuid().ToString("N"));
            OneDriveRoot = Path.Combine(Root, "OneDrive");
            Directory.CreateDirectory(Root);
            Clock = new MutableClock(new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero));
            Guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            Store = new JsonAuthorityStateStore(new TestPaths(Root));
            SnapshotService = new RecordingSnapshotService(Root);
        }

        public string Root { get; }
        public string OneDriveRoot { get; }
        public MutableClock Clock { get; }
        public WriteAuthorityGuard Guard { get; }
        public JsonAuthorityStateStore Store { get; }
        public RecordingSnapshotService SnapshotService { get; }
        public Guid LineageId { get; } = Guid.NewGuid();
        public Guid DeviceId { get; } = Guid.NewGuid();

        public OneDriveRecoveryCheckpointPublisher CreatePublisher() =>
            new(OneDriveRoot, Store, Guard, SnapshotService, Clock);

        public Task SaveAuthorityAsync(long revision, AuthorityPhase phase = AuthorityPhase.Authoritative) => Store.SaveAsync(
            new AuthorityStateDocument(2, phase == AuthorityPhase.Authoritative ? WriteAuthorityState.Authoritative : WriteAuthorityState.NonAuthoritativeReadOnly, Clock.UtcNow)
            {
                Protocol = new AuthorityProtocolState(checked(revision + 1), DeviceId, "Checkpoint source", LineageId, 1, 0, revision, phase)
            });

        public void Dispose()
        {
            Guard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class RecordingSnapshotService(string root) : ILocalRecoverySnapshotService
    {
        public async Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, $"source-{change.Sequence}-{Guid.NewGuid():N}.db");
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()))
            {
                await connection.OpenAsync(cancellationToken);
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision',$revision);";
                command.Parameters.AddWithValue("$revision", change.Sequence.ToString(CultureInfo.InvariantCulture));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));
            return new RecoverySnapshotResult(path, path, hash, change.CommittedAtUtc, change.Sequence, 5);
        }
    }

    private sealed class RecordingCheckpointPublisher(MutableClock clock) : IRecoveryCheckpointPublisher
    {
        public List<long> Published { get; } = [];
        public bool Fail { get; set; }

        public Task<RecoveryCheckpointPublicationResult> PublishAsync(long businessRevision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) throw new IOException("synthetic OneDrive failure");
            Published.Add(businessRevision);
            var metadata = new RecoveryCheckpointMetadata(1, "M07", Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), businessRevision, 0, clock.UtcNow, "checkpoint.db", 1, new string('A', 64));
            return Task.FromResult(new RecoveryCheckpointPublicationResult(metadata, "checkpoint.db", "metadata.json", true));
        }
    }

    private sealed class RecordingRecoveryScheduler : IRecoveryScheduler
    {
        public List<DurableChange> Changes { get; } = [];

        public void NotifyCommitted(DurableChange change) => Changes.Add(change);

        public Task FlushAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingCloudScheduler : IRecoveryCheckpointScheduler
    {
        public List<long> Published { get; } = [];
        private DurableChange? pending;

        public void NotifyCommitted(DurableChange change) => pending = change;

        public Task<bool> TryPublishDueAsync(bool force, CancellationToken cancellationToken = default)
        {
            if (pending is null) return Task.FromResult(false);
            Published.Add(pending.Sequence);
            pending = null;
            return Task.FromResult(true);
        }
    }

    private sealed class MutableClock(DateTimeOffset initial) : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; private set; } = initial;
        public DateOnly BusinessDate => DateOnly.FromDateTime(UtcNow.DateTime);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
        public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
    }

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string RootDirectory { get; } = root;
        public string DataDirectory { get; } = Path.Combine(root, "Data");
        public string RecoveryDirectory { get; } = Path.Combine(root, "Recovery");
        public string CacheDirectory { get; } = Path.Combine(root, "Cache");
        public string LogsDirectory { get; } = Path.Combine(root, "Logs");
        public string ConfigDirectory { get; } = Path.Combine(root, "Config");
        public string TempDirectory { get; } = Path.Combine(root, "Temp");
        public string LiveDatabasePath { get; } = Path.Combine(root, "Data", "live.db");
        public void EnsureInitialized() => Directory.CreateDirectory(ConfigDirectory);
    }
}
