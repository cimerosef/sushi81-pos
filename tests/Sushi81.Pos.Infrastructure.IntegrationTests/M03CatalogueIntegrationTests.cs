using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Ids;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M03CatalogueIntegrationTests
{
    [TestMethod]
    public async Task ProductionMigrationCreatesFiveTablesAndSingletonDefaultsIdempotently()
    {
        using var paths = new TempPaths();
        var factory = await InitializeAsync(paths);
        foreach (var table in new[] { "categories", "products", "option_groups", "options", "business_settings" })
            Assert.AreEqual(1L, await ScalarAsync(factory, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM business_settings;"));
        var tx = new SqliteTransactionRunner(factory); var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM business_settings;"));
    }

    [TestMethod]
    public async Task M03QueriesUseReadOnlyPathAndNeverCreateMissingDatabase()
    {
        using var paths = new TempPaths();
        var factory = new SqliteConnectionFactory(paths);
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), new FixedClock());
        await Assert.ThrowsAsync<SqliteException>(async () => await store.ListCategoriesAsync());
        Assert.IsFalse(File.Exists(paths.LiveDatabasePath));
        var settings = new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), new FixedClock());
        await Assert.ThrowsAsync<SqliteException>(async () => await settings.GetAsync());
        Assert.IsFalse(File.Exists(paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task MigrationOneToTwoPreservesExistingFoundationSentinel()
    {
        using var paths = new TempPaths(); var factory = new SqliteConnectionFactory(paths); var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, M01Migrations.All, clock).InitializeAsync();
        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            await using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO foundation_metadata(key,value) VALUES ('sentinel','preserved');"; await command.ExecuteNonQueryAsync();
        }
        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshots).InitializeAsync();
        await using var verify = await factory.OpenLiveConnectionAsync();
        await using var check = verify.CreateCommand(); check.CommandText = "SELECT value FROM foundation_metadata WHERE key='sentinel';";
        Assert.AreEqual("preserved", Convert.ToString(await check.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
    }

    [TestMethod]
    public async Task EditedSettingsRemainAfterCurrentMigrationRerun()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock();
        var store = new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), clock);
        var edited = (await store.GetAsync()) with { PickupDiscountRate = 0.375m, DeliveryFeeAmountTtc = Money.FromCents(777) };
        Assert.IsTrue((await store.UpdateAsync(edited)).Succeeded);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var reopened = await new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), clock).GetAsync();
        Assert.AreEqual(edited.PickupDiscountRate, reopened.PickupDiscountRate); Assert.AreEqual(edited.DeliveryFeeAmountTtc, reopened.DeliveryFeeAmountTtc);
    }

    [TestMethod]
    public async Task ProductForeignKeyRejectsMissingCategory()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths);
        await using var connection = await factory.OpenLiveConnectionAsync(); await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc) VALUES ('p','P','P','Product','missing',0,'0',1,1,0,'2026-08-29T00:00:00Z','2026-08-29T00:00:00Z');";
        await Assert.ThrowsAsync<SqliteException>(async () => await command.ExecuteNonQueryAsync());
    }

    [TestMethod]
    public async Task DeactivateReactivatePreservesOptionHierarchy()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var service = new CatalogueService(new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock));
        var category = (await service.CreateCategoryAsync("Plats")).Value!;
        var draft = new ProductDraft(Guid.Empty, "P", "Product", category.Id, Money.Zero, 10m, true, true, true, [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Multi, false, 0, 2, 0, [new OptionDraft(Guid.Empty, "Plus", Money.FromCents(25), true, 0)])]);
        var product = (await service.CreateProductAsync(draft)).Value!;
        Assert.IsTrue((await service.SetProductActiveAsync(product, false)).Succeeded); Assert.IsTrue((await service.SetProductActiveAsync(product, true)).Succeeded);
        var loaded = await service.GetProductForEditAsync(product); Assert.IsNotNull(loaded); Assert.HasCount(1, loaded!.Groups); Assert.HasCount(1, loaded.Groups[0].Options);
    }

    [TestMethod]
    public async Task UpdateAndRenamePreserveOpaqueIdentityAndAssociation()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var service = new CatalogueService(new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock));
        var first = (await service.CreateCategoryAsync("Plats")).Value!; var second = (await service.CreateCategoryAsync("Desserts")).Value!;
        var created = (await service.CreateProductAsync(new ProductDraft(Guid.NewGuid(), "P", "Product", first.Id, Money.Zero, 0m, true, true, false, []))).Value!;
        var loaded = (await service.GetProductForEditAsync(created))!;
        Assert.IsTrue((await service.UpdateProductAsync(created, loaded with { Code = "P2", Name = "Updated", CategoryId = second.Id, PriceTtc = Money.FromCents(345), VatRate = 100m, IsActive = false, DiscountEligible = false, OptionsEnabled = true })).Succeeded);
        Assert.IsTrue((await service.RenameCategoryAsync(second.Id, "Desserts renommés")).Succeeded);
        var listed = (await service.ListProductsAsync()).Single(); Assert.AreEqual(created, listed.Id); Assert.AreEqual(second.Id, listed.CategoryId); Assert.AreEqual("Desserts renommés", listed.CategoryName); Assert.IsFalse(listed.IsActive);
    }

    [TestMethod]
    public async Task ReorderingAndExplicitChildDeletionPersistExactly()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var service = new CatalogueService(new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock));
        var category = (await service.CreateCategoryAsync("Plats")).Value!;
        var product = (await service.CreateProductAsync(new ProductDraft(Guid.Empty, "P", "Product", category.Id, Money.Zero, 10m, true, true, true, [new OptionGroupDraft(Guid.Empty, "A", SelectionMode.Single, false, null, null, 0, [new OptionDraft(Guid.Empty, "A1", Money.Zero, true, 0)]), new OptionGroupDraft(Guid.Empty, "B", SelectionMode.Multi, false, 0, 2, 1, [new OptionDraft(Guid.Empty, "B1", Money.Zero, true, 0), new OptionDraft(Guid.Empty, "B2", Money.Zero, true, 1)])]))).Value!;
        var loaded = (await service.GetProductForEditAsync(product))!; var retained = loaded.Groups[1];
        var changed = await service.UpdateProductAsync(product, loaded with { Groups = [retained with { DisplayOrder = 0, Options = [retained.Options[1] with { DisplayOrder = 0 }] }] });
        Assert.IsTrue(changed.Succeeded, changed.ErrorMessage); var reloaded = (await service.GetProductForEditAsync(product))!; Assert.HasCount(1, reloaded.Groups); Assert.AreEqual("B", reloaded.Groups[0].Name); Assert.HasCount(1, reloaded.Groups[0].Options); Assert.AreEqual("B2", reloaded.Groups[0].Options[0].Name);
    }

    [TestMethod]
    public async Task DuplicateConflictLeavesExistingCatalogueUnchanged()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var service = new CatalogueService(new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock));
        var first = (await service.CreateCategoryAsync("Plats")).Value!; Assert.IsFalse((await service.CreateCategoryAsync(" plats ")).Succeeded);
        var draft = new ProductDraft(Guid.Empty, "P", "Product", first.Id, Money.Zero, 10m, true, true, false, []); Assert.IsTrue((await service.CreateProductAsync(draft)).Succeeded); Assert.IsFalse((await service.CreateProductAsync(draft)).Succeeded);
        Assert.HasCount(1, await service.ListProductsAsync()); Assert.HasCount(1, await service.ListCategoriesAsync());
    }

    [TestMethod]
    public async Task FailedM03MigrationPreservesLiveDatabaseWithoutReset()
    {
        using var paths = new TempPaths(); var factory = new SqliteConnectionFactory(paths); var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, [new SqliteMigration(1, "foundation", "CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY,value TEXT NOT NULL); INSERT INTO foundation_metadata VALUES ('k','v');")], clock).InitializeAsync();
        var failing = new SqliteMigrationRunner(factory, [new SqliteMigration(1, "foundation", "CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY,value TEXT NOT NULL); INSERT INTO foundation_metadata VALUES ('k','v');"), new SqliteMigration(2, "broken", "THIS IS NOT VALID SQL;")], clock, new SqliteLocalRecoverySnapshotService(paths, factory, clock));
        await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await failing.InitializeAsync());
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM foundation_metadata;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='products';"));
    }

    [TestMethod]
    public async Task FullCatalogueLifecyclePreservesIdsOrderAndCascadeDelete()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var ids = new DeterministicIds(); var runner = new SqliteTransactionRunner(factory); var store = new SqliteCatalogueStore(factory, runner, ids, clock); var service = new CatalogueService(store);
        var categoryResult = await service.CreateCategoryAsync("  Plats  "); Assert.IsTrue(categoryResult.Succeeded); var category = categoryResult.Value!;
        var groupId = Guid.NewGuid();
        var draft = new ProductDraft(Guid.Empty, " F1 ", " Saumon ", category.Id, Money.FromCents(1250), 10m, true, true, true,
            [new OptionGroupDraft(groupId, "Taille", SelectionMode.Single, true, null, null, 4, [new OptionDraft(Guid.Empty, "Grand", Money.FromCents(100), true, 8)]), new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Multi, false, 0, 3, 1, [new OptionDraft(Guid.Empty, "Avocat", Money.FromCents(-50), true, 1), new OptionDraft(Guid.Empty, "Sans", Money.Zero, false, 0)])]);
        var created = await service.CreateProductAsync(draft); Assert.IsTrue(created.Succeeded, created.ErrorMessage); var productId = created.Value;
        var loaded = await service.GetProductForEditAsync(productId); Assert.IsNotNull(loaded); Assert.AreEqual(productId, loaded!.Id); Assert.AreEqual("F1", loaded.Code); Assert.HasCount(2, loaded.Groups); Assert.AreEqual(0, loaded.Groups[0].DisplayOrder); Assert.AreEqual(1, loaded.Groups[1].DisplayOrder); Assert.HasCount(2, loaded.Groups.Single(group => group.Name == "Extras").Options);
        var updated = await service.UpdateProductAsync(productId, loaded with { Code = "F2", Name = "Saumon nouveau", PriceTtc = Money.FromCents(1300), Groups = [loaded.Groups[0] with { DisplayOrder = 0 }, loaded.Groups[1] with { DisplayOrder = 1 }] }); Assert.IsTrue(updated.Succeeded, updated.ErrorMessage);
        var reloaded = await service.GetProductForEditAsync(productId); Assert.AreEqual(productId, reloaded!.Id); Assert.AreEqual("F2", reloaded.Code); Assert.AreEqual("Extras", reloaded.Groups[0].Name); Assert.AreEqual(0, reloaded.Groups[0].DisplayOrder);
        Assert.IsTrue((await service.SetProductActiveAsync(productId, false)).Succeeded); Assert.IsFalse((await service.ListProductsAsync(active: true)).Any()); Assert.IsTrue((await service.ListProductsAsync(active: false)).Any());
        Assert.IsTrue((await service.DeleteProductAsync(productId)).Succeeded); Assert.IsNull(await service.GetProductForEditAsync(productId));
        var reused = await service.CreateProductAsync(draft with { Code = "F2", Groups = [] }); Assert.IsTrue(reused.Succeeded); Assert.AreNotEqual(productId, reused.Value);
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM categories;"));
    }

    [TestMethod]
    public async Task CategoryAndProductNormalizedUniquenessReturnsValidationResults()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock); var service = new CatalogueService(store);
        Assert.IsTrue((await service.CreateCategoryAsync("Café")).Succeeded); var duplicateCategory = await service.CreateCategoryAsync(" cafe\u0301 "); Assert.IsFalse(duplicateCategory.Succeeded);
        var categories = await service.ListCategoriesAsync(); var draft = new ProductDraft(Guid.Empty, "x1", "One", categories[0].Id, Money.Zero, 0m, true, true, false, []); Assert.IsTrue((await service.CreateProductAsync(draft)).Succeeded); var duplicate = await service.CreateProductAsync(draft with { Id = Guid.Empty, Code = " X1 " }); Assert.IsFalse(duplicate.Succeeded);
    }

    [TestMethod]
    public async Task SettingsDefaultsEditAndProcessReopenRoundTripExactly()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var store = new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), clock); var service = new Sushi81.Pos.Application.Settings.BusinessSettingsService(store);
        var defaults = await service.GetAsync(); Assert.AreEqual(0.10m, defaults.PickupDiscountRate); Assert.AreEqual(1500L, defaults.PickupDiscountMinTotalTtc.Cents);
        var edited = defaults with { PickupDiscountRate = 0.125m, PickupDiscountMinTotalTtc = Money.FromCents(1775), DeliveryMinMerchandiseTotalTtc = Money.FromCents(3450), DeliveryFeeEnabled = true, DeliveryFeeAmountTtc = Money.FromCents(525) }; Assert.IsTrue((await service.UpdateAsync(edited)).Succeeded);
        var reopened = await new Sushi81.Pos.Application.Settings.BusinessSettingsService(new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), clock)).GetAsync(); Assert.AreEqual(edited.PickupDiscountRate, reopened.PickupDiscountRate); Assert.AreEqual(edited.DeliveryFeeAmountTtc, reopened.DeliveryFeeAmountTtc); Assert.IsTrue(reopened.DeliveryFeeEnabled);
    }

    [TestMethod]
    public async Task InvalidAggregateRollsBackEveryRow()
    {
        using var paths = new TempPaths(); var factory = await InitializeAsync(paths); var clock = new FixedClock(); var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock); var service = new CatalogueService(store); var category = (await service.CreateCategoryAsync("Plats")).Value!;
        var invalid = new ProductDraft(Guid.Empty, "BAD", "Bad", category.Id, Money.Zero, 10m, true, true, true, [new OptionGroupDraft(Guid.Empty, "Group", SelectionMode.Multi, false, null, null, 0, [])]);
        var result = await store.CreateProductAsync(invalid); Assert.IsFalse(result.Succeeded); Assert.IsEmpty(await service.ListProductsAsync()); Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM option_groups;"));
    }

    private static async Task<SqliteConnectionFactory> InitializeAsync(IAppPaths paths)
    {
        var factory = new SqliteConnectionFactory(paths); await new SqliteMigrationRunner(factory, ProductionMigrations.All, new FixedClock()).InitializeAsync(); return factory;
    }
    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql) { await using var connection = await factory.OpenLiveConnectionAsync(); return await ScalarAsync(connection, sql); }
    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql) { await using var command = connection.CreateCommand(); command.CommandText = sql; return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture); }

    private sealed class FixedClock : IBusinessClock { public DateTimeOffset UtcNow => new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero); public DateOnly BusinessDate => new(2026, 8, 29); public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc; }
    private sealed class DeterministicIds : IIdGenerator { private int count; public Guid NewId() => Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}"); }
    private sealed class TempPaths : IAppPaths, IDisposable
    {
        public TempPaths() { RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M03.Tests", Guid.NewGuid().ToString("N")); DataDirectory = Path.Combine(RootDirectory, "Data"); RecoveryDirectory = Path.Combine(RootDirectory, "Recovery"); CacheDirectory = Path.Combine(RootDirectory, "Cache"); LogsDirectory = Path.Combine(RootDirectory, "Logs"); ConfigDirectory = Path.Combine(RootDirectory, "Config"); TempDirectory = Path.Combine(RootDirectory, "Temp"); LiveDatabasePath = Path.Combine(DataDirectory, "live.db"); EnsureInitialized(); }
        public string RootDirectory { get; } public string DataDirectory { get; } public string RecoveryDirectory { get; } public string CacheDirectory { get; } public string LogsDirectory { get; } public string ConfigDirectory { get; } public string TempDirectory { get; } public string LiveDatabasePath { get; }
        public void EnsureInitialized() { foreach (var p in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(p); }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
