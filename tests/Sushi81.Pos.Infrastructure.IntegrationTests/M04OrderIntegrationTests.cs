using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M04OrderIntegrationTests
{
    [TestMethod]
    public async Task PopulatedM03DatabaseMigratesToV3WithoutChangingCatalogueRows()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, M01Migrations.All.Concat(M03Migrations.All), clock).InitializeAsync();
        var ids = new DeterministicIds();
        var catalogue = new CatalogueService(new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock));
        var category = (await catalogue.CreateCategoryAsync("Plats")).Value!;
        var product = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P1", "Plat", category.Id, Money.FromCents(1250), 10m, true, true, false, []))).Value!;
        var snapshot = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshot).InitializeAsync();
        Assert.AreEqual(3L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM categories WHERE category_id='" + category.Id + "';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM products WHERE product_id='" + product + "';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM business_settings;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task ConfirmPersistsCompleteHistoricalSnapshotAndDispatchesAfterCommit()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeAsync(paths, clock);
        var ids = new DeterministicIds();
        var runner = new SqliteTransactionRunner(factory);
        var catalogueStore = new SqliteCatalogueStore(factory, runner, ids, clock);
        var catalogue = new CatalogueService(catalogueStore);
        var category = (await catalogue.CreateCategoryAsync("Plats")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P1", "Plat initial", category.Id, Money.FromCents(1250), 10m, true, true, false, []))).Value!;
        var settings = new SqliteBusinessSettingsStore(factory, runner, clock);
        var sink = new RecordingDispatcher();
        using var orderService = new OrderEntryService(new OrderEntryCatalogueService(catalogueStore), settings, new SqliteOrderStore(factory, runner), sink, ids, clock);
        var selected = (await orderService.GetActiveProductAsync(productId))!;
        var result = await orderService.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, selected.Aggregate, [], [new(null, null, "Emballage", Money.FromCents(25))], 2, selected.CategoryName)],
            FulfilmentMode.Retrait, new DateOnly(2026, 9, 1), null, "0612345678", null, "  commande test ", false));

        Assert.IsTrue(result.Succeeded, result.Issues.Count == 0 ? null : result.Issues[0].Message);
        Assert.IsTrue(result.DispatchSucceeded);
        Assert.IsNotNull(result.CommittedOrder);
        Assert.AreEqual(result.CommittedOrder!.Id, sink.OrderId);
        Assert.IsTrue(sink.CalledAfterCommit);
        Assert.AreEqual("06 12 34 56 78", result.CommittedOrder.Telephone);
        Assert.AreEqual(2550L, result.CommittedOrder.TotalTtc.Cents);
        Assert.HasCount(1, result.CommittedOrder.Items);
        Assert.AreEqual(25L, result.CommittedOrder.Items[0].Adjustments[0].AdjustmentTtcPerUnit.Cents);
        var taxIds = result.CommittedOrder.TaxBreakdown.Select(tax => tax.Id).ToArray();

        var changed = (await catalogue.GetProductForEditAsync(productId))!;
        Assert.IsTrue((await catalogue.UpdateProductAsync(productId, changed with { Code = "P2", Name = "Produit modifié", PriceTtc = Money.FromCents(9999), VatRate = 20m, DiscountEligible = false })).Succeeded);
        Assert.IsTrue((await catalogue.DeleteProductAsync(productId)).Succeeded);
        var reopened = await orderService.GetOrderByIdAsync(result.CommittedOrder.Id);
        Assert.IsNotNull(reopened);
        Assert.AreEqual("P1", reopened!.Items[0].ProductCode);
        Assert.AreEqual("Plat initial", reopened.Items[0].ProductName);
        Assert.AreEqual(1250L, reopened.Items[0].ProductBasePriceTtc.Cents);
        Assert.AreEqual(10m, reopened.Items[0].ProductVatRate);
        Assert.AreEqual(result.CommittedOrder.TotalTtc, reopened.TotalTtc);
        CollectionAssert.AreEqual(taxIds, reopened.TaxBreakdown.Select(tax => tax.Id).ToArray());
    }

    [TestMethod]
    public async Task DispatchFailureLeavesCommittedOrderReloadableAndWriteFailureRollsBack()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeAsync(paths, clock);
        var ids = new DeterministicIds();
        var runner = new SqliteTransactionRunner(factory);
        var catalogueStore = new SqliteCatalogueStore(factory, runner, ids, clock);
        var catalogue = new CatalogueService(catalogueStore);
        var category = (await catalogue.CreateCategoryAsync("Plats")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P1", "Plat", category.Id, Money.FromCents(1250), 10m, true, true, false, []))).Value!;
        var selected = (await new OrderEntryCatalogueService(catalogueStore).GetActiveProductAsync(productId))!;
        var settings = new SqliteBusinessSettingsStore(factory, runner, clock);
        var failingSink = new RecordingDispatcher { ThrowOnDispatch = true };
        var store = new SqliteOrderStore(factory, runner);
        using var service = new OrderEntryService(new OrderEntryCatalogueService(catalogueStore), settings, store, failingSink, ids, clock);
        var result = await service.ConfirmNewOrderAsync(new NewOrderDraft([new OrderLineDraft(Guid.Empty, selected.Aggregate, [], [], 1, selected.CategoryName)], FulfilmentMode.Retrait, clock.BusinessDate, null, null, null, null, false));
        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.DispatchSucceeded);
        Assert.IsNotNull(await service.GetOrderByIdAsync(result.CommittedOrder!.Id));

        var before = await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;");
        var failingStore = new SqliteOrderStore(factory, runner, stage => stage == "adjustment" ? new InvalidOperationException("synthetic adjustment failure") : null);
        var invalidSnapshot = result.CommittedOrder with
        {
            Id = Guid.NewGuid(),
            Items = [result.CommittedOrder.Items[0] with { Id = Guid.NewGuid(), Adjustments = [new OrderLineAdjustmentSnapshot(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Fail", Money.FromCents(1), 5.5m)] }],
            TaxBreakdown = result.CommittedOrder.TaxBreakdown.Select(tax => tax with { Id = Guid.NewGuid() }).ToArray()
        };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await failingStore.SaveAsync(invalidSnapshot));
        Assert.AreEqual(before, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_item_adjustments WHERE label_snapshot='Fail';"));
    }

    private static async Task<SqliteConnectionFactory> InitializeAsync(IAppPaths paths, IBusinessClock clock)
    {
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        return factory;
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed class RecordingDispatcher : IOrderPrintDispatcher
    {
        public Guid OrderId { get; private set; }
        public bool CalledAfterCommit { get; private set; }
        public bool ThrowOnDispatch { get; init; }
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default)
        {
            OrderId = committedOrder.Id; CalledAfterCommit = committedOrder.Items.Count > 0;
            if (ThrowOnDispatch) throw new InvalidOperationException("synthetic dispatch failure");
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 8, 31);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M04.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data"); RecoveryDirectory = Path.Combine(RootDirectory, "Recovery"); CacheDirectory = Path.Combine(RootDirectory, "Cache"); LogsDirectory = Path.Combine(RootDirectory, "Logs"); ConfigDirectory = Path.Combine(RootDirectory, "Config"); TempDirectory = Path.Combine(RootDirectory, "Temp"); LiveDatabasePath = Path.Combine(DataDirectory, "live.db"); EnsureInitialized();
        }
        public string RootDirectory { get; } public string DataDirectory { get; } public string RecoveryDirectory { get; } public string CacheDirectory { get; } public string LogsDirectory { get; } public string ConfigDirectory { get; } public string TempDirectory { get; } public string LiveDatabasePath { get; }
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
