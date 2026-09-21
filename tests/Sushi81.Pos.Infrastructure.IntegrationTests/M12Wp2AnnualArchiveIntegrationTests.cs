using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Archive;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Export;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M12Wp2AnnualArchiveIntegrationTests
{
    [TestMethod]
    public async Task FinalizationPublishesArchivePreservesCreateUpdateCancelAndRemovesOnlyExactTargets()
    {
        using var fixture = await Fixture.CreateAsync();
        var updateId = Guid.Parse("52000000-0000-0000-0000-000000000001");
        var cancelId = Guid.Parse("52000000-0000-0000-0000-000000000002");
        var createId = Guid.Parse("52000000-0000-0000-0000-000000000003");
        var hiboutikId = Guid.Parse("52000000-0000-0000-0000-000000000004");

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(updateId, OrderSourceType.Pos, "before update"), [Payment(updateId)]);
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(cancelId, OrderSourceType.Pos, "before cancel"), [Payment(cancelId)]);
        var exported = await fixture.ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m12-wp2-test");
        Assert.IsNotNull(exported.Batch);
        await fixture.ExportStore.MarkBatchSucceededAsync(exported.Batch!.Payload.Meta.BatchId, fixture.Clock.UtcNow);

        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(createId, OrderSourceType.Pos, "new order"), [Payment(createId)]);
        await fixture.OrderStore.SaveLifecycleAsync(
            ClosedOrder(updateId, OrderSourceType.Pos, "after update") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(1) },
            []);
        await fixture.OrderStore.SaveLifecycleAsync(
            ClosedOrder(cancelId, OrderSourceType.Pos, "before cancel") with
            {
                Status = OrderStatus.Cancelled,
                CancelledAt = new DateTimeOffset(2026, 12, 31, 19, 0, 0, TimeSpan.Zero),
                UpdatedAt = fixture.Clock.UtcNow.AddMinutes(2)
            },
            []);
        await fixture.OrderStore.SaveAsync(ClosedOrder(hiboutikId, OrderSourceType.HiboutikPaste, "Hiboutik historical order"));

        var revisionBefore = await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';");
        var result = await fixture.Service.FinalizeNextArchiveAsync();

        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, result.Outcome);
        Assert.AreEqual(2026, result.ArchiveYear);
        Assert.AreEqual(4, result.ArchivedOrderCount);
        Assert.IsTrue(File.Exists(result.CanonicalArchivePath));
        Assert.IsNotNull(result.ArchiveSha256);
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id IN ('52000000-0000-0000-0000-000000000001','52000000-0000-0000-0000-000000000002','52000000-0000-0000-0000-000000000003','52000000-0000-0000-0000-000000000004');"));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions WHERE archive_year=2026;"));
        Assert.IsGreaterThan(revisionBefore, await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';"));
        Assert.AreEqual(2, fixture.Notifier.Count);

        var prepared = await fixture.ExportStore.ListPreparedBatchesAsync();
        Assert.HasCount(1, prepared);
        CollectionAssert.AreEquivalent(
            new[] { ExportAction.Create, ExportAction.Update, ExportAction.Cancel },
            prepared.Single().Payload.Orders.Select(order => order.Action).ToArray());
        CollectionAssert.AreEquivalent(
            new[] { createId, updateId, cancelId },
            prepared.Single().Payload.Orders.Select(order => order.OrderId).ToArray());
        Assert.IsFalse(prepared.Single().Payload.Orders.Any(order => order.OrderId == hiboutikId));

        await using var archive = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(result.CanonicalArchivePath);
        Assert.AreEqual(4L, await ScalarAsync(archive, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(3L, await ScalarAsync(archive, "SELECT COUNT(*) FROM payment_adjustments;"));
        Assert.AreEqual(0L, await ScalarAsync(archive, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='annual_archive_completions';"));
        Assert.AreEqual("M12-WP1-1", await ScalarTextAsync(archive, "SELECT value FROM archive_metadata WHERE key='archive_format_version';"));
    }

    [TestMethod]
    public async Task RepeatedCallValidatesCompletionAndMissingCanonicalIsNotRecreated()
    {
        using var fixture = await Fixture.CreateAsync();
        var id = Guid.Parse("52100000-0000-0000-0000-000000000001");
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "repeatable"), [Payment(id)]);

        var first = await fixture.Service.FinalizeNextArchiveAsync();
        var revisionAfterFirst = await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';");
        var second = await fixture.Service.FinalizeNextArchiveAsync();

        Assert.AreEqual(AnnualArchiveFinalizationOutcome.AlreadyCompleted, second.Outcome);
        Assert.AreEqual(first.ArchiveSha256, second.ArchiveSha256);
        Assert.AreEqual(revisionAfterFirst, await ScalarAsync(fixture.Factory, "SELECT value FROM foundation_metadata WHERE key='business_data_revision';"));
        Assert.AreEqual(1, fixture.Notifier.Count);

        File.Delete(first.CanonicalArchivePath);
        var third = await fixture.Service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.AlreadyCompleted, third.Outcome);
        Assert.IsFalse(File.Exists(first.CanonicalArchivePath));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task CopyFailureAndPostDeleteFailureLeaveRetryableLiveState()
    {
        using var copyFixture = await Fixture.CreateAsync();
        var copyId = Guid.Parse("52200000-0000-0000-0000-000000000001");
        await copyFixture.OrderStore.SaveLifecycleAsync(ClosedOrder(copyId, OrderSourceType.Pos, "copy failure"), [Payment(copyId)]);
        var copyFailure = copyFixture.CreateService(stage => stage == "copy" ? new IOException("copy failure") : null);
        await Assert.ThrowsAsync<IOException>(() => copyFailure.FinalizeNextArchiveAsync());
        Assert.AreEqual(1L, await ScalarAsync(copyFixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await ScalarAsync(copyFixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.IsFalse(File.Exists(Path.Combine(copyFixture.Paths.ArchiveDirectory, "sushi81-archive-2026.db")));
        var copied = await copyFixture.Service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, copied.Outcome);

        using var transactionFixture = await Fixture.CreateAsync();
        var transactionId = Guid.Parse("52200000-0000-0000-0000-000000000002");
        await transactionFixture.OrderStore.SaveLifecycleAsync(ClosedOrder(transactionId, OrderSourceType.Pos, "transaction failure"), [Payment(transactionId)]);
        var transactionFailure = transactionFixture.CreateService(stage => stage == "after-delete-before-commit" ? new InvalidOperationException("rollback") : null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => transactionFailure.FinalizeNextArchiveAsync());
        Assert.AreEqual(1L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.AreEqual(1L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED';"));
        var retried = await transactionFixture.Service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.CompletedNow, retried.Outcome);
        Assert.AreEqual(0L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(1L, await ScalarAsync(transactionFixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED';"));
    }

    [TestMethod]
    public async Task CorruptCanonicalArchiveFailsClosedWithoutReplacingIt()
    {
        using var fixture = await Fixture.CreateAsync();
        var id = Guid.Parse("52300000-0000-0000-0000-000000000001");
        await fixture.OrderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "corrupt canonical"), [Payment(id)]);
        var first = await fixture.Service.FinalizeNextArchiveAsync();
        var originalBytes = File.ReadAllBytes(first.CanonicalArchivePath);
        File.WriteAllBytes(first.CanonicalArchivePath, [0x53, 0x51, 0x4c, 0x69, 0x74, 0x65]);

        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.FinalizeNextArchiveAsync());
        CollectionAssert.AreEqual(new byte[] { 0x53, 0x51, 0x4c, 0x69, 0x74, 0x65 }, File.ReadAllBytes(first.CanonicalArchivePath));
        Assert.AreNotEqual(originalBytes.Length, File.ReadAllBytes(first.CanonicalArchivePath).Length);
    }

    [TestMethod]
    public async Task JanuaryAndNonAuthoritativeRunsDoNotMutate()
    {
        using var paths = new TestPaths();
        var clock = new TestClock(new DateOnly(2027, 1, 31));
        var factory = await InitializeAsync(paths, clock);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var transactionRunner = new SqliteTransactionRunner(factory);
        var ids = new DeterministicIds();
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
        var exportStore = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
        var service = new SqliteAnnualArchiveFinalizationService(paths, clock, guard, factory, transactionRunner, exportStore, ids, new NoOpDurableChangeNotifier());

        var january = await service.FinalizeNextArchiveAsync();
        Assert.AreEqual(AnnualArchiveFinalizationOutcome.NoTargetYet, january.Outcome);
        clock.BusinessDateValue = new DateOnly(2027, 2, 1);
        await Assert.ThrowsAsync<WriteAuthorityException>(() => service.FinalizeNextArchiveAsync());
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
        Assert.IsFalse(Directory.Exists(paths.ArchiveDirectory));
    }

    [TestMethod]
    public async Task MigrationNineUpgradesWithoutResetAndBrokenUpgradeRollsBack()
    {
        using (var paths = new TestPaths())
        {
            var clock = new TestClock(new DateOnly(2027, 2, 1));
            var factory = new SqliteConnectionFactory(paths);
            var beforeM12 = ProductionMigrations.All.Where(migration => migration.Version < 9).ToArray();
            await new SqliteMigrationRunner(factory, beforeM12, clock).InitializeAsync();
            var runner = new SqliteTransactionRunner(factory);
            var orderStore = new SqliteOrderStore(factory, runner, idGenerator: new DeterministicIds(), clock: clock);
            var id = Guid.Parse("52400000-0000-0000-0000-000000000001");
            await orderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "upgrade"), [Payment(id)]);
            await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, new FixedSnapshotService()).InitializeAsync();
            Assert.AreEqual(9L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM annual_archive_completions;"));
            Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", id));
        }

        using (var paths = new TestPaths())
        {
            var clock = new TestClock(new DateOnly(2027, 2, 1));
            var factory = new SqliteConnectionFactory(paths);
            var beforeM12 = ProductionMigrations.All.Where(migration => migration.Version < 9).ToArray();
            await new SqliteMigrationRunner(factory, beforeM12, clock).InitializeAsync();
            var runner = new SqliteTransactionRunner(factory);
            var orderStore = new SqliteOrderStore(factory, runner, idGenerator: new DeterministicIds(), clock: clock);
            var id = Guid.Parse("52400000-0000-0000-0000-000000000002");
            await orderStore.SaveLifecycleAsync(ClosedOrder(id, OrderSourceType.Pos, "broken upgrade"), [Payment(id)]);
            var broken = beforeM12.Append(new SqliteMigration(9, "broken-annual-archive-completion-ledger", "CREATE TABLE annual_archive_completions (archive_year INTEGER PRIMARY KEY, broken syntax); INVALID SQL;")).ToArray();
            await Assert.ThrowsAsync<DatabaseMigrationException>(() => new SqliteMigrationRunner(factory, broken, clock, new FixedSnapshotService()).InitializeAsync());
            Assert.AreEqual(8L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='annual_archive_completions';"));
            Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", id));
        }
    }

    private static async Task<SqliteConnectionFactory> InitializeAsync(TestPaths paths, TestClock clock)
    {
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        return factory;
    }

    private static OrderSnapshot ClosedOrder(Guid id, OrderSourceType source, string comment) => new(
        id,
        source,
        OrderStatus.Closed,
        new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero),
        null,
        FulfilmentMode.Retrait,
        new DateOnly(2026, 12, 31),
        new TimeOnly(12, 0),
        false,
        null,
        null,
        comment,
        Money.FromCents(1000),
        false,
        false,
        null,
        Money.Zero,
        [new(Guid.NewGuid(), 0, Guid.NewGuid(), "P-M12", "M12 produit", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])],
        [new(10m, Money.FromCents(1000), Money.FromCents(91), Guid.NewGuid())])
    {
        Reference = $"M12-{id.ToString("N")[..8]}",
        CardPaymentTtc = Money.FromCents(1000),
        CashPaymentTtc = Money.Zero,
        SourceTotalTtc = Money.FromCents(1000)
    };

    private static PaymentAdjustment Payment(Guid orderId) => new(
        Guid.NewGuid(),
        orderId,
        PaymentBucket.Card,
        Money.FromCents(1000),
        new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 18, 1, 0, TimeSpan.Zero));

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql, Guid? id = null)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return await ScalarAsync(connection, sql, id);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, Guid? id = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed class Fixture(TestPaths paths, TestClock clock, SqliteConnectionFactory factory, WriteAuthorityGuard guard, DeterministicIds ids, SqliteOrderStore orderStore, SqliteGestionExportStore exportStore, GestionExportService exportService, RecordingNotifier notifier, SqliteAnnualArchiveFinalizationService service) : IDisposable
    {
        public TestPaths Paths { get; } = paths;
        public TestClock Clock { get; } = clock;
        public SqliteConnectionFactory Factory { get; } = factory;
        public SqliteOrderStore OrderStore { get; } = orderStore;
        public SqliteGestionExportStore ExportStore { get; } = exportStore;
        public GestionExportService ExportService { get; } = exportService;
        public RecordingNotifier Notifier { get; } = notifier;
        public SqliteAnnualArchiveFinalizationService Service { get; } = service;
        private WriteAuthorityGuard Guard { get; } = guard;
        private DeterministicIds Ids { get; } = ids;

        public static async Task<Fixture> CreateAsync()
        {
            var paths = new TestPaths();
            var clock = new TestClock(new DateOnly(2027, 2, 1));
            var factory = await InitializeAsync(paths, clock);
            var transactionRunner = new SqliteTransactionRunner(factory);
            var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var ids = new DeterministicIds();
            var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
            var exportStore = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
            var notifier = new RecordingNotifier();
            var exportService = new GestionExportService(exportStore, exportStore, clock, guard, notifier, ids);
            var service = new SqliteAnnualArchiveFinalizationService(paths, clock, guard, factory, transactionRunner, exportStore, ids, notifier);
            return new Fixture(paths, clock, factory, guard, ids, orderStore, exportStore, exportService, notifier, service);
        }

        public SqliteAnnualArchiveFinalizationService CreateService(Func<string, Exception?> injector) =>
            new(Paths, Clock, Guard, Factory, new SqliteTransactionRunner(Factory), ExportStore, Ids, Notifier, injector);

        public void Dispose()
        {
            Guard.Dispose();
            Paths.Dispose();
        }
    }

    private sealed class TestClock(DateOnly businessDate) : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2027, 2, 1, 8, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDateValue { get; set; } = businessDate;
        public DateOnly BusinessDate => BusinessDateValue;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"52500000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Count { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSnapshotService : ILocalRecoverySnapshotService
    {
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoverySnapshotResult("snapshot.db", "snapshot.json", "synthetic", DateTimeOffset.UtcNow, change.Sequence, 8));
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M12.WP2.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(path);
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string ArchiveDirectory => Path.Combine(RootDirectory, "Archive");
        public string LiveDatabasePath { get; }
        public void EnsureInitialized() { }
        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
