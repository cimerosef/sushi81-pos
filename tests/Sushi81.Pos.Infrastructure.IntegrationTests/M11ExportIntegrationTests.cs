using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Export;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M11ExportIntegrationTests
{
    [TestMethod]
    public async Task FreshProductionDatabaseAppliesM11LedgerMigration()
    {
        using var paths = new TestPaths();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, new FixedClock()).InitializeAsync();

        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(9L, await ScalarAsync(connection, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='export_batches';"));
        Assert.AreEqual(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='export_batch_orders';"));
        Assert.AreEqual(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='export_emissions';"));
    }

    [TestMethod]
    public async Task PopulatedSchema7To8PreservesBusinessDataWhileAddingExportLedger()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        var preM11 = PreM11Migrations();
        await new SqliteMigrationRunner(factory, preM11, clock).InitializeAsync();

        var orderId = Guid.Parse("21100000-0000-0000-0000-000000000001");
        var orderStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);
        var existing = ClosedOrder(orderId);
        await orderStore.SaveAsync(existing);

        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshots).InitializeAsync();

        Assert.AreEqual(9L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(existing.TotalTtc, (await orderStore.GetByIdAsync(orderId))!.TotalTtc);
        Assert.AreEqual(existing.Comment, (await orderStore.GetByIdAsync(orderId))!.Comment);
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='export_batch_orders';"));
    }

    [TestMethod]
    public async Task M11MigrationFailureLeavesPopulatedSchema7DatabaseIntact()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        var preM11 = PreM11Migrations();
        await new SqliteMigrationRunner(factory, preM11, clock).InitializeAsync();
        var orderId = Guid.Parse("21200000-0000-0000-0000-000000000001");
        var orderStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);
        await orderStore.SaveAsync(ClosedOrder(orderId));

        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        var failing = new SqliteMigrationRunner(
            factory,
            preM11.Concat([
                new SqliteMigration(8, "broken-m11-export-ledger", "CREATE TABLE m11_failure_marker(value TEXT NOT NULL); THIS IS INVALID SQL;")
            ]),
            clock,
            snapshots);

        await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await failing.InitializeAsync());

        Assert.AreEqual(7L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", orderId.ToString()));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='m11_failure_marker';"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='export_batches';"));
    }

    [TestMethod]
    public async Task PreparedBatchBecomesSuccessfulAndThenProtectsAgainstDuplicateCreate()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();

        var ids = new DeterministicIds();
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
        var orderId = Guid.Parse("21000000-0000-0000-0000-000000000001");
        var order = ClosedOrder(orderId);
        await orderStore.SaveLifecycleAsync(order, [new PaymentAdjustment(
            Guid.Parse("21000000-0000-0000-0000-000000000002"),
            orderId,
            PaymentBucket.Card,
            Money.FromCents(1000),
            new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero))]);

        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), ids);

        var prepared = await service.PrepareBatchAsync(new ExportSelectionOptions(), "test-version");
        Assert.IsNotNull(prepared.Batch);
        Assert.AreEqual(ExportBatchStatus.Prepared, prepared.Batch!.Status);
        Assert.HasCount(1, prepared.Selection.Actions);

        await service.MarkBatchSucceededAsync(prepared.Batch, clock.UtcNow);
        var emissions = await store.ListLatestSuccessfulEmissionsAsync();
        Assert.HasCount(1, emissions);
        Assert.AreEqual(ExportAction.Create, emissions[0].Action);
        Assert.AreEqual(new DateOnly(2026, 9, 18), emissions[0].SettlementDate);

        var afterSuccess = await service.SelectAsync(new ExportSelectionOptions());
        Assert.IsEmpty(afterSuccess.Actions);
        Assert.IsFalse(afterSuccess.IsBlocked);

        await using var verification = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(1L, await ScalarAsync(verification, "SELECT COUNT(*) FROM export_batches WHERE status='SUCCESS';"));
        Assert.AreEqual(1L, await ScalarAsync(verification, "SELECT COUNT(*) FROM export_emissions;"));
    }

    [TestMethod]
    public async Task SuccessfulCancelPreservesTheHistoricalPositiveSnapshotAndUsesZeroChildRows()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
        var orderId = Guid.Parse("21300000-0000-0000-0000-000000000001");
        var order = ClosedOrder(orderId);
        await orderStore.SaveLifecycleAsync(order, [new PaymentAdjustment(
            Guid.Parse("21300000-0000-0000-0000-000000000002"), orderId, PaymentBucket.Card,
            Money.FromCents(1000), new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero), clock.UtcNow)]);
        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), ids);

        var create = (await service.PrepareBatchAsync(new ExportSelectionOptions(), "test-version")).Batch!;
        await service.MarkBatchSucceededAsync(create, clock.UtcNow);
        var positive = (await store.ListLatestSuccessfulEmissionsAsync()).Single();

        var cancelled = order with
        {
            Status = OrderStatus.Cancelled,
            CancelledAt = clock.UtcNow,
            Comment = "cancelled after export",
            Items = [order.Items[0] with { ProductName = "Catalogue changed after export" }]
        };
        await orderStore.SaveLifecycleAsync(cancelled, []);

        var cancelPreparation = await service.PrepareBatchAsync(new ExportSelectionOptions(), "test-version");
        Assert.IsNotNull(cancelPreparation.Batch);
        Assert.HasCount(1, cancelPreparation.Selection.Actions);
        Assert.AreEqual(ExportAction.Cancel, cancelPreparation.Selection.Actions[0].Action);
        Assert.IsEmpty(cancelPreparation.Selection.Actions[0].Lines);
        Assert.IsEmpty(cancelPreparation.Selection.Actions[0].TaxBreakdown);

        await service.MarkBatchSucceededAsync(cancelPreparation.Batch!, clock.UtcNow.AddMinutes(1));
        var cancel = (await store.ListLatestSuccessfulEmissionsAsync()).Single();
        Assert.AreEqual(ExportAction.Cancel, cancel.Action);
        Assert.AreEqual(positive.PositiveSnapshotHash, cancel.PositiveSnapshotHash);
        Assert.AreEqual(positive.PositivePayload.Comment, cancel.PositivePayload.Comment);
        Assert.AreEqual("M11 produit", cancel.PositivePayload.Lines.Single().ProductName);
        Assert.AreEqual(positive.PositivePayload.FulfilmentDate, cancel.FulfilmentDate);
        Assert.AreEqual(positive.PositivePayload.SettlementDate, cancel.SettlementDate);
    }

    [TestMethod]
    public async Task TwoPreparedBatchesCannotBothFinalizeTheSameLogicalAction()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
        var orderId = Guid.Parse("21400000-0000-0000-0000-000000000001");
        var order = ClosedOrder(orderId);
        await orderStore.SaveLifecycleAsync(order, [new PaymentAdjustment(
            Guid.Parse("21400000-0000-0000-0000-000000000002"), orderId, PaymentBucket.Card,
            Money.FromCents(1000), new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero), clock.UtcNow)]);
        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), ids);

        var first = (await service.PrepareBatchAsync(new ExportSelectionOptions(), "test-version")).Batch!;
        var second = (await service.PrepareBatchAsync(new ExportSelectionOptions(), "test-version")).Batch!;
        await service.MarkBatchSucceededAsync(first, clock.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await service.MarkBatchSucceededAsync(second, clock.UtcNow.AddMinutes(1)));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_batches WHERE status='SUCCESS';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_emissions;"));
    }

    [TestMethod]
    public async Task TamperedPersistedPreparedPayloadCannotFinalize()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
        var orderId = Guid.Parse("21500000-0000-0000-0000-000000000001");
        var order = ClosedOrder(orderId);
        await orderStore.SaveLifecycleAsync(order, [new PaymentAdjustment(
            Guid.Parse("21500000-0000-0000-0000-000000000002"), orderId, PaymentBucket.Card,
            Money.FromCents(1000), new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero), clock.UtcNow)]);
        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), ids);
        var prepared = (await service.PrepareBatchAsync(new ExportSelectionOptions(), "test-version")).Batch!;

        await using (var connection = await factory.OpenLiveConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE export_batches SET payload_json=$payload WHERE batch_id=$batch;";
            command.Parameters.AddWithValue("$payload", ExportPayloadSerializer.SerializeBatch(prepared.Payload) + " ");
            command.Parameters.AddWithValue("$batch", prepared.Payload.Meta.BatchId.ToString());
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(async () => await service.MarkBatchSucceededAsync(prepared, clock.UtcNow));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED';"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_emissions;"));
    }

    [TestMethod]
    public async Task NonAuthoritativeDeviceCannotPrepareExportLedgerState()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: ids, clock: clock);
        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, ids);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var service = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), ids);

        await Assert.ThrowsAsync<WriteAuthorityException>(async () => await service.PrepareBatchAsync(new ExportSelectionOptions(), "test-version"));

        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM export_batches;"));
    }

    private static OrderSnapshot ClosedOrder(Guid id) => new(
        id,
        OrderSourceType.Pos,
        OrderStatus.Closed,
        new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 18, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 18, 8, 30, 0, TimeSpan.Zero),
        null,
        FulfilmentMode.Retrait,
        new DateOnly(2026, 9, 18),
        new TimeOnly(12, 0),
        false,
        null,
        null,
        "M11 synthetic order",
        Money.FromCents(1000),
        false,
        false,
        null,
        Money.Zero,
        [new(Guid.Parse("21000000-0000-0000-0000-000000000003"), 0, Guid.Parse("21000000-0000-0000-0000-000000000004"), "P-M11", "M11 produit", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])],
        [new(10m, Money.FromCents(1000), Money.Zero, Guid.Parse("21000000-0000-0000-0000-000000000005"))])
    {
        CardPaymentTtc = Money.FromCents(1000),
        CashPaymentTtc = Money.Zero
    };

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql, string orderId)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", orderId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static SqliteMigration[] PreM11Migrations() =>
        M01Migrations.All
            .Concat(M03Migrations.All)
            .Concat(M04Migrations.All)
            .Concat(M05Migrations.All)
            .Concat(M08Migrations.All)
            .Concat(M09Migrations.All)
            .ToArray();

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 20);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"22000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M11.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path);
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath { get; }
        public void EnsureInitialized() { }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
