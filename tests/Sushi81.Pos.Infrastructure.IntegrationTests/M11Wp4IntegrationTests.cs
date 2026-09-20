using ClosedXML.Excel;
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
public sealed class M11Wp4IntegrationTests
{
    [TestMethod]
    public async Task ProductionBoundaryExcludesHiboutikFromGestionEvenAfterCancellationAndDateFiltering()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: new DeterministicIds(), clock: clock);
        var posId = Guid.Parse("41000000-0000-0000-0000-000000000001");
        var hiboutikId = Guid.Parse("41000000-0000-0000-0000-000000000002");
        var pos = ClosedOrder(posId, OrderSourceType.Pos, new DateOnly(2026, 9, 18));
        var hiboutik = ClosedOrder(hiboutikId, OrderSourceType.HiboutikPaste, new DateOnly(2026, 9, 19)) with
        {
            TotalTtc = Money.FromCents(3231),
            SourceTotalTtc = Money.FromCents(3590),
            CardPaymentTtc = Money.Zero,
            CashPaymentTtc = Money.Zero
        };
        await orderStore.SaveLifecycleAsync(pos, [Payment(posId, 1000, new DateOnly(2026, 9, 18))]);
        await orderStore.SaveAsync(hiboutik);

        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, new DeterministicIds());
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), new DeterministicIds());

        var posSelection = await service.SelectAsync(new ExportSelectionOptions(new DateOnly(2026, 9, 18), new DateOnly(2026, 9, 18)));
        Assert.IsFalse(posSelection.IsBlocked);
        Assert.HasCount(1, posSelection.Actions);
        Assert.AreEqual(posId, posSelection.Actions[0].OrderId);

        var hiboutikSelection = await service.SelectAsync(new ExportSelectionOptions(new DateOnly(2026, 9, 19), new DateOnly(2026, 9, 19)));
        Assert.IsFalse(hiboutikSelection.IsBlocked);
        Assert.IsEmpty(hiboutikSelection.Actions);
        Assert.IsEmpty(hiboutikSelection.Diagnostics);
        Assert.AreEqual(Money.FromCents(3590), (await store.ListExportOrderSourcesAsync()).Single(source => source.Snapshot.Id == hiboutikId).Snapshot.SourceTotalTtc);

        await orderStore.SaveAsync(hiboutik with
        {
            Status = OrderStatus.Cancelled,
            CancelledAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow.AddMinutes(1),
            Comment = "cancelled after Hiboutik paste"
        });

        var cancelledHiboutikSelection = await service.SelectAsync(new ExportSelectionOptions(new DateOnly(2026, 9, 19), new DateOnly(2026, 9, 19)));
        Assert.IsFalse(cancelledHiboutikSelection.IsBlocked);
        Assert.IsEmpty(cancelledHiboutikSelection.Actions);
        Assert.IsEmpty(cancelledHiboutikSelection.Diagnostics);
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_batches;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_emissions;"));
    }

    [TestMethod]
    public async Task RealSqliteClosedXmlRegenerationKeepsImmutableBatchAndLeavesPendingUpdateVisible()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: new DeterministicIds(), clock: clock);
        var orderId = Guid.Parse("42000000-0000-0000-0000-000000000001");
        var original = ClosedOrder(orderId, OrderSourceType.Pos, new DateOnly(2026, 9, 18));
        await orderStore.SaveLifecycleAsync(original, [Payment(orderId, 1000, new DateOnly(2026, 9, 18))]);

        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, new DeterministicIds());
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var exportService = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), new DeterministicIds());
        var workbookService = new GestionExportWorkbookService(exportService, store, new ClosedXmlGestionExportWorkbookGateway(), clock, guard);
        var root = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M11.WP4", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var firstPath = Path.Combine(root, "first.xlsx");
            var first = await workbookService.GenerateAsync(new ExportSelectionOptions(), "m11-wp4-test", firstPath);
            Assert.IsNotNull(first);
            var successful = (await store.ListSuccessfulBatchesAsync()).Single();
            var emissionsBeforeEdit = await store.ListLatestSuccessfulEmissionsAsync();
            Assert.HasCount(1, emissionsBeforeEdit);

            using (var firstWorkbook = new XLWorkbook(firstPath))
            {
                Assert.AreEqual("M11 produit", firstWorkbook.Worksheet("OrderLines").Cell(2, 5).GetString());
                Assert.AreEqual(1, firstWorkbook.Worksheet("OrderLines").LastRowUsed()!.RowNumber() - 1);
            }

            await orderStore.SaveAsync(original with
            {
                Comment = "changed after successful export",
                UpdatedAt = clock.UtcNow.AddMinutes(2),
                Items = [original.Items[0] with { ProductName = "Catalogue changed later" }]
            });

            var pendingUpdate = await exportService.SelectAsync(new ExportSelectionOptions());
            Assert.IsFalse(pendingUpdate.IsBlocked);
            Assert.HasCount(1, pendingUpdate.Actions);
            Assert.AreEqual(ExportAction.Update, pendingUpdate.Actions[0].Action);

            var regeneratedPath = Path.Combine(root, "regenerated.xlsx");
            var regenerated = await workbookService.RegenerateAsync(successful.Payload.Meta.BatchId, regeneratedPath);
            Assert.AreEqual(successful.Payload.Meta.BatchId, regenerated.BatchId);
            Assert.IsTrue(regenerated.IsRegeneration);

            using (var regeneratedWorkbook = new XLWorkbook(regeneratedPath))
            {
                Assert.AreEqual("M11 produit", regeneratedWorkbook.Worksheet("OrderLines").Cell(2, 5).GetString());
                Assert.AreEqual("M11 synthetic order", regeneratedWorkbook.Worksheet("Orders").Cell(2, 14).GetString());
            }

            Assert.HasCount(1, await store.ListSuccessfulBatchesAsync());
            var emissionsAfterRegeneration = await store.ListLatestSuccessfulEmissionsAsync();
            Assert.HasCount(1, emissionsAfterRegeneration);
            Assert.AreEqual(emissionsBeforeEdit[0].PositiveSnapshotHash, emissionsAfterRegeneration[0].PositiveSnapshotHash);
            Assert.HasCount(1, (await exportService.SelectAsync(new ExportSelectionOptions())).Actions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task RealSqlitePreparedRetryReusesExistingValidatedFileAndEmitsExactlyOnce()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: new DeterministicIds(), clock: clock);
        var orderId = Guid.Parse("43000000-0000-0000-0000-000000000001");
        await orderStore.SaveLifecycleAsync(ClosedOrder(orderId, OrderSourceType.Pos, new DateOnly(2026, 9, 18)), [Payment(orderId, 1000, new DateOnly(2026, 9, 18))]);

        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, new DeterministicIds());
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var gateway = new ClosedXmlGestionExportWorkbookGateway();
        var exportService = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), new DeterministicIds());
        var workbookService = new GestionExportWorkbookService(exportService, store, gateway, clock, guard);
        var root = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M11.WP4", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var prepared = (await exportService.PrepareBatchAsync(new ExportSelectionOptions(), "m11-wp4-test")).Batch!;
            var finalPath = Path.Combine(root, "prepared-retry.xlsx");

            await using (var output = new FileStream(finalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await gateway.WriteAsync(prepared.Payload, output);
                await output.FlushAsync();
            }
            await using (var existing = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await gateway.ValidateAsync(existing, prepared.Payload);

            var retried = await workbookService.FinalizePreparedBatchAsync(prepared.Payload.Meta.BatchId, finalPath);
            Assert.AreEqual(prepared.Payload.Meta.BatchId, retried.BatchId);
            Assert.IsFalse(retried.IsRegeneration);
            Assert.AreEqual(ExportBatchStatus.Success, (await store.GetBatchAsync(prepared.Payload.Meta.BatchId))!.Status);
            Assert.IsEmpty(await store.ListPreparedBatchesAsync());
            Assert.HasCount(1, await store.ListSuccessfulBatchesAsync());
            Assert.HasCount(1, await store.ListLatestSuccessfulEmissionsAsync());

            await Assert.ThrowsAsync<InvalidOperationException>(async () => await workbookService.FinalizePreparedBatchAsync(prepared.Payload.Meta.BatchId, finalPath));
            Assert.HasCount(1, await store.ListLatestSuccessfulEmissionsAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task ReadOnlyAuthorityCanPreviewButCannotCreateRealExportLedgerOrWorkbook()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var transactionRunner = new SqliteTransactionRunner(factory);
        var orderStore = new SqliteOrderStore(factory, transactionRunner, idGenerator: new DeterministicIds(), clock: clock);
        var orderId = Guid.Parse("44000000-0000-0000-0000-000000000001");
        await orderStore.SaveLifecycleAsync(ClosedOrder(orderId, OrderSourceType.Pos, new DateOnly(2026, 9, 18)), [Payment(orderId, 1000, new DateOnly(2026, 9, 18))]);

        var store = new SqliteGestionExportStore(factory, orderStore, transactionRunner, new DeterministicIds());
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var exportService = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), new DeterministicIds());
        var workbookService = new GestionExportWorkbookService(exportService, store, new ClosedXmlGestionExportWorkbookGateway(), clock, guard);
        var preview = await workbookService.SelectAsync(new ExportSelectionOptions());
        Assert.IsFalse(preview.IsBlocked);
        Assert.HasCount(1, preview.Actions);

        var finalPath = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M11.WP4", $"readonly-{Guid.NewGuid():N}.xlsx");
        try
        {
            await Assert.ThrowsAsync<WriteAuthorityException>(async () => await workbookService.GenerateAsync(new ExportSelectionOptions(), "m11-wp4-test", finalPath));
            Assert.IsFalse(File.Exists(finalPath));
            Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_batches;"));
            Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_emissions;"));
        }
        finally
        {
            if (File.Exists(finalPath)) File.Delete(finalPath);
        }
    }

    private static async Task<SqliteConnectionFactory> InitializeProductionAsync(TestPaths paths, IBusinessClock clock)
    {
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        return factory;
    }

    private static OrderSnapshot ClosedOrder(Guid id, OrderSourceType source, DateOnly fulfilmentDate) => new(
        id,
        source,
        OrderStatus.Closed,
        new DateTimeOffset(fulfilmentDate.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
        new DateTimeOffset(fulfilmentDate.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
        new DateTimeOffset(fulfilmentDate.ToDateTime(new TimeOnly(8, 30)), TimeSpan.Zero),
        null,
        FulfilmentMode.Retrait,
        fulfilmentDate,
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
        [new(Guid.NewGuid(), 0, Guid.NewGuid(), "P-M11", "M11 produit", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])],
        [new(10m, Money.FromCents(1000), Money.Zero, Guid.NewGuid())])
    {
        CardPaymentTtc = Money.FromCents(1000),
        CashPaymentTtc = Money.Zero
    };

    private static PaymentAdjustment Payment(Guid orderId, long cents, DateOnly effectiveDate) => new(
        Guid.NewGuid(),
        orderId,
        PaymentBucket.Card,
        Money.FromCents(cents),
        new DateTimeOffset(effectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero));

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
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
        public Guid NewId() => Guid.Parse($"45000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M11.WP4.Tests", Guid.NewGuid().ToString("N"));
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
        public string LiveDatabasePath { get; }
        public void EnsureInitialized() { }
        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
