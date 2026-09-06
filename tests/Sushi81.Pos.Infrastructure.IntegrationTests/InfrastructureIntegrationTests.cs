using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Ids;
using Sushi81.Pos.Infrastructure.Logging;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class InfrastructureIntegrationTests
{
    [TestMethod]
    public async Task LiveConnectionAppliesAndVerifiesRequiredPragmas()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        await using var connection = await factory.OpenLiveConnectionAsync();

        Assert.AreEqual(1L, await ScalarLongAsync(connection, "PRAGMA foreign_keys;"));
        Assert.AreEqual("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.AreEqual(2L, await ScalarLongAsync(connection, "PRAGMA synchronous;"));
        Assert.AreEqual(5000L, await ScalarLongAsync(connection, "PRAGMA busy_timeout;"));
    }

    [TestMethod]
    public async Task ConfigurationIsAtomicAndMalformedConfigurationFailsClearly()
    {
        using var paths = new TestAppPaths();
        var service = new JsonLocalConfigurationService(paths);
        await service.SaveAsync(new("zh-CN"));
        Assert.AreEqual("zh-CN", (await service.LoadAsync()).UiCulture);

        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "local-settings.json"), "{ bad json");
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await service.LoadAsync());
        StringAssert.Contains(error.Message, "malformed");
        Assert.IsFalse(File.Exists(paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task MigrationsAreOrderedIdempotentAndRejectUnknownFutureVersion()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        var migrations = new[]
        {
            new SqliteMigration(1, "create-sentinel", "CREATE TABLE sentinel (value TEXT NOT NULL); INSERT INTO sentinel(value) VALUES ('preserved');"),
            new SqliteMigration(2, "add-column", "ALTER TABLE sentinel ADD COLUMN extra TEXT NULL;")
        };
        var runner = new SqliteMigrationRunner(factory, migrations, clock);
        await runner.InitializeAsync();
        await runner.InitializeAsync();

        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            Assert.AreEqual(2L, await ScalarLongAsync(connection, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.AreEqual("preserved", await ScalarStringAsync(connection, "SELECT value FROM sentinel;"));
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version, name, applied_at_utc) VALUES (99, 'future', '2026-01-01T00:00:00.0000000+00:00');");
        }

        var futureError = await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await runner.InitializeAsync());
        StringAssert.Contains(futureError.Message, "newer");
    }

    [TestMethod]
    public async Task FailedMigrationRollsBackAndNeverReplacesSentinelDatabase()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        var initial = new SqliteMigrationRunner(factory, [new SqliteMigration(1, "create-sentinel", "CREATE TABLE sentinel (value TEXT NOT NULL); INSERT INTO sentinel(value) VALUES ('safe');")], clock);
        await initial.InitializeAsync();
        var originalLength = new FileInfo(paths.LiveDatabasePath).Length;

        var failing = new SqliteMigrationRunner(factory,
        [
            new SqliteMigration(1, "create-sentinel", "CREATE TABLE sentinel (value TEXT NOT NULL); INSERT INTO sentinel(value) VALUES ('safe');"),
            new SqliteMigration(2, "fail-after-write", "INSERT INTO sentinel(value) VALUES ('should-rollback'); THIS IS INVALID SQL;")
        ], clock);

        await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await failing.InitializeAsync());
        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sentinel;"));
        Assert.IsGreaterThanOrEqualTo(originalLength, new FileInfo(paths.LiveDatabasePath).Length);
    }

    [TestMethod]
    public async Task ExistingDatabaseUpgradeCreatesValidatedPreMigrationSnapshot()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, [new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);")], clock).InitializeAsync();
        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await new SqliteMigrationRunner(factory,
        [
            new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);"),
            new SqliteMigration(2, "second", "CREATE TABLE second_table (id INTEGER PRIMARY KEY);")
        ], clock, snapshots).InitializeAsync();

        var metadata = Directory.GetFiles(paths.RecoveryDirectory, "metadata.json", SearchOption.AllDirectories);
        Assert.HasCount(1, metadata);
        var database = Path.Combine(Path.GetDirectoryName(metadata[0])!, "snapshot.db");
        await SqliteLocalRecoverySnapshotService.VerifyAsync(database, metadata[0]);
    }

    [TestMethod]
    public async Task ExistingUpgradeWithoutSnapshotServiceBlocksBeforeSchemaChanges()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, [new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);")], clock).InitializeAsync();

        var upgrade = new SqliteMigrationRunner(factory,
        [
            new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);"),
            new SqliteMigration(2, "second", "CREATE TABLE second_table (id INTEGER PRIMARY KEY);")
        ], clock);

        await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await upgrade.InitializeAsync());
        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'second_table';"));
    }

    [TestMethod]
    public async Task ConcurrentMigrationRunnersCannotInterleaveAppliedHistory()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        SqliteMigration[] migrations = [new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);")];
        var first = new SqliteMigrationRunner(factory, migrations, new FixedClock());
        var second = new SqliteMigrationRunner(factory, migrations, new FixedClock());

        var attempts = await Task.WhenAll(RunWithoutThrowingAsync(first), RunWithoutThrowingAsync(second));
        Assert.IsTrue(attempts.Any(result => result));
        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM schema_migrations WHERE version = 1 AND name = 'first';"));
    }

    [TestMethod]
    public async Task TransactionRunnerCommitsAllRowsOrRollsEverythingBack()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        await using (var setup = await factory.OpenLiveConnectionAsync())
        {
            await ExecuteAsync(setup, "CREATE TABLE test_parent(id INTEGER PRIMARY KEY); CREATE TABLE test_child(id INTEGER PRIMARY KEY, parent_id INTEGER NOT NULL REFERENCES test_parent(id));");
        }

        var runner = new SqliteTransactionRunner(factory);
        await runner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = (SqliteApplicationTransaction)transaction;
            await ExecuteAsync(sqlite.Connection, "INSERT INTO test_parent(id) VALUES (1); INSERT INTO test_child(id, parent_id) VALUES (1, 1);", sqlite.Transaction, token);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = (SqliteApplicationTransaction)transaction;
            await ExecuteAsync(sqlite.Connection, "INSERT INTO test_parent(id) VALUES (2);", sqlite.Transaction, token);
            throw new InvalidOperationException("synthetic rollback");
        }));

        await using var verification = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(1L, await ScalarLongAsync(verification, "SELECT COUNT(*) FROM test_parent;"));
        Assert.AreEqual(1L, await ScalarLongAsync(verification, "SELECT COUNT(*) FROM test_child;"));
    }

    [TestMethod]
    public async Task SnapshotIsWalSafeValidatedAndRetainsFiveUnits()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            await ExecuteAsync(connection, "CREATE TABLE committed_values(value TEXT NOT NULL); INSERT INTO committed_values(value) VALUES ('committed');");
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, "INSERT INTO committed_values(value) VALUES ('rolled-back');", transaction);
            await transaction.RollbackAsync();
        }

        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        RecoverySnapshotResult last = null!;
        for (var sequence = 1; sequence <= 6; sequence++)
        {
            last = await snapshots.CreateAsync(new DurableChange(sequence, clock.UtcNow));
        }

        await using var snapshotConnection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(last.DatabasePath);
        Assert.AreEqual(1L, await ScalarLongAsync(snapshotConnection, "SELECT COUNT(*) FROM committed_values;"));
        Assert.HasCount(5, Directory.GetFiles(paths.RecoveryDirectory, "metadata.json", SearchOption.AllDirectories));

        await File.AppendAllTextAsync(last.DatabasePath, "tamper");
        await Assert.ThrowsAsync<RecoverySnapshotValidationException>(async () => await SqliteLocalRecoverySnapshotService.VerifyAsync(last.DatabasePath, last.MetadataPath));
    }

    [TestMethod]
    public async Task IncompleteRecoveryUnitAndStagingArtifactsAreNeverValid()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        var incompleteDirectory = Path.Combine(paths.RecoveryDirectory, "recovery-incomplete");
        Directory.CreateDirectory(incompleteDirectory);
        await File.WriteAllTextAsync(Path.Combine(incompleteDirectory, "metadata.json"), "{}");
        await Assert.ThrowsAsync<RecoverySnapshotValidationException>(async () => await SqliteLocalRecoverySnapshotService.VerifyAsync(
            Path.Combine(incompleteDirectory, "snapshot.db"),
            Path.Combine(incompleteDirectory, "metadata.json")));

        var stagingDirectory = Path.Combine(paths.TempDirectory, "recovery-staging.staging");
        Directory.CreateDirectory(stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(stagingDirectory, "snapshot.db"), "not a database");
        Assert.IsEmpty(Directory.GetDirectories(paths.RecoveryDirectory, "recovery-staging*", SearchOption.TopDirectoryOnly));
    }

    [TestMethod]
    public async Task FailedSixthSnapshotPreservesPreviousFiveValidUnits()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            await ExecuteAsync(connection, "CREATE TABLE snapshot_source(value TEXT NOT NULL);");
        }

        var snapshots = new FailOnSixthSnapshotService(paths, factory, clock);
        for (var sequence = 1; sequence <= 5; sequence++)
        {
            await snapshots.CreateAsync(new DurableChange(sequence, clock.UtcNow));
        }

        await Assert.ThrowsAsync<IOException>(async () => await snapshots.CreateAsync(new DurableChange(6, clock.UtcNow)));
        var metadata = Directory.GetFiles(paths.RecoveryDirectory, "metadata.json", SearchOption.AllDirectories);
        Assert.HasCount(5, metadata);
        foreach (var metadataPath in metadata)
        {
            await SqliteLocalRecoverySnapshotService.VerifyAsync(Path.Combine(Path.GetDirectoryName(metadataPath)!, "snapshot.db"), metadataPath);
        }
    }

    [TestMethod]
    public async Task RecoverySchedulerCoalescesAndFlushesCommittedChangesOnly()
    {
        var snapshots = new RecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        scheduler.NotifyCommitted(new DurableChange(2, DateTimeOffset.UtcNow));
        await scheduler.FlushAsync();

        Assert.HasCount(1, snapshots.Changes);
        Assert.AreEqual(2L, snapshots.Changes[0].Sequence);
    }

    [TestMethod]
    public async Task RecoverySchedulerDebouncesAndNeverRunsSnapshotsConcurrently()
    {
        var snapshots = new DelayedRecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        scheduler.NotifyCommitted(new DurableChange(2, DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromSeconds(4));

        Assert.HasCount(1, snapshots.Changes);
        Assert.AreEqual(2L, snapshots.Changes[0].Sequence);
        Assert.AreEqual(1, snapshots.MaximumConcurrentCalls);
    }

    [TestMethod]
    public async Task RecoverySchedulerFailureDoesNotClaimASnapshot()
    {
        var snapshots = new FailingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<IOException>(async () => await scheduler.FlushAsync());
        Assert.AreEqual(1, snapshots.Attempts);
    }

    [TestMethod]
    public async Task RecoverySchedulerDoesNotLoseACommitDuringAnActiveSnapshot()
    {
        var snapshots = new ActiveSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        var flush = scheduler.FlushAsync();
        await snapshots.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scheduler.NotifyCommitted(new DurableChange(2, DateTimeOffset.UtcNow));
        snapshots.Release();
        await flush;

        Assert.HasCount(2, snapshots.Changes);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, snapshots.Changes.Select(change => change.Sequence).ToArray());
    }

    [TestMethod]
    public void AuthorityAndIdsFailClosedAndGenerateOpaqueUniqueValues()
    {
        var guard = new WriteAuthorityGuard();
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
        guard.SetState(WriteAuthorityState.Authoritative);
        guard.RequireWriteAuthority();
        var ids = new GuidV7IdGenerator(TimeProvider.System);
        Assert.AreNotEqual(ids.NewId(), ids.NewId());
    }

    [TestMethod]
    public async Task AuthorityBootstrapIsDurableAndMissingStateFailsClosed()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }
        var store = new JsonAuthorityStateStore(paths);
        var legacyBootstrapEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        var firstGuard = new WriteAuthorityGuard();
        var first = await new AuthorityStateCoordinator(store, firstGuard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);

        Assert.AreEqual(WriteAuthorityState.Authoritative, first.State);
        Assert.AreEqual(WriteAuthorityState.Authoritative, firstGuard.State);
        Assert.IsTrue(await store.HasBootstrapMarkerAsync());
        Assert.IsTrue(await store.HasBootstrapAnchorAsync());

        File.Delete(Path.Combine(paths.ConfigDirectory, "authority-state.json"));
        var secondGuard = new WriteAuthorityGuard();
        var second = await new AuthorityStateCoordinator(store, secondGuard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, second.State);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, secondGuard.State);
        Assert.Throws<WriteAuthorityException>(secondGuard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task MissingAuthorityStateWithoutLegacyEvidenceFailsClosedInsteadOfRebootstrapping()
    {
        using var paths = new TestAppPaths();
        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(new JsonAuthorityStateStore(paths), guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(false);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.IsFalse(await new JsonAuthorityStateStore(paths).HasBootstrapMarkerAsync());
        Assert.IsFalse(await new JsonAuthorityStateStore(paths).HasBootstrapAnchorAsync());
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task EstablishedAuthorityWithoutIndependentAnchorFailsClosed()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var store = new JsonAuthorityStateStore(paths);
        var legacyBootstrapEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        await new AuthorityStateCoordinator(store, new WriteAuthorityGuard(), new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);
        File.Delete(Path.Combine(paths.DataDirectory, "authority-bootstrap.anchor"));

        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(store, guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.IsNotNull(result.Error);
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task EstablishedAuthorityWithoutPreExistingLiveDatabaseIsBlockedBeforeReplacementCreation()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var store = new JsonAuthorityStateStore(paths);
        var legacyEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        await new AuthorityStateCoordinator(store, new WriteAuthorityGuard(), new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance)
            .InitializeAsync(legacyEvidence);
        File.Delete(paths.LiveDatabasePath);

        await Assert.ThrowsAsync<AuthorityStartupBlockedException>(() => AuthorityStartupPreflight.CaptureAsync(paths, store));
        Assert.IsFalse(File.Exists(paths.LiveDatabasePath));

        var guard = new WriteAuthorityGuard();
        var resolution = await new AuthorityStateCoordinator(store, guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance)
            .InitializeAsync(legacyEvidence, preMigrationLiveDatabaseEvidence: false);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, resolution.State);
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task AuthorityStartupPreflightAllowsSupportedExistingLiveDatabase()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var evidence = await AuthorityStartupPreflight.CaptureAsync(paths, new JsonAuthorityStateStore(paths));

        Assert.IsTrue(evidence.HasPreExistingLiveDatabase);
        Assert.IsFalse(evidence.HasEstablishedAuthorityArtifacts);
    }

    [TestMethod]
    public async Task FreshMigratedDatabaseDoesNotQualifyAsLegacyBootstrapEvidence()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        var store = new JsonAuthorityStateStore(paths);
        var evidenceBeforeMigrations = await store.HasLegacyBootstrapEvidenceAsync();

        var clock = new FixedClock();
        await new SqliteMigrationRunner(new SqliteConnectionFactory(paths), ProductionMigrations.All, clock).InitializeAsync();
        Assert.IsTrue(await store.HasLegacyBootstrapEvidenceAsync(), "The migrated schema is not itself pre-existing legacy evidence.");

        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(store, guard, clock, NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(evidenceBeforeMigrations);

        Assert.IsFalse(evidenceBeforeMigrations);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task EstablishedAuthorityStatesRoundTripDurablyAcrossRestart()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var store = new JsonAuthorityStateStore(paths);
        var legacyBootstrapEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        await new AuthorityStateCoordinator(store, new WriteAuthorityGuard(), new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);
        foreach (var state in new[] { WriteAuthorityState.Authoritative, WriteAuthorityState.NonAuthoritativeReadOnly, WriteAuthorityState.Transitioning, WriteAuthorityState.RecoveryRequired })
        {
            await store.SaveAsync(new AuthorityStateDocument(1, state, DateTimeOffset.UtcNow));
            var guard = new WriteAuthorityGuard();
            var reloaded = await new AuthorityStateCoordinator(store, guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);
            Assert.AreEqual(state, reloaded.State);
            Assert.AreEqual(state, guard.State);
        }
    }

    [TestMethod]
    public async Task MalformedOrFutureAuthorityStateFailsClosedWithoutBootstrap()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "authority-state.json"), "{\"schemaVersion\":99,\"state\":\"Authoritative\",\"updatedAtUtc\":\"2026-08-27T12:00:00Z\"}");
        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "authority-bootstrap.marker"), "marker");

        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(new JsonAuthorityStateStore(paths), guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(false);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, guard.State);
    }

    [TestMethod]
    public async Task DurableChangeNotifierPersistsSequenceAndCoalescesRecovery()
    {
        using var paths = new TestAppPaths();
        var snapshots = new RecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        using var notifier = new DurableChangeNotifier(paths, new FixedClock(), scheduler, NullLogger<DurableChangeNotifier>.Instance);

        await notifier.NotifyCommittedAsync();
        await notifier.NotifyCommittedAsync();
        await scheduler.FlushAsync();

        Assert.HasCount(1, snapshots.Changes);
        Assert.AreEqual(2L, snapshots.Changes[0].Sequence);
        StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(paths.ConfigDirectory, "recovery-sequence.json")), "2");
    }

    [TestMethod]
    public async Task DurableChangeNotifierReconcilesCorruptSequenceWithValidatedRecoveryMetadataAfterRestart()
    {
        using var paths = new TestAppPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await snapshots.CreateAsync(new DurableChange(7, clock.UtcNow));
        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "recovery-sequence.json"), "{ not-json");

        var recording = new RecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(recording, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        using var notifier = new DurableChangeNotifier(paths, clock, scheduler, NullLogger<DurableChangeNotifier>.Instance);
        await notifier.NotifyCommittedAsync();

        StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(paths.ConfigDirectory, "recovery-sequence.json")), "8");
    }

    [TestMethod]
    public void RedactorExcludesRepresentativeSensitiveValues()
    {
        var output = SensitiveDataRedactor.Redact("phone 0612345678 email client@example.test");
        Assert.IsFalse(output.Contains("0612345678", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("client@example.test", StringComparison.Ordinal));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<bool> RunWithoutThrowingAsync(SqliteMigrationRunner runner)
    {
        try
        {
            await runner.InitializeAsync();
            return true;
        }
        catch (DatabaseMigrationException)
        {
            return false;
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        public DateOnly BusinessDate => new(2026, 8, 27);

        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingSnapshotService : ILocalRecoverySnapshotService
    {
        public List<DurableChange> Changes { get; } = [];

        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.FromResult(new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1));
        }
    }

    private sealed class DelayedRecordingSnapshotService : ILocalRecoverySnapshotService
    {
        private int activeCalls;
        private int maximumConcurrentCalls;

        public List<DurableChange> Changes { get; } = [];

        public int MaximumConcurrentCalls => maximumConcurrentCalls;

        public async Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref activeCalls);
            InterlockedExtensions.Max(ref maximumConcurrentCalls, active);
            try
            {
                await Task.Delay(50, cancellationToken);
                Changes.Add(change);
                return new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1);
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        }
    }

    private sealed class ActiveSnapshotService : ILocalRecoverySnapshotService
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<DurableChange> Changes { get; } = [];

        public async Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            Started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1);
        }

        public void Release() => release.TrySetResult(true);
    }

    private sealed class FailingSnapshotService : ILocalRecoverySnapshotService
    {
        public int Attempts { get; private set; }

        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new IOException("synthetic snapshot failure");
        }
    }

    private sealed class FailOnSixthSnapshotService(IAppPaths paths, SqliteConnectionFactory connectionFactory, IBusinessClock clock)
        : SqliteLocalRecoverySnapshotService(paths, connectionFactory, clock)
    {
        private int calls;

        protected override Task BackupToStagingAsync(string stagingDatabasePath, CancellationToken cancellationToken)
        {
            calls++;
            return calls == 6
                ? Task.FromException(new IOException("synthetic sixth snapshot failure"))
                : base.BackupToStagingAsync(stagingDatabasePath, cancellationToken);
        }
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref int location, int value)
    {
        var current = Volatile.Read(ref location);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

internal sealed class TestAppPaths : IAppPaths, IDisposable
{
    public TestAppPaths()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.Tests", Guid.NewGuid().ToString("N"));
        DataDirectory = Path.Combine(RootDirectory, "Data");
        RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
        CacheDirectory = Path.Combine(RootDirectory, "Cache");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        ConfigDirectory = Path.Combine(RootDirectory, "Config");
        TempDirectory = Path.Combine(RootDirectory, "Temp");
        LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string RecoveryDirectory { get; }
    public string CacheDirectory { get; }
    public string LogsDirectory { get; }
    public string ConfigDirectory { get; }
    public string TempDirectory { get; }
    public string LiveDatabasePath { get; }

    public void EnsureInitialized()
    {
        foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
        {
            Directory.CreateDirectory(path);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
