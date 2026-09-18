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
            [
                new OptionGroupDraft(Guid.Empty, "Sauces", SelectionMode.Single, false, null, null, 7,
                    [new OptionDraft(Guid.Empty, "Ponzu", Money.FromCents(75), true, 9)]),
                new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Multi, false, 0, 2, 3,
                    [
                        new OptionDraft(Guid.Empty, "Avocado", Money.FromCents(150), false, 4),
                        new OptionDraft(Guid.Empty, "Wasabi", Money.Zero, true, 2)
                    ])
            ]))).Value!;
        var inactiveId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-2", "Inactive", uncoded.Id, Money.FromCents(850), 5.5m, false, false, false, []))).Value!;
        var seeded = (await store.GetProductForEditAsync(activeId))!;
        var sauces = seeded.Groups.Single(value => value.Name == "Sauces");
        var extras = seeded.Groups.Single(value => value.Name == "Extras");
        await SetGroupDisplayOrderAsync(factory, sauces.Id, 70);
        await SetGroupDisplayOrderAsync(factory, extras.Id, 30);
        await SetOptionDisplayOrderAsync(factory, sauces.Options.Single().Id, 90);
        await SetOptionDisplayOrderAsync(factory, extras.Options.Single(value => value.Name == "Avocado").Id, 40);
        await SetOptionDisplayOrderAsync(factory, extras.Options.Single(value => value.Name == "Wasabi").Id, 20);

        var sourcePath = Path.Combine(paths.TempDirectory, "roundtrip.xlsx");
        await ExportAsync(store, sourcePath);
        using (var workbook = new XLWorkbook(sourcePath))
        {
            CollectionAssert.AreEqual(ExpectedSheetNames, workbook.Worksheets.Select(value => value.Name).ToArray());
            Assert.AreEqual(XLWorksheetVisibility.VeryHidden, workbook.Worksheet("__Sushi81Meta").Visibility);
            Assert.IsFalse(workbook.Worksheets.Any(value => value.Name.Equals("Categories", StringComparison.OrdinalIgnoreCase)));

            var products = workbook.Worksheet("Products");
            var groups = workbook.Worksheet("OptionGroups");
            var options = workbook.Worksheet("Options");
            Assert.IsTrue(products.Protection.IsProtected);
            Assert.IsTrue(groups.Protection.IsProtected);
            Assert.IsTrue(options.Protection.IsProtected);
            Assert.IsTrue(products.Column(10).IsHidden);
            Assert.IsTrue(products.Column(11).IsHidden);
            Assert.IsTrue(groups.Column(9).IsHidden);
            Assert.IsTrue(groups.Column(10).IsHidden);
            Assert.IsTrue(options.Column(8).IsHidden);
            Assert.IsTrue(options.Column(9).IsHidden);
            Assert.IsTrue(options.Column(10).IsHidden);
            Assert.AreEqual("PL", products.Cell(FindRow(products, 1, "P-1"), 4).GetString());
            Assert.AreEqual(string.Empty, products.Cell(FindRow(products, 1, "P-2"), 4).GetString());
            Assert.AreEqual($"product:{activeId:N}", products.Cell(FindRow(products, 1, "P-1"), 10).GetString());
            Assert.IsTrue(groups.CellsUsed().Any(value => value.GetString() == $"product:{activeId:N}"));
            Assert.IsTrue(groups.CellsUsed().Any(value => value.GetString().StartsWith("group:", StringComparison.Ordinal)));
            Assert.IsTrue(options.CellsUsed().Any(value => value.GetString() == $"product:{activeId:N}"));
            Assert.IsTrue(options.CellsUsed().Any(value => value.GetString().StartsWith("group:", StringComparison.Ordinal)));
            Assert.IsTrue(options.CellsUsed().Any(value => value.GetString().StartsWith("option:", StringComparison.Ordinal)));
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
        Assert.AreEqual(0, preview.Preview.ProductCreateCount);
        Assert.AreEqual(0, preview.Preview.ProductModifyCount);
        Assert.AreEqual(0, preview.Preview.ProductActivateCount);
        Assert.AreEqual(0, preview.Preview.ProductDeactivateCount);
        Assert.AreEqual(0, preview.Preview.OptionGroupCreateCount);
        Assert.AreEqual(0, preview.Preview.OptionGroupModifyCount);
        Assert.AreEqual(0, preview.Preview.OptionCreateCount);
        Assert.AreEqual(0, preview.Preview.OptionModifyCount);
        Assert.AreEqual(0, preview.Preview.OptionActivateCount);
        Assert.AreEqual(0, preview.Preview.OptionDeactivateCount);

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
        var active = reread.Products.Single(value => value.Id == activeId);
        Assert.IsTrue(active.IsActive);
        Assert.HasCount(2, active.OptionGroups);
        Assert.AreEqual(30, active.OptionGroups.Single(value => value.Name == "Extras").DisplayOrder);
        Assert.AreEqual(70, active.OptionGroups.Single(value => value.Name == "Sauces").DisplayOrder);
        Assert.IsFalse(active.OptionGroups.Single(value => value.Name == "Extras").Options.Single(value => value.Name == "Avocado").IsActive);
        Assert.AreEqual(40, active.OptionGroups.Single(value => value.Name == "Extras").Options.Single(value => value.Name == "Avocado").DisplayOrder);
        Assert.AreEqual(active.Id, active.OptionGroups.Single(value => value.Name == "Sauces").ProductId);

        var secondPath = Path.Combine(paths.TempDirectory, "roundtrip-again.xlsx");
        await ExportAsync(store, secondPath);
        await using var second = File.OpenRead(secondPath);
        var reparsed = await gateway.ReadAsync(second, secondPath);
        Assert.IsFalse(reparsed.HasErrors, string.Join(";", reparsed.Issues.Select(issue => issue.Code)));
        CollectionAssert.AreEquivalent(ExpectedProductCodes, reparsed.Products.Select(value => value.ProductCode).ToArray());
        Assert.AreEqual("PL", reparsed.Products.Single(value => value.ProductCode == "P-1").CategoryShortCode);
        Assert.IsTrue(string.IsNullOrEmpty(reparsed.Products.Single(value => value.ProductCode == "P-2").CategoryShortCode));
        Assert.HasCount(2, reparsed.OptionGroups);
        Assert.HasCount(3, reparsed.Options);
        Assert.AreEqual("SINGLE", reparsed.OptionGroups.Single(value => value.GroupName == "Sauces").SelectionMode);
        Assert.IsFalse(reparsed.Options.Single(value => value.OptionName == "Avocado").IsActive!.Value);
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
        var reassignmentCategory = (await catalogue.CreateCategoryWithCodeAsync("Desserts", "DE")).Value!;
        var existingId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-1", "Original", category.Id, Money.FromCents(100), 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Single, false, null, null, 1,
                [
                    new OptionDraft(Guid.Empty, "Sauce", Money.FromCents(25), true, 2),
                    new OptionDraft(Guid.Empty, "Sesame", Money.Zero, false, 3)
                ])]))).Value!;
        var omittedId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-OMIT", "Omitted", category.Id, Money.FromCents(50), 20m, true, false, false, []))).Value!;
        var retainedId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "P-RETAIN", "Retained", category.Id, Money.FromCents(75), 5.5m, true, false, true,
            [new OptionGroupDraft(Guid.Empty, "Omitted group", SelectionMode.Multi, false, 0, 1, 6,
                [new OptionDraft(Guid.Empty, "Omitted option", Money.FromCents(10), true, 7)])]))).Value!;
        var original = (await store.GetProductForEditAsync(existingId))!;
        var originalGroup = original.Groups.Single();
        var originalSauce = originalGroup.Options.Single(value => value.Name == "Sauce");
        var originalSesame = originalGroup.Options.Single(value => value.Name == "Sesame");
        var retained = (await store.GetProductForEditAsync(retainedId))!;
        var omittedGroup = retained.Groups.Single();
        var omittedOption = omittedGroup.Options.Single();

        var sourcePath = Path.Combine(paths.TempDirectory, "edited.xlsx");
        await ExportAsync(store, sourcePath);
        using (var workbook = new XLWorkbook(sourcePath))
        {
            var products = workbook.Worksheet("Products");
            var existingRow = FindRow(products, 1, "P-1");
            products.Cell(existingRow, 1).Value = "P-EDITED";
            products.Cell(existingRow, 2).Value = "Edited";
            products.Cell(existingRow, 3).Value = "Desserts";
            products.Cell(existingRow, 4).Value = "DE";
            products.Cell(existingRow, 5).Value = 2.50m;
            products.Cell(existingRow, 6).Value = 10m;
            products.Cell(existingRow, 7).Value = false;
            products.Cell(existingRow, 8).Value = false;
            products.Cell(existingRow, 9).Value = false;
            products.Row(FindRow(products, 1, "P-OMIT")).Delete();
            var newRow = products.LastRowUsed()!.RowNumber() + 1;
            products.Cell(newRow, 1).Value = "P-NEW";
            products.Cell(newRow, 2).Value = "New product";
            products.Cell(newRow, 3).Value = "Seasonals";
            products.Cell(newRow, 4).Value = "SE";
            products.Cell(newRow, 5).Value = 4.50m;
            products.Cell(newRow, 6).Value = 20m;
            products.Cell(newRow, 7).Value = true;
            products.Cell(newRow, 8).Value = false;
            products.Cell(newRow, 9).Value = true;

            var groups = workbook.Worksheet("OptionGroups");
            var existingGroupRow = FindRow(groups, 3, "Extras");
            groups.Cell(existingGroupRow, 1).Value = "P-EDITED";
            groups.Cell(existingGroupRow, 2).Value = "Edited";
            groups.Cell(existingGroupRow, 3).Value = "Edited extras";
            groups.Cell(existingGroupRow, 4).Value = "MULTI";
            groups.Cell(existingGroupRow, 5).Value = true;
            groups.Cell(existingGroupRow, 6).Value = 1;
            groups.Cell(existingGroupRow, 7).Value = 2;
            groups.Cell(existingGroupRow, 8).Value = 5;
            groups.Row(FindRow(groups, 3, "Omitted group")).Delete();
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
            var sauceRow = FindRow(options, 4, "Sauce");
            options.Cell(sauceRow, 1).Value = "P-EDITED";
            options.Cell(sauceRow, 2).Value = "Edited";
            options.Cell(sauceRow, 3).Value = "Edited extras";
            options.Cell(sauceRow, 4).Value = "Edited sauce";
            options.Cell(sauceRow, 5).Value = 1.25m;
            options.Cell(sauceRow, 6).Value = true;
            options.Cell(sauceRow, 7).Value = 3;
            var sesameRow = FindRow(options, 4, "Sesame");
            options.Cell(sesameRow, 1).Value = "P-EDITED";
            options.Cell(sesameRow, 2).Value = "Edited";
            options.Cell(sesameRow, 3).Value = "Edited extras";
            options.Cell(sesameRow, 4).Value = "Edited sesame";
            options.Cell(sesameRow, 5).Value = 0.50m;
            options.Cell(sesameRow, 6).Value = true;
            options.Cell(sesameRow, 7).Value = 4;
            options.Row(FindRow(options, 4, "Omitted option")).Delete();
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
        Assert.AreEqual(1, preview.Preview.ProductDeactivateCount);
        Assert.AreEqual(1, preview.Preview.NewCategoryCount);
        Assert.AreEqual(1, preview.Preview.OptionGroupCreateCount);
        Assert.AreEqual(1, preview.Preview.OptionGroupModifyCount);
        Assert.AreEqual(1, preview.Preview.OptionCreateCount);
        Assert.AreEqual(2, preview.Preview.OptionModifyCount);
        Assert.AreEqual(1, preview.Preview.OptionActivateCount);
        Assert.IsNotNull(preview.Plan);
        Assert.HasCount(9, preview.Plan!.Operations);
        Assert.IsFalse(preview.Plan!.Operations.Any(value => string.Equals(value.Kind.ToString(), "Delete", StringComparison.Ordinal)));

        var revisionBefore = await ReadRevisionAsync(factory);
        var committed = await service.CommitAsync(preview);
        Assert.IsTrue(committed.Succeeded, string.Join(";", committed.Issues.Select(issue => issue.Code)));
        Assert.IsTrue(committed.Changed);
        Assert.AreEqual(1, notifier.Calls);
        Assert.AreEqual(revisionBefore + 1, await ReadRevisionAsync(factory));

        var edited = await store.GetProductForEditAsync(existingId);
        Assert.IsNotNull(edited);
        Assert.AreEqual(existingId, edited!.Id);
        Assert.AreEqual("P-EDITED", edited.Code);
        Assert.AreEqual("Edited", edited!.Name);
        Assert.AreEqual(250, edited.PriceTtc.Cents);
        Assert.AreEqual(10m, edited.VatRate);
        Assert.IsFalse(edited.IsActive);
        Assert.IsFalse(edited.DiscountEligible);
        Assert.IsFalse(edited.OptionsEnabled);
        Assert.AreEqual(reassignmentCategory.Id, edited.CategoryId);
        var editedGroup = edited.Groups.Single();
        Assert.AreEqual(originalGroup.Id, editedGroup.Id);
        Assert.AreEqual("Edited extras", editedGroup.Name);
        Assert.AreEqual(SelectionMode.Multi, editedGroup.SelectionMode);
        Assert.IsTrue(editedGroup.IsRequired);
        Assert.AreEqual(1, editedGroup.MinSelections);
        Assert.AreEqual(2, editedGroup.MaxSelections);
        Assert.AreEqual(5, editedGroup.DisplayOrder);
        var editedSauce = editedGroup.Options.Single(value => value.Id == originalSauce.Id);
        Assert.AreEqual("Edited sauce", editedSauce.Name);
        Assert.AreEqual(125, editedSauce.PriceAdjustmentTtc.Cents);
        Assert.IsTrue(editedSauce.IsActive);
        Assert.AreEqual(3, editedSauce.DisplayOrder);
        var editedSesame = editedGroup.Options.Single(value => value.Id == originalSesame.Id);
        Assert.AreEqual("Edited sesame", editedSesame.Name);
        Assert.AreEqual(50, editedSesame.PriceAdjustmentTtc.Cents);
        Assert.IsTrue(editedSesame.IsActive);
        Assert.AreEqual(4, editedSesame.DisplayOrder);
        var allProducts = await store.ListProductsAsync();
        var omitted = allProducts.Single(value => value.Id == omittedId);
        Assert.AreEqual("P-OMIT", omitted.Code);
        Assert.AreEqual(50, omitted.PriceTtc.Cents);
        var retainedAfter = await store.GetProductForEditAsync(retainedId);
        Assert.IsNotNull(retainedAfter);
        Assert.AreEqual(omittedGroup.Id, retainedAfter!.Groups.Single().Id);
        Assert.AreEqual("Omitted group", retainedAfter.Groups.Single().Name);
        Assert.AreEqual(omittedOption.Id, retainedAfter.Groups.Single().Options.Single().Id);
        Assert.AreEqual("Omitted option", retainedAfter.Groups.Single().Options.Single().Name);
        var added = allProducts.Single(value => value.Code == "P-NEW");
        Assert.AreNotEqual(Guid.Empty, added.Id);
        var addedDraft = await store.GetProductForEditAsync(added.Id);
        Assert.IsNotNull(addedDraft);
        var seasonals = (await store.ListCategoriesAsync()).Single(value => value.Name == "Seasonals");
        Assert.AreEqual(seasonals.Id, addedDraft!.CategoryId);
        Assert.AreEqual("SE", seasonals.ShortCode);
        Assert.HasCount(1, addedDraft.Groups);
        Assert.AreNotEqual(Guid.Empty, addedDraft.Groups[0].Id);
        Assert.AreEqual("Toppings", addedDraft.Groups[0].Name);
        Assert.HasCount(1, addedDraft.Groups[0].Options);
        Assert.AreNotEqual(Guid.Empty, addedDraft.Groups[0].Options[0].Id);
        Assert.AreEqual("Sesame", addedDraft.Groups[0].Options[0].Name);

        var finalPath = Path.Combine(paths.TempDirectory, "edited-final.xlsx");
        await ExportAsync(store, finalPath);
        await using var finalStream = File.OpenRead(finalPath);
        var finalWorkbook = await gateway.ReadAsync(finalStream, finalPath);
        Assert.IsFalse(finalWorkbook.HasErrors, string.Join(";", finalWorkbook.Issues.Select(value => value.Code)));
        Assert.IsTrue(finalWorkbook.Products.Any(value => value.ProductCode == "P-EDITED"));
        Assert.IsFalse(finalWorkbook.Products.Any(value => value.ProductCode == "P-1"));
        Assert.IsTrue(finalWorkbook.Products.Any(value => value.ProductCode == "P-OMIT"));
        Assert.IsTrue(finalWorkbook.OptionGroups.Any(value => value.GroupName == "Edited extras"));
        Assert.IsTrue(finalWorkbook.OptionGroups.Any(value => value.GroupName == "Omitted group"));
        Assert.IsTrue(finalWorkbook.Options.Any(value => value.OptionName == "Edited sesame" && value.IsActive == true));
        Assert.IsTrue(finalWorkbook.Options.Any(value => value.OptionName == "Omitted option"));
    }

    [TestMethod]
    public async Task RealFileSortReorderKeepsAllProductionBindingsAndProducesNoOperations()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var firstId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-Z", "Zulu", category.Id, Money.FromCents(100), 20m, true, false, true,
            [
                new OptionGroupDraft(Guid.Empty, "Z group", SelectionMode.Multi, false, 0, 1, 4, [new OptionDraft(Guid.Empty, "Z option", Money.Zero, true, 8)]),
                new OptionGroupDraft(Guid.Empty, "A group", SelectionMode.Single, false, null, null, 2, [new OptionDraft(Guid.Empty, "A option", Money.FromCents(25), true, 6)])
            ]))).Value!;
        var secondId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-A", "Alpha", category.Id, Money.FromCents(200), 5.5m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "B group", SelectionMode.Multi, false, 0, 1, 1, [new OptionDraft(Guid.Empty, "B option", Money.FromCents(10), true, 3)])]))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();

        var path = Path.Combine(paths.TempDirectory, "sorted.xlsx");
        await ExportAsync(store, path);
        using (var workbook = new XLWorkbook(path))
        {
            SortCompleteVisibleRows(workbook.Worksheet("Products"), 11, 2);
            SortCompleteVisibleRows(workbook.Worksheet("OptionGroups"), 12, 3);
            SortCompleteVisibleRows(workbook.Worksheet("Options"), 12, 4);
            workbook.Save();
        }

        using (var workbook = new XLWorkbook(path))
        {
            var zProduct = baseline.Products.Single(value => value.Id == firstId);
            var zGroup = zProduct.OptionGroups.Single(value => value.Name == "Z group");
            var zOption = zGroup.Options.Single(value => value.Name == "Z option");
            var products = workbook.Worksheet("Products");
            var groups = workbook.Worksheet("OptionGroups");
            var options = workbook.Worksheet("Options");
            Assert.AreEqual($"product:{firstId:N}", products.Cell(FindRow(products, 1, "P-Z"), 10).GetString());
            var zGroupRow = FindRow(groups, 3, "Z group");
            Assert.AreEqual($"product:{firstId:N}", groups.Cell(zGroupRow, 9).GetString());
            Assert.AreEqual($"group:{zGroup.Id:N}", groups.Cell(zGroupRow, 10).GetString());
            var zOptionRow = FindRow(options, 4, "Z option");
            Assert.AreEqual($"product:{firstId:N}", options.Cell(zOptionRow, 8).GetString());
            Assert.AreEqual($"group:{zGroup.Id:N}", options.Cell(zOptionRow, 9).GetString());
            Assert.AreEqual($"option:{zOption.Id:N}", options.Cell(zOptionRow, 10).GetString());
        }

        var gateway = new ClosedXmlCatalogueWorkbookImportGateway();
        var service = new CatalogueImportService(gateway, store, store,
            new WriteAuthorityGuard(WriteAuthorityState.Authoritative), new RecordingNotifier());
        await using var source = File.OpenRead(path);
        var preview = await service.PreviewAsync(source, CatalogueImportMode.Update, path);

        Assert.IsFalse(preview.HasErrors, string.Join(";", preview.Preview.Issues.Select(value => value.Code)));
        Assert.IsNotNull(preview.Plan);
        Assert.IsEmpty(preview.Plan!.Operations);
        Assert.IsTrue(baseline.Products.Any(value => value.Id == firstId));
        Assert.IsTrue(baseline.Products.Any(value => value.Id == secondId));
        foreach (var product in baseline.Products)
        foreach (var group in product.OptionGroups)
        {
            Assert.AreEqual(product.Id, group.ProductId);
            foreach (var option in group.Options) Assert.AreEqual(group.Id, option.OptionGroupId);
        }
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

    private static int FindRow(IXLWorksheet worksheet, int column, string value)
    {
        var last = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        return Enumerable.Range(2, Math.Max(0, last - 1)).Single(row =>
            string.Equals(worksheet.Cell(row, column).GetString(), value, StringComparison.Ordinal));
    }

    private static void SortCompleteVisibleRows(IXLWorksheet worksheet, int lastColumn, int sortColumn)
    {
        var last = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        if (last > 2) worksheet.Range(2, 1, last, lastColumn).Sort(sortColumn, XLSortOrder.Ascending);
    }

    private static Task SetGroupDisplayOrderAsync(SqliteConnectionFactory factory, Guid id, int displayOrder) =>
        SetDisplayOrderAsync(factory, "UPDATE option_groups SET display_order=$order WHERE option_group_id=$id;", id, displayOrder);

    private static Task SetOptionDisplayOrderAsync(SqliteConnectionFactory factory, Guid id, int displayOrder) =>
        SetDisplayOrderAsync(factory, "UPDATE options SET display_order=$order WHERE option_id=$id;", id, displayOrder);

    private static async Task SetDisplayOrderAsync(SqliteConnectionFactory factory, string commandText, Guid id, int displayOrder)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$order", displayOrder);
        command.Parameters.AddWithValue("$id", id.ToString());
        Assert.AreEqual(1, await command.ExecuteNonQueryAsync());
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
