using System.IO.Compression;
using ClosedXML.Excel;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class CatalogueWorkbookIntegrationTests
{
    private static readonly string[] ExpectedSheetNames = ["Products", "OptionGroups", "Options", "__Sushi81Meta"];

    [TestMethod]
    public async Task ExportWritesBoundedSheetsTechnicalBindingsAndProtection()
    {
        var productId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var model = new CatalogueWorkbookExport(
            [new CatalogueWorkbookProduct(
                productId, "P-01", "Salmon", "Plats", "PL", Money.FromCents(1299), 20m, false, true, true,
                [new CatalogueWorkbookOptionGroup(groupId, productId, "P-01", "Salmon", "Extras", SelectionMode.Multi, false, 0, 2, 0,
                    [new CatalogueWorkbookOption(optionId, groupId, "P-01", "Salmon", "Extras", "Avocado", Money.FromCents(150), false, 0)])])],
            Guid.NewGuid());
        var bytes = await WriteAsync(model);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        CollectionAssert.AreEqual(ExpectedSheetNames, workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
        Assert.AreEqual(XLWorksheetVisibility.VeryHidden, workbook.Worksheet("__Sushi81Meta").Visibility);

        var products = workbook.Worksheet("Products");
        Assert.AreEqual("P-01", products.Cell(2, 1).GetString());
        Assert.AreEqual("PL", products.Cell(2, 4).GetString());
        Assert.AreEqual(12.99m, products.Cell(2, 5).GetValue<decimal>());
        Assert.IsFalse(products.Cell(2, 7).GetBoolean());
        Assert.IsFalse(products.Cell(2, 1).Style.Protection.Locked);
        Assert.IsFalse(products.Cell(2, 10).Style.Protection.Locked);
        Assert.IsFalse(products.Cell(2, 11).Style.Protection.Locked);
        Assert.IsTrue(products.Column(10).IsHidden);
        Assert.IsTrue(products.Protection.IsProtected);
        Assert.IsTrue(products.Protection.AllowedElements.HasFlag(XLSheetProtectionElements.Sort));
        Assert.IsTrue(products.Protection.AllowedElements.HasFlag(XLSheetProtectionElements.AutoFilter));
        Assert.IsFalse(products.Protection.AllowedElements.HasFlag(XLSheetProtectionElements.FormatColumns));

        var groups = workbook.Worksheet("OptionGroups");
        Assert.AreEqual("Extras", groups.Cell(2, 3).GetString());
        Assert.AreEqual(groupId.ToString("D"), groups.Cell(2, 12).GetString());
        Assert.IsFalse(groups.Cell(2, 9).Style.Protection.Locked);
        Assert.IsFalse(groups.Cell(2, 12).Style.Protection.Locked);
        Assert.IsTrue(groups.Column(9).IsHidden);
        Assert.IsFalse(groups.Protection.AllowedElements.HasFlag(XLSheetProtectionElements.FormatColumns));
        var options = workbook.Worksheet("Options");
        Assert.AreEqual("Avocado", options.Cell(2, 4).GetString());
        Assert.IsFalse(options.Cell(2, 6).GetBoolean());
        Assert.IsFalse(options.Cell(2, 8).Style.Protection.Locked);
        Assert.IsFalse(options.Cell(2, 12).Style.Protection.Locked);
        Assert.IsTrue(options.Column(8).IsHidden);
        Assert.IsFalse(options.Protection.AllowedElements.HasFlag(XLSheetProtectionElements.FormatColumns));

        var metadata = workbook.Worksheet("__Sushi81Meta");
        Assert.AreEqual("Worksheet", metadata.Cell(6, 1).GetString());
        Assert.AreEqual("product_code", metadata.Cell(7, 2).GetString());
        Assert.AreEqual(1, metadata.Cell(7, 3).GetValue<int>());
        Assert.IsTrue(metadata.Cell(7, 4).GetBoolean());
        Assert.IsTrue(metadata.Cell(7, 5).GetBoolean());
        Assert.AreEqual("product-code", metadata.Cell(7, 6).GetString());
        Assert.AreEqual("EntityType", metadata.Cell(43, 1).GetString());
        Assert.AreEqual("Product", metadata.Cell(44, 1).GetString());
        Assert.AreEqual("Products", metadata.Cell(44, 5).GetString());

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.IsNull(archive.GetEntry("xl/vbaProject.bin"));
    }

    [TestMethod]
    public async Task EmptyExportStillProvidesDeterministicHeadersAndMetadata()
    {
        var bytes = await WriteAsync(new CatalogueWorkbookExport([], Guid.NewGuid()));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.AreEqual("Product Code", workbook.Worksheet("Products").Cell(1, 1).GetString());
        Assert.AreEqual("Option Name", workbook.Worksheet("Options").Cell(1, 4).GetString());
        Assert.AreEqual("ContractVersion", workbook.Worksheet("__Sushi81Meta").Cell(2, 1).GetString());
        Assert.AreEqual("M10-CATALOGUE-1", workbook.Worksheet("__Sushi81Meta").Cell(2, 2).GetString());

        var products = workbook.Worksheet("Products");
        Assert.IsFalse(products.Cell(2, 1).Style.Protection.Locked);
        Assert.IsFalse(products.Cell(2, 10).Style.Protection.Locked);
        Assert.AreEqual(string.Empty, products.Cell(2, 10).GetString());
        products.Row(2).InsertRowsBelow(1);
        Assert.IsFalse(products.Cell(3, 1).Style.Protection.Locked);
        Assert.IsFalse(products.Cell(3, 10).Style.Protection.Locked);
        Assert.AreEqual(string.Empty, products.Cell(3, 10).GetString());
    }

    [TestMethod]
    public async Task SortingFullBoundedRangeKeepsTechnicalBindingsWithBusinessRows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var bytes = await WriteAsync(new CatalogueWorkbookExport(
            [
                new CatalogueWorkbookProduct(firstId, "B", "Zeta", "Plats", "P", Money.FromCents(100), 20m, true, false, false, []),
                new CatalogueWorkbookProduct(secondId, "A", "Alpha", "Plats", "P", Money.FromCents(200), 20m, true, false, false, [])
            ], Guid.NewGuid()));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var products = workbook.Worksheet("Products");
        products.Range(2, 1, 3, 11).Sort(2, XLSortOrder.Ascending);

        Assert.AreEqual("A", products.Cell(2, 1).GetString());
        Assert.AreEqual($"product:{secondId:N}", products.Cell(2, 10).GetString());
        Assert.AreEqual("B", products.Cell(3, 1).GetString());
        Assert.AreEqual($"product:{firstId:N}", products.Cell(3, 10).GetString());

        products.Row(3).InsertRowsAbove(1);
        Assert.AreEqual("B", products.Cell(4, 1).GetString());
        Assert.AreEqual($"product:{firstId:N}", products.Cell(4, 10).GetString());
        Assert.AreEqual(string.Empty, products.Cell(3, 10).GetString());
    }

    [TestMethod]
    public async Task SortingChildRangesKeepsParentAndOwnBindingsTogether()
    {
        var productId = Guid.NewGuid();
        var groupZId = Guid.NewGuid();
        var groupAId = Guid.NewGuid();
        var optionZId = Guid.NewGuid();
        var optionAId = Guid.NewGuid();
        var model = new CatalogueWorkbookExport(
            [new CatalogueWorkbookProduct(
                productId,
                "P-1",
                "Product",
                "Plats",
                "P",
                Money.FromCents(100),
                20m,
                true,
                false,
                true,
                [
                    new CatalogueWorkbookOptionGroup(
                        groupZId,
                        productId,
                        "P-1",
                        "Product",
                        "Z group",
                        SelectionMode.Multi,
                        false,
                        0,
                        1,
                        1,
                        [new CatalogueWorkbookOption(optionZId, groupZId, "P-1", "Product", "Z group", "Z option", Money.Zero, true, 0)]),
                    new CatalogueWorkbookOptionGroup(
                        groupAId,
                        productId,
                        "P-1",
                        "Product",
                        "A group",
                        SelectionMode.Multi,
                        false,
                        0,
                        1,
                        0,
                        [new CatalogueWorkbookOption(optionAId, groupAId, "P-1", "Product", "A group", "A option", Money.Zero, true, 0)])])],
            Guid.NewGuid());

        using var workbook = new XLWorkbook(new MemoryStream(await WriteAsync(model)));
        var groups = workbook.Worksheet("OptionGroups");
        groups.Range(2, 1, 3, 12).Sort(3, XLSortOrder.Ascending);
        Assert.AreEqual("A group", groups.Cell(2, 3).GetString());
        Assert.AreEqual($"product:{productId:N}", groups.Cell(2, 9).GetString());
        Assert.AreEqual($"group:{groupAId:N}", groups.Cell(2, 10).GetString());
        Assert.AreEqual("Z group", groups.Cell(3, 3).GetString());
        Assert.AreEqual($"product:{productId:N}", groups.Cell(3, 9).GetString());
        Assert.AreEqual($"group:{groupZId:N}", groups.Cell(3, 10).GetString());
        groups.Row(3).InsertRowsAbove(1);
        Assert.AreEqual(string.Empty, groups.Cell(3, 9).GetString());
        Assert.AreEqual(string.Empty, groups.Cell(3, 10).GetString());

        var options = workbook.Worksheet("Options");
        options.Range(2, 1, 3, 12).Sort(4, XLSortOrder.Ascending);
        Assert.AreEqual("A option", options.Cell(2, 4).GetString());
        Assert.AreEqual($"product:{productId:N}", options.Cell(2, 8).GetString());
        Assert.AreEqual($"group:{groupAId:N}", options.Cell(2, 9).GetString());
        Assert.AreEqual($"option:{optionAId:N}", options.Cell(2, 10).GetString());
        Assert.AreEqual("Z option", options.Cell(3, 4).GetString());
        Assert.AreEqual($"product:{productId:N}", options.Cell(3, 8).GetString());
        Assert.AreEqual($"group:{groupZId:N}", options.Cell(3, 9).GetString());
        Assert.AreEqual($"option:{optionZId:N}", options.Cell(3, 10).GetString());
        options.Row(3).InsertRowsAbove(1);
        Assert.AreEqual(string.Empty, options.Cell(3, 8).GetString());
        Assert.AreEqual(string.Empty, options.Cell(3, 9).GetString());
        Assert.AreEqual(string.Empty, options.Cell(3, 10).GetString());
    }

    [TestMethod]
    public async Task CanonicalFingerprintsDistinguishDelimiterAndUnicodeValues()
    {
        var first = new CatalogueWorkbookProduct(Guid.NewGuid(), "a|b", "c", "Cat", "P", Money.FromCents(100), 20m, true, false, false, []);
        var second = first with { ProductId = Guid.NewGuid(), Code = "a", Name = "b|c" };
        var bytes = await WriteAsync(new CatalogueWorkbookExport([first, second], Guid.NewGuid()));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var metadata = workbook.Worksheet("__Sushi81Meta");
        var fingerprintOne = metadata.Cell(44, 7).GetString();
        var fingerprintTwo = metadata.Cell(45, 7).GetString();
        Assert.AreNotEqual(fingerprintOne, fingerprintTwo);
    }

    [TestMethod]
    public async Task ProductionSnapshotRemainsCoherentAcrossConcurrentCommit()
    {
        using var paths = new TempPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();

        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "OLD")).Value!;
        var product = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty,
            "P-1",
            "Product",
            category.Id,
            Money.FromCents(100),
            20m,
            true,
            false,
            true,
            [new OptionGroupDraft(
                Guid.Empty,
                "Extras",
                SelectionMode.Multi,
                false,
                0,
                2,
                0,
                [new OptionDraft(Guid.Empty, "Old option", Money.FromCents(25), true, 0)])]))).Value!;
        var current = (await store.GetProductForEditAsync(product))!;
        var group = current.Groups.Single();
        var option = group.Options.Single();

        var productsRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSnapshot = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.SnapshotAfterProductsReadAsync = async cancellationToken =>
        {
            productsRead.TrySetResult(true);
            await releaseSnapshot.Task.WaitAsync(cancellationToken);
        };

        var snapshotTask = store.ReadCatalogueWorkbookSnapshotAsync();
        await productsRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await using (var writer = await factory.OpenLiveConnectionAsync())
        await using (var transaction = (SqliteTransaction)await writer.BeginTransactionAsync())
        {
            async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
            {
                await using var command = writer.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
                await command.ExecuteNonQueryAsync();
            }

            await ExecuteAsync(
                "UPDATE categories SET short_code=$code, normalized_short_code=$normalized WHERE category_id=$id;",
                ("$code", "NEW"), ("$normalized", "new"), ("$id", category.Id.ToString()));
            await ExecuteAsync(
                "UPDATE option_groups SET name=$name WHERE option_group_id=$id;",
                ("$name", "New extras"), ("$id", group.Id.ToString()));
            await ExecuteAsync(
                "UPDATE options SET name=$name WHERE option_id=$id;",
                ("$name", "New option"), ("$id", option.Id.ToString()));
            await transaction.CommitAsync();
        }

        releaseSnapshot.TrySetResult(true);
        var snapshot = await snapshotTask;
        var exported = snapshot.Single();
        Assert.AreEqual("OLD", exported.CategoryShortCode);
        Assert.AreEqual("Extras", exported.OptionGroups.Single().Name);
        Assert.AreEqual("Old option", exported.OptionGroups.Single().Options.Single().Name);

        store.SnapshotAfterProductsReadAsync = null;
        var currentSnapshot = (await store.ReadCatalogueWorkbookSnapshotAsync()).Single();
        Assert.AreEqual("NEW", currentSnapshot.CategoryShortCode);
        Assert.AreEqual("New extras", currentSnapshot.OptionGroups.Single().Name);
        Assert.AreEqual("New option", currentSnapshot.OptionGroups.Single().Options.Single().Name);
    }

    private static async Task<byte[]> WriteAsync(CatalogueWorkbookExport model)
    {
        await using var stream = new MemoryStream();
        await new ClosedXmlCatalogueWorkbookGateway().WriteAsync(model, stream);
        return stream.ToArray();
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 17);
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
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M10.Workbook.Tests", Guid.NewGuid().ToString("N"));
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
