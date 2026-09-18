using ClosedXML.Excel;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M10Wp3CatalogueImportCommitTests
{
    private static readonly string[] SwappedGroupNames = ["Second group", "First group"];
    private static readonly string[] SwappedOptionNames = ["Two", "One"];
    [TestMethod]
    public async Task CommitUpdatesExactEntitiesOnceAndNotifiesAfterCommit()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Original", category.Id, Money.FromCents(100), 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Multi, false, 0, 2, 0, [new OptionDraft(Guid.Empty, "Sauce", Money.Zero, true, 0)])]))).Value!;

        var exported = await store.ReadCatalogueWorkbookSnapshotAsync();
        await using var workbookStream = new MemoryStream();
        await new ClosedXmlCatalogueWorkbookGateway().WriteAsync(new CatalogueWorkbookExport(exported, Guid.NewGuid()), workbookStream);
        workbookStream.Position = 0;
        var workbook = new ClosedXmlCatalogueWorkbookImportGateway();
        var parsed = await workbook.ReadAsync(workbookStream);
        var preview = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, parsed, await store.ReadCatalogueImportBaselineAsync());
        Assert.AreEqual(0, preview.Preview.ErrorCount, string.Join(";", preview.Preview.Issues.Select(issue => issue.Code)));

        using var editedWorkbook = new XLWorkbook(new MemoryStream(workbookStream.ToArray()));
        editedWorkbook.Worksheet("Products").Cell(2, 2).Value = "Updated";
        editedWorkbook.Worksheet("Products").Cell(2, 5).Value = 2.50m;
        await using var changedStream = new MemoryStream();
        editedWorkbook.SaveAs(changedStream);
        changedStream.Position = 0;
        var changedWorkbook = await workbook.ReadAsync(changedStream);
        Assert.AreEqual("Updated", changedWorkbook.Products.Single(value => value.ProductId == productId.ToString("D")).ProductName);
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var planned = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, changedWorkbook, baseline);
        Assert.AreEqual(0, planned.Preview.ErrorCount, string.Join(";", planned.Preview.Issues.Select(issue => issue.Code)));
        Assert.IsGreaterThan(0, planned.Preview.ProductModifyCount, $"ops={planned.Plan?.Operations.Count}; names={string.Join(",", planned.Plan?.Operations.Select(value => value.Kind) ?? [])}");
        planned = planned with { PreviewBaseline = baseline };

        var notifier = new RecordingNotifier();
        var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new CatalogueImportService(workbook, store, store, guard, notifier);
        var committed = await service.CommitAsync(planned);
        Assert.IsTrue(committed.Succeeded, $"preview={planned.Preview.ProductModifyCount}; " + string.Join(";", committed.Issues.Select(issue => issue.Code)));
        Assert.IsTrue(committed.Changed);
        Assert.AreEqual(1, notifier.Calls);
        Assert.AreEqual("Updated", (await store.GetProductForEditAsync(productId))!.Name);
        Assert.AreEqual(250, (await store.GetProductForEditAsync(productId))!.PriceTtc.Cents);
    }

    [TestMethod]
    public async Task MidBatchFailureRollsBackRowsAndRevision()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new(2, "P-1", "New", "Plats", null, Money.Zero, 20m, true, false, false, null, null)], [], [], [], [], "synthetic");
        var preview = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);
        Assert.IsNotNull(preview.Plan);
        store.ImportWriteFailureInjector = _ => new InvalidOperationException("synthetic mid-batch failure");
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(preview.Plan!, CatalogueImportBaseline.Empty));
        Assert.IsFalse(result.Succeeded);
        Assert.IsEmpty(await store.ListProductsAsync());
        Assert.IsEmpty(await store.ListCategoriesAsync());
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand(); command.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        var revision = await command.ExecuteScalarAsync();
        Assert.IsTrue(revision is null or DBNull || Convert.ToString(revision, System.Globalization.CultureInfo.InvariantCulture) == "0");
    }

    [TestMethod]
    public async Task AddOnlyFirstInitializationCreatesCompleteHierarchyAtomically()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new(2, "P-1", "New", "Plats", null, Money.FromCents(100), 20m, true, false, true, null, null)],
            [new(2, "P-1", "New", "Extras", "MULTI", false, 0, 1, 0, null, null, null, null)],
            [new(2, "P-1", "New", "Extras", "Sauce", Money.Zero, true, 0, null, null, null, null, null)], [], [], "synthetic");
        var preview = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);
        Assert.IsNotNull(preview.Plan, string.Join(";", preview.Preview.Issues.Select(issue => issue.Code)));
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(preview.Plan!, CatalogueImportBaseline.Empty));
        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.HasCount(1, await store.ListCategoriesAsync());
        Assert.HasCount(1, await store.ListProductsAsync());
        var product = (await store.ListProductsAsync()).Single();
        var draft = await store.GetProductForEditAsync(product.Id);
        Assert.IsNotNull(draft);
        Assert.HasCount(1, draft!.Groups);
        Assert.HasCount(1, draft.Groups[0].Options);
    }

    [TestMethod]
    public async Task TransactionRunnerCommitFailureRollsBackImportAndDoesNotNotify()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var runner = new SqliteTransactionRunner(factory, () => new InvalidOperationException("synthetic commit failure"));
        var store = new SqliteCatalogueStore(factory, runner, new DeterministicIds(), clock);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new(2, "P-1", "New", "Plats", null, Money.Zero, 20m, true, false, false, null, null)], [], [], [], [], "synthetic");
        var preview = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(preview.Plan!, CatalogueImportBaseline.Empty));
        Assert.IsFalse(result.Succeeded);
        Assert.IsEmpty(await store.ListProductsAsync());
        Assert.IsEmpty(await store.ListCategoriesAsync());
    }

    [TestMethod]
    public async Task MismatchedPreviewBaselineTokenBlocksBeforeAnyWrite()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var plan = new CatalogueImportPlan(CatalogueImportMode.Update, [], [], [], "synthetic");

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(plan, CatalogueImportBaseline.Empty, "tampered-baseline-token"));

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(result.Issues.Select(issue => issue.Code).ToArray(), "baseline-token-mismatch");
        Assert.IsEmpty(await store.ListProductsAsync());
        Assert.IsEmpty(await store.ListCategoriesAsync());
    }

    [TestMethod]
    public async Task ChangedLiveBaselineIsRejectedAsAStableBlockingConflict()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var runner = new SqliteTransactionRunner(factory);
        var store = new SqliteCatalogueStore(factory, runner, new DeterministicIds(), clock);
        var previewBaseline = await store.ReadCatalogueImportBaselineAsync();
        var plan = new CatalogueImportPlan(CatalogueImportMode.Update, [], [], [], "synthetic");
        await new CatalogueService(store).CreateCategoryWithCodeAsync("Plats", "PL");

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(plan, previewBaseline));

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(result.Issues.Select(issue => issue.Code).ToArray(), "stale-baseline");
    }

    [TestMethod]
    public async Task CodeAndDisplayOrderSwapsUseSafeStagingAndPersistExactFinalValues()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var first = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "A", "First", category.Id, Money.FromCents(100), 20m, true, true, true,
            [
                new OptionGroupDraft(Guid.Empty, "First group", SelectionMode.Multi, false, 0, 2, 0,
                    [new OptionDraft(Guid.Empty, "One", Money.Zero, true, 0), new OptionDraft(Guid.Empty, "Two", Money.Zero, true, 1)]),
                new OptionGroupDraft(Guid.Empty, "Second group", SelectionMode.Multi, false, 0, 2, 1, [])]))).Value!;
        var second = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "B", "Second", category.Id, Money.FromCents(200), 20m, true, true, false, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var firstCurrent = baseline.Products.Single(value => value.Id == first);
        var secondCurrent = baseline.Products.Single(value => value.Id == second);
        var firstGroups = firstCurrent.OptionGroups.OrderBy(value => value.DisplayOrder).ToArray();
        var firstOptions = firstGroups[0].Options.OrderBy(value => value.DisplayOrder).ToArray();
        var operations = new List<CatalogueImportOperation>
        {
            ProductModify(firstCurrent, "B"),
            ProductModify(secondCurrent, "A"),
            GroupModify(firstGroups[0], first, "B", 1),
            GroupModify(firstGroups[1], first, "B", 0),
            OptionModify(firstOptions[0], firstGroups[0].Id, 1),
            OptionModify(firstOptions[1], firstGroups[0].Id, 0),
        };
        var plan = new CatalogueImportPlan(CatalogueImportMode.Update, operations, [], [], "synthetic");

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(plan, baseline));

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual("B", (await store.GetProductForEditAsync(first))!.Code);
        Assert.AreEqual("A", (await store.GetProductForEditAsync(second))!.Code);
        var saved = await store.GetProductForEditAsync(first);
        Assert.IsNotNull(saved);
        CollectionAssert.AreEqual(SwappedGroupNames, saved!.Groups.Select(value => value.Name).ToArray());
        CollectionAssert.AreEqual(SwappedOptionNames, saved.Groups.Single(value => value.Name == "First group").Options.Select(value => value.Name).ToArray());
    }

    private static CatalogueImportOperation ProductModify(CatalogueImportBaselineProduct product, string code) =>
        new(CatalogueImportEntityType.Product, CatalogueImportOperationKind.Modify, product.Id, $"product:{product.Id:N}", 2, "Products",
            new Dictionary<string, string?>
            {
                ["code"] = code, ["name"] = product.Name, ["category"] = product.CategoryName, ["categoryShortCode"] = product.CategoryShortCode,
                ["priceCents"] = product.PriceTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture), ["vatRate"] = product.VatRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["isActive"] = product.IsActive.ToString(), ["discountEligible"] = product.DiscountEligible.ToString(), ["optionsEnabled"] = product.OptionsEnabled.ToString()
            },
            CatalogueImportEntityReference.Existing(product.Id, $"product:{product.Id:N}"),
            CatalogueImportEntityReference.Existing(product.CategoryId, $"category:{product.CategoryId:N}"));

    private static CatalogueImportOperation GroupModify(CatalogueImportBaselineOptionGroup group, Guid productId, string productCode, int order) =>
        new(CatalogueImportEntityType.OptionGroup, CatalogueImportOperationKind.Modify, group.Id, $"group:{group.Id:N}", 2, "OptionGroups",
            new Dictionary<string, string?>
            {
                ["productCode"] = productCode, ["name"] = group.Name, ["selectionMode"] = group.SelectionMode.ToString(), ["isRequired"] = group.IsRequired.ToString(),
                ["minSelections"] = group.MinSelections?.ToString(System.Globalization.CultureInfo.InvariantCulture), ["maxSelections"] = group.MaxSelections?.ToString(System.Globalization.CultureInfo.InvariantCulture), ["displayOrder"] = order.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            CatalogueImportEntityReference.Existing(group.Id, $"group:{group.Id:N}"),
            ParentReference: CatalogueImportEntityReference.Existing(productId, $"product:{productId:N}"));

    private static CatalogueImportOperation OptionModify(CatalogueImportBaselineOption option, Guid groupId, int order) =>
        new(CatalogueImportEntityType.Option, CatalogueImportOperationKind.Modify, option.Id, $"option:{option.Id:N}", 2, "Options",
            new Dictionary<string, string?>
            {
                ["name"] = option.Name, ["priceAdjustmentCents"] = option.PriceAdjustmentTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture), ["isActive"] = option.IsActive.ToString(), ["displayOrder"] = order.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            CatalogueImportEntityReference.Existing(option.Id, $"option:{option.Id:N}"),
            ParentReference: CatalogueImportEntityReference.Existing(groupId, $"group:{groupId:N}"));

    [TestMethod]
    public async Task CatalogueImportLeavesHistoricalOrderSnapshotAndReprintDataUnchanged()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var runner = new SqliteTransactionRunner(factory);
        var store = new SqliteCatalogueStore(factory, runner, ids, clock);
        var catalogue = new CatalogueService(store);
        var sourceCategory = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var targetCategory = (await catalogue.CreateCategoryWithCodeAsync("Desserts", "DE")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Plat initial", sourceCategory.Id, Money.FromCents(1250), 10m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Single, false, null, null, 0,
                [new OptionDraft(Guid.Empty, "Sauce initiale", Money.FromCents(25), true, 0)])]))).Value!;

        var selected = (await new OrderEntryCatalogueService(store).GetActiveProductAsync(productId))!;
        var selectedOptionId = selected.Aggregate.OptionsByGroup.Values.Single().Single().Id;
        using var orderService = new OrderEntryService(new OrderEntryCatalogueService(store),
            new Sushi81.Pos.Infrastructure.Settings.SqliteBusinessSettingsStore(factory, runner, clock),
            new Sushi81.Pos.Infrastructure.Order.SqliteOrderStore(factory, runner),
            new NoopDispatcher(), ids, clock);
        var created = await orderService.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, selected.Aggregate, [selectedOptionId], [], 2, selected.CategoryName)],
            FulfilmentMode.Retrait, clock.BusinessDate, new TimeOnly(11, 0), null, null, null, false));
        Assert.IsTrue(created.Succeeded, string.Join(";", created.Issues.Select(issue => issue.Message)));
        var historical = created.CommittedOrder!;
        var historicalPrint = new OrderPrintDocumentFactory(clock).Create(historical, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.ExplicitReprint);

        var exported = await store.ReadCatalogueWorkbookSnapshotAsync();
        await using var exportStream = new MemoryStream();
        await new ClosedXmlCatalogueWorkbookGateway().WriteAsync(new CatalogueWorkbookExport(exported, Guid.NewGuid()), exportStream);
        using var editedWorkbook = new XLWorkbook(new MemoryStream(exportStream.ToArray()));
        editedWorkbook.Worksheet("Products").Cell(2, 1).Value = "P-9";
        editedWorkbook.Worksheet("Products").Cell(2, 2).Value = "Plat modifie";
        editedWorkbook.Worksheet("Products").Cell(2, 3).Value = targetCategory.Name;
        editedWorkbook.Worksheet("Products").Cell(2, 4).Value = targetCategory.ShortCode;
        editedWorkbook.Worksheet("Products").Cell(2, 5).Value = 9.99m;
        editedWorkbook.Worksheet("Products").Cell(2, 6).Value = 20m;
        editedWorkbook.Worksheet("Products").Cell(2, 7).Value = false;
        editedWorkbook.Worksheet("Products").Cell(2, 8).Value = false;
        editedWorkbook.Worksheet("Products").Cell(2, 9).Value = false;
        editedWorkbook.Worksheet("OptionGroups").Cell(2, 1).Value = "P-9";
        editedWorkbook.Worksheet("OptionGroups").Cell(2, 2).Value = "Plat modifie";
        editedWorkbook.Worksheet("OptionGroups").Cell(2, 3).Value = "Extras modifies";
        editedWorkbook.Worksheet("Options").Cell(2, 1).Value = "P-9";
        editedWorkbook.Worksheet("Options").Cell(2, 2).Value = "Plat modifie";
        editedWorkbook.Worksheet("Options").Cell(2, 3).Value = "Extras modifies";
        editedWorkbook.Worksheet("Options").Cell(2, 4).Value = "Sauce modifiee";
        editedWorkbook.Worksheet("Options").Cell(2, 5).Value = 1.50m;
        editedWorkbook.Worksheet("Options").Cell(2, 6).Value = false;
        await using var changedStream = new MemoryStream();
        editedWorkbook.SaveAs(changedStream);
        changedStream.Position = 0;

        var importGateway = new ClosedXmlCatalogueWorkbookImportGateway();
        var changedWorkbook = await importGateway.ReadAsync(changedStream);
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var planned = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, changedWorkbook, baseline);
        Assert.AreEqual(0, planned.Preview.ErrorCount, string.Join(";", planned.Preview.Issues.Select(issue => issue.Code)));
        planned = planned with { PreviewBaseline = baseline };
        var committed = await new CatalogueImportService(importGateway, store, store,
            new WriteAuthorityGuard(WriteAuthorityState.Authoritative), new RecordingNotifier()).CommitAsync(planned);
        Assert.IsTrue(committed.Succeeded, string.Join(";", committed.Issues.Select(issue => issue.Code)));

        var reopened = await new Sushi81.Pos.Infrastructure.Order.SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(historical.Id);
        Assert.IsNotNull(reopened);
        Assert.AreEqual(historical.Items[0].ProductCode, reopened!.Items[0].ProductCode);
        Assert.AreEqual(historical.Items[0].ProductName, reopened.Items[0].ProductName);
        Assert.AreEqual(historical.Items[0].CategoryName, reopened.Items[0].CategoryName);
        Assert.AreEqual(historical.Items[0].ProductBasePriceTtc, reopened.Items[0].ProductBasePriceTtc);
        Assert.AreEqual(historical.Items[0].ProductVatRate, reopened.Items[0].ProductVatRate);
        Assert.AreEqual(historical.Items[0].ProductDiscountEligible, reopened.Items[0].ProductDiscountEligible);
        CollectionAssert.AreEqual(historical.Items[0].Adjustments.ToArray(), reopened.Items[0].Adjustments.ToArray());
        Assert.AreEqual(historical.TotalTtc, reopened.TotalTtc);
        CollectionAssert.AreEqual(historical.TaxBreakdown.ToArray(), reopened.TaxBreakdown.ToArray());
        var reopenedPrint = new OrderPrintDocumentFactory(clock).Create(reopened, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.ExplicitReprint);
        CollectionAssert.AreEqual(historicalPrint.Content.Blocks.ToArray(), reopenedPrint.Content.Blocks.ToArray());

        var current = await store.GetProductForEditAsync(productId);
        Assert.IsNotNull(current);
        Assert.AreEqual("P-9", current!.Code);
        Assert.AreEqual(targetCategory.Id, current.CategoryId);
        Assert.AreEqual(999, current.PriceTtc.Cents);
        Assert.AreEqual(20m, current.VatRate);
        Assert.IsFalse(current.IsActive);
        Assert.IsFalse(current.OptionsEnabled);
        Assert.AreEqual("Extras modifies", current.Groups.Single().Name);
        Assert.AreEqual("Sauce modifiee", current.Groups.Single().Options.Single().Name);
        Assert.AreEqual(150, current.Groups.Single().Options.Single().PriceAdjustmentTtc.Cents);
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class NoopDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
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
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M10.Wp3.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data"); RecoveryDirectory = Path.Combine(RootDirectory, "Recovery"); CacheDirectory = Path.Combine(RootDirectory, "Cache"); LogsDirectory = Path.Combine(RootDirectory, "Logs"); ConfigDirectory = Path.Combine(RootDirectory, "Config"); TempDirectory = Path.Combine(RootDirectory, "Temp"); LiveDatabasePath = Path.Combine(DataDirectory, "live.db"); EnsureInitialized();
        }
        public string RootDirectory { get; } public string DataDirectory { get; } public string RecoveryDirectory { get; } public string CacheDirectory { get; } public string LogsDirectory { get; } public string ConfigDirectory { get; } public string TempDirectory { get; } public string LiveDatabasePath { get; }
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
