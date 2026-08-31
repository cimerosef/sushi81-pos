using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
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
    private static readonly decimal[] MixedTaxRates = [5.5m, 10m];

    [TestMethod]
    public async Task FailedSchemaUpgradeLeavesLiveDatabaseAtPreviousVersion()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, M01Migrations.All, clock).InitializeAsync();
        await using (var connection = await factory.OpenLiveConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO foundation_metadata(key,value) VALUES ('sentinel','kept');";
            await command.ExecuteNonQueryAsync();
        }

        var broken = M01Migrations.All.Concat([new SqliteMigration(2, "broken-upgrade", "CREATE TABLE foundation_metadata (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);")]).ToArray();
        var snapshots = new RecordingSnapshotService();
        var runner = new SqliteMigrationRunner(factory, broken, clock, snapshots);
        await Assert.ThrowsAsync<DatabaseMigrationException>(() => runner.InitializeAsync());

        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM foundation_metadata WHERE key='sentinel' AND value='kept';"));
        Assert.HasCount(1, snapshots.Changes);
    }

    [TestMethod]
    public async Task PopulatedV2FailedV3PreservesM03DataAndRealV3RetrySucceeds()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, M01Migrations.All.Concat(M03Migrations.All), clock).InitializeAsync();
        var ids = new DeterministicIds();
        var catalogue = new CatalogueService(new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock));
        var category = (await catalogue.CreateCategoryAsync("Plats v2")).Value!;
        var product = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "V2-P1", "Produit v2", category.Id, Money.FromCents(1250), 10m, true, true, false, []))).Value!;
        var snapshots = new RecordingSnapshotService();
        var broken = M01Migrations.All.Concat(M03Migrations.All).Concat([
            new SqliteMigration(3, "broken-v3", "CREATE TABLE products (product_id TEXT NOT NULL PRIMARY KEY);")]).ToArray();

        await Assert.ThrowsAsync<DatabaseMigrationException>(() => new SqliteMigrationRunner(factory, broken, clock, snapshots).InitializeAsync());
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM categories WHERE category_id='" + category.Id + "' AND name='Plats v2';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM products WHERE product_id='" + product + "' AND code='V2-P1';"));

        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshots).InitializeAsync();
        Assert.AreEqual(3L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM categories WHERE category_id='" + category.Id + "' AND name='Plats v2';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM products WHERE product_id='" + product + "' AND code='V2-P1';"));
        Assert.IsTrue(snapshots.Changes.Any(change => change.Sequence == 2));
    }

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
        var optionId = Guid.Empty;
        var groupId = Guid.Empty;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P1", "Plat initial", category.Id, Money.FromCents(1250), 10m, true, true, true,
            [new OptionGroupDraft(groupId, "Options initiales", SelectionMode.Single, false, null, null, 0,
                [new OptionDraft(optionId, "Sauce initiale", Money.FromCents(25), true, 0)])]))).Value!;
        var settings = new SqliteBusinessSettingsStore(factory, runner, clock);
        var orderStore = new SqliteOrderStore(factory, runner);
        var sink = new RecordingDispatcher(factory, runner);
        using var orderService = new OrderEntryService(new OrderEntryCatalogueService(catalogueStore), settings, orderStore, sink, ids, clock);
        var selected = (await orderService.GetActiveProductAsync(productId))!;
        optionId = selected.Aggregate.OptionsByGroup.Values.Single().Single().Id;
        var result = await orderService.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, selected.Aggregate, [optionId], [new(null, null, "Emballage", Money.FromCents(25))], 2, selected.CategoryName)],
            FulfilmentMode.Retrait, new DateOnly(2026, 9, 1), new TimeOnly(11, 0), "0612345678", null, "  commande test ", false));

        Assert.IsTrue(result.Succeeded, result.Issues.Count == 0 ? null : result.Issues[0].Message);
        Assert.IsTrue(result.DispatchSucceeded);
        Assert.IsNotNull(result.CommittedOrder);
        Assert.AreEqual(result.CommittedOrder!.Id, sink.OrderId);
        Assert.IsTrue(sink.CalledAfterCommit);
        Assert.AreEqual("06 12 34 56 78", result.CommittedOrder.Telephone);
        Assert.AreEqual(2600L, result.CommittedOrder.TotalTtc.Cents);
        Assert.HasCount(1, result.CommittedOrder.Items);
        Assert.AreEqual(25L, result.CommittedOrder.Items[0].Adjustments[0].AdjustmentTtcPerUnit.Cents);
        CollectionAssert.AreEquivalent(MixedTaxRates, result.CommittedOrder.TaxBreakdown.Select(tax => tax.VatRate).ToArray());
        var taxIds = result.CommittedOrder.TaxBreakdown.Select(tax => tax.Id).ToArray();

        var original = result.CommittedOrder;
        var changed = (await catalogue.GetProductForEditAsync(productId))!;
        Assert.IsTrue((await catalogue.UpdateProductAsync(productId, changed with
        {
            Code = "P2", Name = "Produit modifié", PriceTtc = Money.FromCents(9999), VatRate = 20m, DiscountEligible = false,
            Groups = [changed.Groups[0] with { Name = "Options modifiées", Options = [changed.Groups[0].Options[0] with { Name = "Sauce modifiée", PriceAdjustmentTtc = Money.FromCents(999), IsActive = false }] }]
        })).Succeeded);
        Assert.IsTrue((await catalogue.RenameCategoryAsync(category.Id, "Desserts")).Succeeded);
        Assert.IsTrue((await catalogue.SetProductActiveAsync(productId, false)).Succeeded);
        Assert.IsTrue((await catalogue.DeleteProductAsync(productId)).Succeeded);
        var reopened = await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(result.CommittedOrder.Id);
        Assert.IsNotNull(reopened);
        Assert.AreEqual("P1", reopened!.Items[0].ProductCode);
        Assert.AreEqual("Plat initial", reopened.Items[0].ProductName);
        Assert.AreEqual("Plats", reopened.Items[0].CategoryName);
        Assert.AreEqual(1250L, reopened.Items[0].ProductBasePriceTtc.Cents);
        Assert.AreEqual(10m, reopened.Items[0].ProductVatRate);
        Assert.IsTrue(reopened.Items[0].ProductDiscountEligible);
        Assert.AreEqual(original.Items[0].Quantity, reopened.Items[0].Quantity);
        Assert.AreEqual(original.Items[0].ExtendedBaseTtc, reopened.Items[0].ExtendedBaseTtc);
        Assert.AreEqual(original.Items[0].CalculatedLineTotalTtc, reopened.Items[0].CalculatedLineTotalTtc);
        CollectionAssert.AreEqual(original.Items[0].Adjustments.ToArray(), reopened.Items[0].Adjustments.ToArray());
        Assert.AreEqual(original.TotalTtc, reopened.TotalTtc);
        CollectionAssert.AreEqual(original.TaxBreakdown.ToArray(), reopened.TaxBreakdown.ToArray());
        CollectionAssert.AreEqual(taxIds, reopened.TaxBreakdown.Select(tax => tax.Id).ToArray());
    }

    [TestMethod]
    public async Task ManualTenPercentTaxBucketSurvivesSaveAndReload()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeAsync(paths, clock);
        var ids = new DeterministicIds();
        var runner = new SqliteTransactionRunner(factory);
        var catalogueStore = new SqliteCatalogueStore(factory, runner, ids, clock);
        var catalogue = new CatalogueService(catalogueStore);
        var category = (await catalogue.CreateCategoryAsync("Plats")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P1", "Plat", category.Id, Money.FromCents(1000), 10m, true, true, false, []))).Value!;
        var selected = (await new OrderEntryCatalogueService(catalogueStore).GetActiveProductAsync(productId))!;
        var settings = new SqliteBusinessSettingsStore(factory, runner, clock);
        var store = new SqliteOrderStore(factory, runner);
        using var service = new OrderEntryService(new OrderEntryCatalogueService(catalogueStore), settings, store, new RecordingDispatcher(factory, runner), ids, clock);

        var result = await service.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, selected.Aggregate, [], [], 1, selected.CategoryName)],
            FulfilmentMode.Retrait, clock.BusinessDate, new TimeOnly(11, 0), null, null, null, false, Money.FromCents(1234)));

        Assert.IsTrue(result.Succeeded, result.Issues.Count == 0 ? null : result.Issues[0].Message);
        Assert.IsNotNull(result.CommittedOrder);
        Assert.IsTrue(result.CommittedOrder!.ManualTotalOverrideActive);
        Assert.HasCount(1, result.CommittedOrder.TaxBreakdown);
        Assert.AreEqual(10m, result.CommittedOrder.TaxBreakdown[0].VatRate);
        Assert.AreEqual(1234L, result.CommittedOrder.TaxBreakdown[0].TaxableTtc.Cents);

        var reopened = await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(result.CommittedOrder.Id);
        Assert.IsNotNull(reopened);
        Assert.IsTrue(reopened!.ManualTotalOverrideActive);
        Assert.AreEqual(1234L, reopened.TotalTtc.Cents);
        Assert.HasCount(1, reopened.TaxBreakdown);
        Assert.AreEqual(10m, reopened.TaxBreakdown[0].VatRate);
        Assert.AreEqual(1234L, reopened.TaxBreakdown[0].TaxableTtc.Cents);
        Assert.AreEqual(result.CommittedOrder.TaxBreakdown[0].IncludedVatTtc.Cents, reopened.TaxBreakdown[0].IncludedVatTtc.Cents);
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
        var failingSink = new RecordingDispatcher(factory, runner) { ThrowOnDispatch = true };
        var store = new SqliteOrderStore(factory, runner);
        using var service = new OrderEntryService(new OrderEntryCatalogueService(catalogueStore), settings, store, failingSink, ids, clock);
        var result = await service.ConfirmNewOrderAsync(new NewOrderDraft([new OrderLineDraft(Guid.Empty, selected.Aggregate, [], [new(null, null, "Emballage", Money.FromCents(1))], 1, selected.CategoryName)], FulfilmentMode.Retrait, clock.BusinessDate, new TimeOnly(11, 0), null, null, null, false));
        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.DispatchSucceeded);
        Assert.IsNotNull(await service.GetOrderByIdAsync(result.CommittedOrder!.Id));
        var restarted = await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(result.CommittedOrder.Id);
        Assert.IsNotNull(restarted);
        Assert.AreEqual(result.CommittedOrder.Id, restarted!.Id);
        Assert.AreEqual(result.CommittedOrder.TotalTtc, restarted.TotalTtc);
        Assert.HasCount(result.CommittedOrder.Items.Count, restarted.Items);
        Assert.HasCount(result.CommittedOrder.TaxBreakdown.Count, restarted.TaxBreakdown);
        Assert.AreEqual(result.CommittedOrder.Items[0].CalculatedLineTotalTtc, restarted.Items[0].CalculatedLineTotalTtc);

        var beforeOrders = await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;");
        var beforeItems = await ScalarAsync(factory, "SELECT COUNT(*) FROM order_items;");
        var beforeAdjustments = await ScalarAsync(factory, "SELECT COUNT(*) FROM order_item_adjustments;");
        var beforeTaxes = await ScalarAsync(factory, "SELECT COUNT(*) FROM order_tax_breakdown;");
        foreach (var stage in new[] { "order", "item", "adjustment", "tax" })
        {
            var failingStore = new SqliteOrderStore(factory, runner, current => current == stage ? new InvalidOperationException($"synthetic {stage} failure") : null);
            var invalidSnapshot = result.CommittedOrder with
            {
                Id = Guid.NewGuid(),
                Items = result.CommittedOrder.Items.Select(item => item with
                {
                    Id = Guid.NewGuid(),
                    Adjustments = item.Adjustments.Select(adjustment => adjustment with { Id = Guid.NewGuid() }).ToArray()
                }).ToArray(),
                TaxBreakdown = result.CommittedOrder.TaxBreakdown.Select(tax => tax with { Id = Guid.NewGuid() }).ToArray()
            };
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await failingStore.SaveAsync(invalidSnapshot));
            Assert.AreEqual(beforeOrders, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"), stage);
            Assert.AreEqual(beforeItems, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_items;"), stage);
            Assert.AreEqual(beforeAdjustments, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_item_adjustments;"), stage);
            Assert.AreEqual(beforeTaxes, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_tax_breakdown;"), stage);
        }
    }

    [TestMethod]
    public async Task TransactionCommitFailureRollsBackTheWholeOrderAndNeverDispatches()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeAsync(paths, clock);
        var ids = new DeterministicIds();
        var catalogueStore = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock);
        var catalogue = new CatalogueService(catalogueStore);
        var category = (await catalogue.CreateCategoryAsync("Plats")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P1", "Plat", category.Id, Money.FromCents(1250), 10m, true, true, false, []))).Value!;
        var selected = (await new OrderEntryCatalogueService(catalogueStore).GetActiveProductAsync(productId))!;
        var commitFailingRunner = new SqliteTransactionRunner(factory, () => new InvalidOperationException("synthetic commit failure"));
        var dispatcher = new RecordingDispatcher(factory, new SqliteTransactionRunner(factory));
        using var service = new OrderEntryService(new OrderEntryCatalogueService(catalogueStore), new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), clock), new SqliteOrderStore(factory, commitFailingRunner), dispatcher, ids, clock);

        var result = await service.ConfirmNewOrderAsync(new NewOrderDraft([new OrderLineDraft(Guid.Empty, selected.Aggregate, [], [new(null, null, "Emballage", Money.FromCents(25))], 1, selected.CategoryName)], FulfilmentMode.Retrait, clock.BusinessDate, new TimeOnly(11, 0), null, null, null, false));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, dispatcher.Calls);
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_items;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_item_adjustments;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_tax_breakdown;"));
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

    private sealed class RecordingDispatcher(SqliteConnectionFactory factory, ITransactionRunner runner) : IOrderPrintDispatcher
    {
        private readonly SqliteConnectionFactory factory = factory;
        private readonly ITransactionRunner runner = runner;
        public int Calls { get; private set; }
        public Guid OrderId { get; private set; }
        public bool CalledAfterCommit { get; private set; }
        public bool ThrowOnDispatch { get; init; }
        public async Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default)
        {
            Calls++;
            OrderId = committedOrder.Id;
            var separateConnectionSnapshot = await new SqliteOrderStore(factory, runner).GetByIdAsync(committedOrder.Id, cancellationToken);
            CalledAfterCommit = separateConnectionSnapshot is not null && separateConnectionSnapshot.Items.Count > 0;
            if (ThrowOnDispatch) throw new InvalidOperationException("synthetic dispatch failure");
        }
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
