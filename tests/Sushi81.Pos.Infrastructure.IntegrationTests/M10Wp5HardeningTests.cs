using ClosedXML.Excel;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

/// <summary>
/// Final WP5 production-path hardening evidence. The tests use real temporary
/// workbooks and the SQLite snapshot/import/commit seams; they never open Excel
/// or touch an owner database.
/// </summary>
[TestClass]
public sealed class M10Wp5HardeningTests
{
    private static readonly string[] ExpectedSheetNames = ["Products", "OptionGroups", "Options", "__Sushi81Meta"];
    private static readonly string[] ExpectedProductCodes = ["P-1", "P-2"];
    [TestMethod]
    public async Task RealFileRoundTripNoOpPreservesCatalogueRevisionAndNotifier()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var coded = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var uncoded = (await catalogue.CreateCategoryAsync("Desserts")).Value!;
        var activeId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-1", "Active", coded.Id, Money.FromCents(1299), 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Multi, false, 0, 2, 3,
                [new OptionDraft(Guid.Empty, "Avocado", Money.FromCents(150), true, 4)])]))).Value!;
        var inactiveId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-2", "Inactive", uncoded.Id, Money.FromCents(850), 5.5m, false, false, false, []))).Value!;

        var sourcePath = Path.Combine(paths.TempDirectory, "roundtrip.xlsx");
        await ExportAsync(store, sourcePath);
        using (var workbook = new XLWorkbook(sourcePath))
        {
            CollectionAssert.AreEqual(ExpectedSheetNames, workbook.Worksheets.Select(value => value.Name).ToArray());
            Assert.AreEqual(XLWorksheetVisibility.VeryHidden, workbook.Worksheet("__Sushi81Meta").Visibility);
            Assert.IsFalse(workbook.Worksheets.Any(value => value.Name.Equals("Categories", StringComparison.OrdinalIgnoreCase)));
        }

        var gateway = new ClosedXmlCatalogueWorkbookImportGateway();
        var notifier = new RecordingNotifier();
        var service = new CatalogueImportService(gateway, store, store,
            new WriteAuthorityGuard(WriteAuthorityState.Authoritative), notifier);
        var revisionBefore = await ReadRevisionAsync(factory);
        await using var source = File.OpenRead(sourcePath);
        var preview = await service.PreviewAsync(source, CatalogueImportMode.Update, sourcePath);

        Assert.IsFalse(preview.HasErrors, string.Join(";", preview.Preview.Issues.Select(issue => issue.Code)));
        Assert.IsNotNull(preview.Plan);
        Assert.IsEmpty(preview.Plan!.Operations);

        var committed = await service.CommitAsync(preview);
        Assert.IsTrue(committed.Succeeded, string.Join(";", committed.Issues.Select(issue => issue.Code)));
        Assert.IsFalse(committed.Changed);
        Assert.AreEqual(0, notifier.Calls);
        Assert.AreEqual(revisionBefore, await ReadRevisionAsync(factory));

        var reread = await store.ReadCatalogueImportBaselineAsync();
        Assert.IsNotNull(reread.Products.Single(value => value.Id == activeId));
        Assert.IsNotNull(reread.Products.Single(value => value.Id == inactiveId));
        Assert.AreEqual("PL", reread.Products.Single(value => value.Id == activeId).CategoryShortCode);
        Assert.IsNull(reread.Products.Single(value => value.Id == inactiveId).CategoryShortCode);

        var secondPath = Path.Combine(paths.TempDirectory, "roundtrip-again.xlsx");
        await ExportAsync(store, secondPath);
        await using var second = File.OpenRead(secondPath);
        var reparsed = await gateway.ReadAsync(second, secondPath);
        Assert.IsFalse(reparsed.HasErrors, string.Join(";", reparsed.Issues.Select(issue => issue.Code)));
        CollectionAssert.AreEquivalent(ExpectedProductCodes, reparsed.Products.Select(value => value.ProductCode).ToArray());
        Assert.AreEqual("PL", reparsed.Products.Single(value => value.ProductCode == "P-1").CategoryShortCode);
        Assert.IsTrue(string.IsNullOrEmpty(reparsed.Products.Single(value => value.ProductCode == "P-2").CategoryShortCode));
    }

    [TestMethod]
    public async Task RealFileEditAndNewHierarchyCommitOnceWithoutDeletingOmittedRows()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var runner = new SqliteTransactionRunner(factory);
        var store = new SqliteCatalogueStore(factory, runner, ids, clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var existingId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-1", "Original", category.Id, Money.FromCents(100), 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Single, false, null, null, 1,
                [new OptionDraft(Guid.Empty, "Sauce", Money.FromCents(25), true, 2)])]))).Value!;
        var omittedId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-OMIT", "Omitted", category.Id, Money.FromCents(50), 20m, true, false, false, []))).Value!;

        var sourcePath = Path.Combine(paths.TempDirectory, "edited.xlsx");
        await ExportAsync(store, sourcePath);
        using (var workbook = new XLWorkbook(sourcePath))
        {
            var products = workbook.Worksheet("Products");
            var existingRow = Enumerable.Range(2, products.LastRowUsed()!.RowNumber() - 1).Single(row => products.Cell(row, 1).GetString() == "P-1");
            products.Cell(existingRow, 2).Value = "Edited";
            products.Cell(existingRow, 5).Value = 2.50m;
            var newRow = products.LastRowUsed()!.RowNumber() + 1;
            products.Cell(newRow, 1).Value = "P-NEW";
            products.Cell(newRow, 2).Value = "New product";
            products.Cell(newRow, 3).Value = "Desserts";
            products.Cell(newRow, 4).Value = "DE";
            products.Cell(newRow, 5).Value = 4.50m;
            products.Cell(newRow, 6).Value = 20m;
            products.Cell(newRow, 7).Value = true;
            products.Cell(newRow, 8).Value = false;
            products.Cell(newRow, 9).Value = true;

            var groups = workbook.Worksheet("OptionGroups");
            var groupRow = groups.LastRowUsed()!.RowNumber() + 1;
            groups.Cell(groupRow, 1).Value = "P-NEW";
            groups.Cell(groupRow, 2).Value = "New product";
            groups.Cell(groupRow, 3).Value = "Toppings";
            groups.Cell(groupRow, 4).Value = "MULTI";
            groups.Cell(groupRow, 5).Value = false;
            groups.Cell(groupRow, 6).Value = 0;
            groups.Cell(groupRow, 7).Value = 1;
            groups.Cell(groupRow, 8).Value = 0;

            var options = workbook.Worksheet("Options");
            var optionRow = options.LastRowUsed()!.RowNumber() + 1;
            options.Cell(optionRow, 1).Value = "P-NEW";
            options.Cell(optionRow, 2).Value = "New product";
            options.Cell(optionRow, 3).Value = "Toppings";
            options.Cell(optionRow, 4).Value = "Sesame";
            options.Cell(optionRow, 5).Value = 0.50m;
            options.Cell(optionRow, 6).Value = true;
            options.Cell(optionRow, 7).Value = 0;
            workbook.Save();
        }

        var gateway = new ClosedXmlCatalogueWorkbookImportGateway();
        var notifier = new RecordingNotifier();
        var service = new CatalogueImportService(gateway, store, store,
            new WriteAuthorityGuard(WriteAuthorityState.Authoritative), notifier);
        await using var source = File.OpenRead(sourcePath);
        var preview = await service.PreviewAsync(source, CatalogueImportMode.Update, sourcePath);
        Assert.IsFalse(preview.HasErrors, string.Join(";", preview.Preview.Issues.Select(issue => $"{issue.Code}:{issue.ExcelRow}:{issue.FieldKey}")));
        Assert.AreEqual(1, preview.Preview.ProductCreateCount);
        Assert.AreEqual(1, preview.Preview.ProductModifyCount);
        Assert.AreEqual(1, preview.Preview.NewCategoryCount);
        Assert.AreEqual(1, preview.Preview.OptionGroupCreateCount);
        Assert.AreEqual(1, preview.Preview.OptionCreateCount);
        Assert.IsNotNull(preview.Plan);

        var revisionBefore = await ReadRevisionAsync(factory);
        var committed = await service.CommitAsync(preview);
        Assert.IsTrue(committed.Succeeded, string.Join(";", committed.Issues.Select(issue => issue.Code)));
        Assert.IsTrue(committed.Changed);
        Assert.AreEqual(1, notifier.Calls);
        Assert.AreEqual(revisionBefore + 1, await ReadRevisionAsync(factory));

        var edited = await store.GetProductForEditAsync(existingId);
        Assert.IsNotNull(edited);
        Assert.AreEqual("Edited", edited!.Name);
        Assert.AreEqual(250, edited.PriceTtc.Cents);
        var allProducts = await store.ListProductsAsync();
        Assert.IsNotNull(allProducts.Single(value => value.Id == omittedId));
        var added = allProducts.Single(value => value.Code == "P-NEW");
        Assert.AreNotEqual(Guid.Empty, added.Id);
        var addedDraft = await store.GetProductForEditAsync(added.Id);
        Assert.IsNotNull(addedDraft);
        var desserts = (await store.ListCategoriesAsync()).Single(value => value.Name == "Desserts");
        Assert.AreEqual(desserts.Id, addedDraft!.CategoryId);
        Assert.AreEqual("DE", desserts.ShortCode);
        Assert.HasCount(1, addedDraft.Groups);
        Assert.AreEqual("Toppings", addedDraft.Groups[0].Name);
        Assert.HasCount(1, addedDraft.Groups[0].Options);
        Assert.AreEqual("Sesame", addedDraft.Groups[0].Options[0].Name);
    }

    [TestMethod]
    public async Task RealFileTamperBlocksBeforeWriteAndPreservesRevisionAndNotifier()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var category = (await new CatalogueService(store).CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        await new CatalogueService(store).CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Product", category.Id, Money.FromCents(100), 20m, true, false, false, []));

        var path = Path.Combine(paths.TempDirectory, "tampered.xlsx");
        await ExportAsync(store, path);
        using (var workbook = new XLWorkbook(path))
        {
            workbook.Worksheet("Products").Cell(2, 10).Value = "product:unknown";
            workbook.Save();
        }

        var gateway = new ClosedXmlCatalogueWorkbookImportGateway();
        var notifier = new RecordingNotifier();
        var service = new CatalogueImportService(gateway, store, store,
            new WriteAuthorityGuard(WriteAuthorityState.Authoritative), notifier);
        var revisionBefore = await ReadRevisionAsync(factory);
        await using var source = File.OpenRead(path);
        var preview = await service.PreviewAsync(source, CatalogueImportMode.Update, path);
        Assert.IsTrue(preview.HasErrors);
        CollectionAssert.Contains(preview.Preview.Issues.Select(issue => issue.Code).ToArray(), "unknown-row-key");
        var committed = await service.CommitAsync(preview);
        Assert.IsFalse(committed.Succeeded);
        Assert.AreEqual(0, notifier.Calls);
        Assert.AreEqual(revisionBefore, await ReadRevisionAsync(factory));
    }

    private static async Task ExportAsync(SqliteCatalogueStore store, string path)
    {
        await using var destination = File.Create(path);
        var snapshot = await store.ReadCatalogueWorkbookSnapshotAsync();
        await new ClosedXmlCatalogueWorkbookGateway().WriteAsync(new CatalogueWorkbookExport(snapshot, Guid.NewGuid()), destination);
    }

    private static async Task<long> ReadRevisionAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        var value = await command.ExecuteScalarAsync();
        return long.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var revision) ? revision : 0;
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 18, 18, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 18);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TempPaths : IAppPaths, IDisposable
    {
        public TempPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M10.Wp5.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            EnsureInitialized();
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
                Directory.CreateDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
