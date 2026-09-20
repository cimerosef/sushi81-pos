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
        Assert.AreEqual(8L, await ScalarAsync(connection, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='export_batches';"));
        Assert.AreEqual(1L, await ScalarAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='export_emissions';"));
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
