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

        var revisionBefore = await ReadRevisionAsync(factory);
        var notifier = new RecordingNotifier();
        var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new CatalogueImportService(workbook, store, store, guard, notifier);
        var committed = await service.CommitAsync(planned);
        Assert.IsTrue(committed.Succeeded, $"preview={planned.Preview.ProductModifyCount}; " + string.Join(";", committed.Issues.Select(issue => issue.Code)));
        Assert.IsTrue(committed.Changed);
        Assert.AreEqual(1, notifier.Calls);
        Assert.AreEqual(revisionBefore + 1, await ReadRevisionAsync(factory));
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
    public async Task UpdateBlankIdRowsAllocateTypedIdsAndBindExactParents()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock);
        var category = (await new CatalogueService(store).CreateCategoryWithCodeAsync("Plats", "PL")).Value!;

        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new(2, "P-new", "New", "Plats", "PL", Money.FromCents(100), 20m, true, true, false, null, null)],
            [new(2, "P-new", "New", "Extras", "MULTI", false, 0, 1, 0, null, null, null, null)],
            [new(2, "P-new", "New", "Extras", "Sauce", Money.FromCents(25), true, 0, null, null, null, null, null)], [], [], "synthetic");
        var preview = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, await store.ReadCatalogueImportBaselineAsync());
        Assert.IsNotNull(preview.Plan, string.Join(";", preview.Preview.Issues.Select(issue => issue.Code)));
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(preview.Plan!, await store.ReadCatalogueImportBaselineAsync()));

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        var product = (await store.ListProductsAsync()).Single();
        var draft = await store.GetProductForEditAsync(product.Id);
        Assert.IsNotNull(draft);
        var group = draft!.Groups.Single();
        var option = group.Options.Single();
        Assert.AreNotEqual(Guid.Empty, product.Id);
        Assert.AreNotEqual(Guid.Empty, group.Id);
        Assert.AreNotEqual(Guid.Empty, option.Id);
        var persisted = await store.ReadCatalogueImportBaselineAsync();
        var persistedGroup = persisted.Products.Single(value => value.Id == product.Id).OptionGroups.Single();
        Assert.AreEqual(product.Id, persistedGroup.ProductId);
        Assert.AreEqual(group.Id, persistedGroup.Options.Single().OptionGroupId);
        Assert.AreEqual(category.Id, product.CategoryId);
        Assert.AreEqual("P-new", product.Code);
        Assert.AreEqual("Sauce", option.Name);
    }

    [TestMethod]
    public async Task ExistingProductCanReferencePlannedCategoryAtomically()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var oldCategory = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Original", oldCategory.Id, Money.FromCents(100), 20m, true, true, false, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var current = baseline.Products.Single(value => value.Id == productId);
        var newCategoryKey = "category:new:desserts";
        var operation = ProductModifyWithCategory(current, "P-1", "Desserts", "DE", CatalogueImportEntityReference.New(newCategoryKey));
        var plan = new CatalogueImportPlan(CatalogueImportMode.Update, [operation], [], [new(newCategoryKey, "Desserts", "DE")], "synthetic");
        var revisionBefore = await ReadRevisionAsync(factory);

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(plan, baseline));

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual(revisionBefore + 1, await ReadRevisionAsync(factory));
        var categories = await store.ListCategoriesAsync();
        Assert.HasCount(2, categories);
        var planned = categories.Single(value => value.Name == "Desserts");
        Assert.AreNotEqual(Guid.Empty, planned.Id);
        Assert.AreEqual("DE", planned.ShortCode);
        Assert.AreEqual("Plats", categories.Single(value => value.Id == oldCategory.Id).Name);
        var saved = await store.GetProductForEditAsync(productId);
        Assert.IsNotNull(saved);
        Assert.AreEqual(productId, saved!.Id);
        Assert.AreEqual(planned.Id, saved.CategoryId);
    }

    [TestMethod]
    public async Task NewGroupUsesExistingProductAndNewOptionUsesExistingGroup()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var ids = new DeterministicIds();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Original", category.Id, Money.Zero, 20m, true, true, true, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var groupKey = "group:new:extras";
        var createGroup = new CatalogueImportOperation(CatalogueImportEntityType.OptionGroup, CatalogueImportOperationKind.Create, null, groupKey, 2, "OptionGroups",
            new Dictionary<string, string?> { ["productCode"] = "P-1", ["name"] = "Extras", ["selectionMode"] = "MULTI", ["isRequired"] = "false", ["minSelections"] = "0", ["maxSelections"] = "1", ["displayOrder"] = "0" },
            CatalogueImportEntityReference.New(groupKey), ParentReference: CatalogueImportEntityReference.Existing(productId, $"product:{productId:N}"));
        var first = await store.CommitAsync(new CatalogueImportCommitRequest(new CatalogueImportPlan(CatalogueImportMode.Update, [createGroup], [], [], "synthetic"), baseline));
        Assert.IsTrue(first.Succeeded, string.Join(";", first.Issues.Select(issue => issue.Code)));
        var afterGroup = await store.ReadCatalogueImportBaselineAsync();
        var group = afterGroup.Products.Single(value => value.Id == productId).OptionGroups.Single();
        Assert.AreEqual(productId, group.ProductId);
        var optionKey = "option:new:sauce";
        var createOption = new CatalogueImportOperation(CatalogueImportEntityType.Option, CatalogueImportOperationKind.Create, null, optionKey, 2, "Options",
            new Dictionary<string, string?> { ["name"] = "Sauce", ["priceAdjustmentCents"] = "25", ["isActive"] = "true", ["displayOrder"] = "0" },
            CatalogueImportEntityReference.New(optionKey), ParentReference: CatalogueImportEntityReference.Existing(group.Id, $"group:{group.Id:N}"));
        var second = await store.CommitAsync(new CatalogueImportCommitRequest(new CatalogueImportPlan(CatalogueImportMode.Update, [createOption], [], [], "synthetic"), afterGroup));
        Assert.IsTrue(second.Succeeded, string.Join(";", second.Issues.Select(issue => issue.Code)));
        var final = await store.ReadCatalogueImportBaselineAsync();
        var savedGroup = final.Products.Single(value => value.Id == productId).OptionGroups.Single();
        var option = savedGroup.Options.Single();
        Assert.AreNotEqual(Guid.Empty, savedGroup.Id);
        Assert.AreNotEqual(Guid.Empty, option.Id);
        Assert.AreEqual(productId, savedGroup.ProductId);
        Assert.AreEqual(savedGroup.Id, option.OptionGroupId);
        Assert.AreEqual("Original", final.Products.Single(value => value.Id == productId).Name);
    }

    [TestMethod]
    public async Task MidBatchFailureRollsBackCategoryProductGroupAndOptionWrites()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var plan = FullHierarchyCreatePlan();
        var revisionBefore = await ReadRevisionAsync(factory);
        store.ImportWriteFailureInjector = changed => changed >= 4 ? new InvalidOperationException("synthetic complete-hierarchy failure") : null;

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(plan, CatalogueImportBaseline.Empty));

        Assert.IsFalse(result.Succeeded);
        Assert.IsEmpty(await store.ListCategoriesAsync());
        Assert.IsEmpty(await store.ListProductsAsync());
        Assert.AreEqual(revisionBefore, await ReadRevisionAsync(factory));
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM option_groups;";
        Assert.AreEqual(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        command.CommandText = "SELECT COUNT(*) FROM options;";
        Assert.AreEqual(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
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
        var plan = FullHierarchyCreatePlan();
        var revisionBefore = await ReadRevisionAsync(factory);
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(plan, CatalogueImportBaseline.Empty));
        Assert.IsFalse(result.Succeeded);
        Assert.IsEmpty(await store.ListProductsAsync());
        Assert.IsEmpty(await store.ListCategoriesAsync());
        Assert.AreEqual(revisionBefore, await ReadRevisionAsync(factory));
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM option_groups;";
        Assert.AreEqual(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
        command.CommandText = "SELECT COUNT(*) FROM options;";
        Assert.AreEqual(0L, Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public async Task RealSqliteConstraintConflictRollsBackAtomicImport()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Original", category.Id, Money.Zero, 20m, true, true, false, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var current = baseline.Products.Single(value => value.Id == productId);
        store.ImportWriteConstraintInjector = async (sqlite, changed) =>
        {
            if (changed < 1) return;
            await using var command = sqlite.Connection.CreateCommand();
            command.Transaction = sqlite.Transaction;
            command.CommandText = "INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc) VALUES ($id,$code,$normalized,$name,$category,$price,$vat,$active,$discount,$options,$created,$updated);";
            command.Parameters.AddWithValue("$id", productId.ToString());
            command.Parameters.AddWithValue("$code", "P-constraint");
            command.Parameters.AddWithValue("$normalized", "p-constraint");
            command.Parameters.AddWithValue("$name", "Conflict");
            command.Parameters.AddWithValue("$category", category.Id.ToString());
            command.Parameters.AddWithValue("$price", 0);
            command.Parameters.AddWithValue("$vat", "20");
            command.Parameters.AddWithValue("$active", 1);
            command.Parameters.AddWithValue("$discount", 1);
            command.Parameters.AddWithValue("$options", 0);
            command.Parameters.AddWithValue("$created", clock.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$updated", clock.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        };
        var revisionBefore = await ReadRevisionAsync(factory);
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(new CatalogueImportPlan(CatalogueImportMode.Update, [ProductModify(current, "P-1-updated")], [], [], "synthetic"), baseline));

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(result.Issues.Select(issue => issue.Code).ToArray(), "persistence-conflict");
        Assert.AreEqual(revisionBefore, await ReadRevisionAsync(factory));
        Assert.AreEqual("P-1", (await store.GetProductForEditAsync(productId))!.Code);
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
    public async Task NoOpImportDoesNotAdvanceBusinessRevision()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var before = await ReadRevisionAsync(factory);

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(new CatalogueImportPlan(CatalogueImportMode.Update, [], [], [], "synthetic"), baseline));

        Assert.IsTrue(result.Succeeded);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual(before, await ReadRevisionAsync(factory));
    }

    [TestMethod]
    public async Task EmptyOrCollidingGeneratedIdsFailBeforeAnyImportWrite()
    {
        foreach (var ids in new IIdGenerator[] { new EmptyIds(), new FixedIds() })
        {
            using var paths = new TempPaths();
            var clock = new FixedClock();
            var factory = new SqliteConnectionFactory(paths);
            await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
            var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock);
            var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
                [new(2, "P-1", "New", "Plats", "PL", Money.FromCents(100), 20m, true, false, false, null, null)], [], [], [], [], "synthetic");
            var preview = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);
            Assert.IsNotNull(preview.Plan, string.Join(";", preview.Preview.Issues.Select(issue => issue.Code)));

            var result = await store.CommitAsync(new CatalogueImportCommitRequest(preview.Plan!, CatalogueImportBaseline.Empty));

            Assert.IsFalse(result.Succeeded);
            CollectionAssert.Contains(result.Issues.Select(issue => issue.Code).ToArray(), "invalid-allocated-id");
            Assert.IsEmpty(await store.ListProductsAsync());
            Assert.IsEmpty(await store.ListCategoriesAsync());
        }
    }

    [TestMethod]
    public async Task ProductionWriteAuthorityGuardBlocksImportUntilHeldScopeReleases()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        await using var held = await guard.EnterWriteScopeAsync();
        var store = new RecordingImportStore();
        var service = new CatalogueImportService(new NoOpImportGateway(), store, store, guard, new RecordingNotifier());
        var preview = new CatalogueImportResult(
            new CatalogueImportPreview(CatalogueImportMode.Update, "synthetic", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], []),
            new CatalogueImportPlan(CatalogueImportMode.Update, [], [], [], "synthetic"), CatalogueImportBaseline.Empty);

        var commit = service.CommitAsync(preview);
        await Task.Delay(50);
        Assert.AreEqual(0, store.CommitCalls);
        await held.DisposeAsync();

        var result = await commit;
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, store.CommitCalls);
    }

    [TestMethod]
    public async Task ProductionWriteAuthorityTransitionWaitsForCommitAndNotifierScopes()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var store = new BlockingImportStore();
        var notifier = new BlockingNotifier();
        var service = new CatalogueImportService(new NoOpImportGateway(), store, store, guard, notifier);
        var preview = new CatalogueImportResult(
            new CatalogueImportPreview(CatalogueImportMode.Update, "synthetic", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [], []),
            new CatalogueImportPlan(CatalogueImportMode.Update, [], [], [], "synthetic"), CatalogueImportBaseline.Empty);

        var commit = service.CommitAsync(preview);
        await store.Entered.Task;
        var transition = Task.Run(() => guard.SetState(WriteAuthorityState.NonAuthoritativeReadOnly));
        await Task.Delay(50);
        Assert.IsFalse(transition.IsCompleted);

        store.Release.TrySetResult(true);
        await notifier.Entered.Task;
        Assert.IsFalse(transition.IsCompleted);
        notifier.Release.TrySetResult(true);

        var result = await commit;
        await transition;
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
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
    public async Task OmittedRowsDoNotDeleteUnrelatedProductsAndCategoriesRemainPreserved()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var firstCategory = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var secondCategory = (await catalogue.CreateCategoryWithCodeAsync("Desserts", "DE")).Value!;
        var firstId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "First", firstCategory.Id, Money.FromCents(100), 20m, true, true, false, []))).Value!;
        var secondId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-2", "Second", secondCategory.Id, Money.FromCents(200), 20m, true, true, false, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var current = baseline.Products.Single(value => value.Id == firstId);
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(
            new CatalogueImportPlan(CatalogueImportMode.Update, [ProductModify(current, "P-1-updated")], [], [], "synthetic"), baseline));

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.HasCount(2, await store.ListCategoriesAsync());
        Assert.IsNotNull((await store.GetProductForEditAsync(firstId)));
        Assert.AreEqual("P-2", (await store.GetProductForEditAsync(secondId))!.Code);
        Assert.AreEqual(secondCategory.Id, (await store.GetProductForEditAsync(secondId))!.CategoryId);
    }

    [TestMethod]
    public async Task ReparentingExistingGroupIsRejectedWithoutMutation()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var firstId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "First", category.Id, Money.Zero, 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Multi, false, 0, 1, 0, [])]))).Value!;
        var secondId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-2", "Second", category.Id, Money.Zero, 20m, true, true, true, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var group = baseline.Products.Single(value => value.Id == firstId).OptionGroups.Single();
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(new CatalogueImportPlan(CatalogueImportMode.Update,
            [GroupModify(group, secondId, "P-2", 0)], [], [], "synthetic"), baseline));

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code.Contains("parent", StringComparison.OrdinalIgnoreCase)), string.Join(";", result.Issues.Select(issue => issue.Code)));
        var saved = await store.ReadCatalogueImportBaselineAsync();
        Assert.AreEqual(firstId, saved.Products.Single(value => value.Id == firstId).OptionGroups.Single().ProductId);
        Assert.IsEmpty((await store.GetProductForEditAsync(secondId))!.Groups);
    }

    [TestMethod]
    public async Task ReparentingExistingOptionIsRejectedWithoutMutation()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var firstId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "First", category.Id, Money.Zero, 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "First group", SelectionMode.Multi, false, 0, 1, 0, [new OptionDraft(Guid.Empty, "Sauce", Money.Zero, true, 0)])]))).Value!;
        var secondId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-2", "Second", category.Id, Money.Zero, 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Second group", SelectionMode.Multi, false, 0, 1, 0, [])]))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var option = baseline.Products.Single(value => value.Id == firstId).OptionGroups.Single().Options.Single();
        var secondGroup = baseline.Products.Single(value => value.Id == secondId).OptionGroups.Single();
        var operation = OptionModify(option, secondGroup.Id, 0) with { ParentReference = CatalogueImportEntityReference.Existing(secondGroup.Id, $"group:{secondGroup.Id:N}") };
        var revisionBefore = await ReadRevisionAsync(factory);
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(new CatalogueImportPlan(CatalogueImportMode.Update, [operation], [], [], "synthetic"), baseline));

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(result.Issues.Select(issue => issue.Code).ToArray(), "reparent-forbidden");
        Assert.AreEqual(revisionBefore, await ReadRevisionAsync(factory));
        var after = await store.ReadCatalogueImportBaselineAsync();
        var firstSaved = after.Products.Single(value => value.Id == firstId).OptionGroups.Single();
        Assert.AreEqual(firstSaved.Id, firstSaved.Options.Single().OptionGroupId);
        Assert.IsEmpty(after.Products.Single(value => value.Id == secondId).OptionGroups.Single().Options);
        Assert.AreEqual("Sauce", firstSaved.Options.Single().Name);
    }

    [TestMethod]
    public async Task OmittedProductGroupAndOptionRowsAreAllPreserved()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var firstId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "First", category.Id, Money.FromCents(100), 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "First group", SelectionMode.Multi, false, 0, 1, 0, [new OptionDraft(Guid.Empty, "First option", Money.Zero, true, 0)])]))).Value!;
        var secondId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-2", "Second", category.Id, Money.FromCents(200), 20m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Second group", SelectionMode.Multi, false, 0, 1, 0, [new OptionDraft(Guid.Empty, "Second option", Money.Zero, true, 0)])]))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var first = baseline.Products.Single(value => value.Id == firstId);
        var firstGroup = first.OptionGroups.Single();
        var firstOption = firstGroup.Options.Single();
        var plan = new CatalogueImportPlan(CatalogueImportMode.Update,
            [ProductModify(first, "P-1-updated")], [], [], "synthetic");

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(plan, baseline));

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        var after = await store.ReadCatalogueImportBaselineAsync();
        Assert.HasCount(2, after.Products);
        var omittedProduct = after.Products.Single(value => value.Id == secondId);
        Assert.AreEqual("P-2", omittedProduct.Code);
        Assert.AreEqual(category.Id, omittedProduct.CategoryId);
        var omittedGroup = omittedProduct.OptionGroups.Single();
        Assert.AreEqual("Second group", omittedGroup.Name);
        var omittedOption = omittedGroup.Options.Single();
        Assert.AreEqual("Second option", omittedOption.Name);
        Assert.AreEqual(omittedGroup.Id, omittedOption.OptionGroupId);
        Assert.AreEqual("Plats", after.Categories.Single(value => value.Id == category.Id).Name);
        Assert.AreEqual("PL", after.Categories.Single(value => value.Id == category.Id).ShortCode);
    }

    [TestMethod]
    public async Task ConcurrentWriteAttemptAfterBaselineReadReturnsStableConflictWithoutPartialImport()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), new DeterministicIds(), clock);
        var catalogue = new CatalogueService(store);
        var category = (await catalogue.CreateCategoryWithCodeAsync("Plats", "PL")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Original", category.Id, Money.FromCents(100), 20m, true, true, false, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var current = baseline.Products.Single(value => value.Id == productId);
        string? writerOutcome = null;
        store.ImportCommitAfterBaselineReadAsync = async token =>
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = paths.LiveDatabasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
                Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Private,
                Pooling = false,
                DefaultTimeout = 5
            }.ToString());
            await connection.OpenAsync(token);
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE products SET name='Concurrent writer' WHERE product_id=$id;";
            command.Parameters.AddWithValue("$id", productId.ToString());
            try
            {
                await command.ExecuteNonQueryAsync(token);
                writerOutcome = "committed";
            }
            catch (Microsoft.Data.Sqlite.SqliteException exception)
            {
                writerOutcome = $"blocked:{exception.SqliteErrorCode}";
                throw;
            }
        };

        var result = await store.CommitAsync(new CatalogueImportCommitRequest(
            new CatalogueImportPlan(CatalogueImportMode.Update, [ProductModify(current, "P-2")], [], [], "synthetic"), baseline));

        Assert.IsFalse(result.Succeeded);
        CollectionAssert.Contains(result.Issues.Select(issue => issue.Code).ToArray(), "concurrent-write-conflict");
        StringAssert.StartsWith(writerOutcome, "blocked:5");
        Assert.AreEqual("Original", (await store.GetProductForEditAsync(productId))!.Name);
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
                    [new OptionDraft(Guid.Empty, "One", Money.Zero, true, 0), new OptionDraft(Guid.Empty, "Two", Money.Zero, true, 1), new OptionDraft(Guid.Empty, "Edge", Money.Zero, true, 1000000)]),
                new OptionGroupDraft(Guid.Empty, "Second group", SelectionMode.Multi, false, 0, 2, 1, [])]))).Value!;
        var second = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "B", "Second", category.Id, Money.FromCents(200), 20m, true, true, false, []))).Value!;
        var tempLikeCode = "__sushi81_import_tmp_" + first.ToString("N") + "_0";
        var tempLike = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, tempLikeCode, "Temp-like", category.Id, Money.FromCents(300), 20m, true, true, false, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var firstCurrent = baseline.Products.Single(value => value.Id == first);
        var secondCurrent = baseline.Products.Single(value => value.Id == second);
        var firstGroups = firstCurrent.OptionGroups.OrderBy(value => value.DisplayOrder).ToArray();
        var firstOptions = firstGroups[0].Options.OrderBy(value => value.DisplayOrder).ToArray();
        await using (var edgeConnection = await factory.OpenLiveConnectionAsync())
        {
            await using var edgeCommand = edgeConnection.CreateCommand();
            edgeCommand.CommandText = "UPDATE option_groups SET display_order=1000000 WHERE option_group_id=$id;";
            edgeCommand.Parameters.AddWithValue("$id", firstGroups[1].Id.ToString());
            await edgeCommand.ExecuteNonQueryAsync();
            await using var optionEdgeCommand = edgeConnection.CreateCommand();
            optionEdgeCommand.CommandText = "UPDATE options SET display_order=1000000 WHERE option_id=$id;";
            optionEdgeCommand.Parameters.AddWithValue("$id", firstOptions[2].Id.ToString());
            await optionEdgeCommand.ExecuteNonQueryAsync();
        }
        var edgeBaseline = await store.ReadCatalogueImportBaselineAsync();
        baseline = edgeBaseline;
        firstCurrent = edgeBaseline.Products.Single(value => value.Id == first);
        secondCurrent = edgeBaseline.Products.Single(value => value.Id == second);
        firstGroups = firstCurrent.OptionGroups.OrderBy(value => value.DisplayOrder).ToArray();
        firstOptions = firstGroups[0].Options.OrderBy(value => value.DisplayOrder).ToArray();
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
        Assert.AreEqual(tempLikeCode, (await store.GetProductForEditAsync(tempLike))!.Code);
        var saved = await store.GetProductForEditAsync(first);
        Assert.IsNotNull(saved);
        CollectionAssert.AreEqual(SwappedGroupNames, saved!.Groups.Select(value => value.Name).ToArray());
        var savedOptions = saved.Groups.Single(value => value.Name == "First group").Options.ToArray();
        CollectionAssert.AreEqual(SwappedOptionNames, savedOptions.Take(2).Select(value => value.Name).ToArray());
        Assert.AreEqual("Edge", savedOptions.Single(value => value.Name == "Edge").Name);
        Assert.AreEqual(1000000, savedOptions.Single(value => value.Name == "Edge").DisplayOrder);
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

    private static CatalogueImportOperation ProductModifyWithCategory(CatalogueImportBaselineProduct product, string code, string categoryName, string? categoryShortCode, CatalogueImportEntityReference categoryReference) =>
        new(CatalogueImportEntityType.Product, CatalogueImportOperationKind.Modify, product.Id, $"product:{product.Id:N}", 2, "Products",
            new Dictionary<string, string?>
            {
                ["code"] = code, ["name"] = product.Name, ["category"] = categoryName, ["categoryShortCode"] = categoryShortCode,
                ["priceCents"] = product.PriceTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture), ["vatRate"] = product.VatRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["isActive"] = product.IsActive.ToString(), ["discountEligible"] = product.DiscountEligible.ToString(), ["optionsEnabled"] = product.OptionsEnabled.ToString()
            },
            CatalogueImportEntityReference.Existing(product.Id, $"product:{product.Id:N}"), categoryReference);

    private static CatalogueImportPlan FullHierarchyCreatePlan() => new(CatalogueImportMode.Update,
        [
            new CatalogueImportOperation(CatalogueImportEntityType.Product, CatalogueImportOperationKind.Create, null, "product:new:rollback", 2, "Products",
                new Dictionary<string, string?> { ["code"] = "P-rollback", ["name"] = "Rollback", ["category"] = "Desserts", ["categoryShortCode"] = "DE", ["priceCents"] = "0", ["vatRate"] = "20", ["isActive"] = "true", ["discountEligible"] = "true", ["optionsEnabled"] = "true" },
                CatalogueImportEntityReference.New("product:new:rollback"), CatalogueImportEntityReference.New("category:new:rollback")),
            new CatalogueImportOperation(CatalogueImportEntityType.OptionGroup, CatalogueImportOperationKind.Create, null, "group:new:rollback", 2, "OptionGroups",
                new Dictionary<string, string?> { ["productCode"] = "P-rollback", ["name"] = "Rollback group", ["selectionMode"] = "MULTI", ["isRequired"] = "false", ["minSelections"] = "0", ["maxSelections"] = "1", ["displayOrder"] = "0" },
                CatalogueImportEntityReference.New("group:new:rollback"), ParentReference: CatalogueImportEntityReference.New("product:new:rollback")),
            new CatalogueImportOperation(CatalogueImportEntityType.Option, CatalogueImportOperationKind.Create, null, "option:new:rollback", 2, "Options",
                new Dictionary<string, string?> { ["name"] = "Rollback option", ["priceAdjustmentCents"] = "0", ["isActive"] = "true", ["displayOrder"] = "0" },
                CatalogueImportEntityReference.New("option:new:rollback"), ParentReference: CatalogueImportEntityReference.New("group:new:rollback"))
        ], [], [new("category:new:rollback", "Desserts", "DE")], "synthetic");

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

    private static async Task<long> ReadRevisionAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        var value = await command.ExecuteScalarAsync();
        return long.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), out var revision) ? revision : 0;
    }

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

    [TestMethod]
    public async Task UnrelatedOrderWriteDoesNotInvalidateCatalogueBaselineToken()
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
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "P-1", "Original", category.Id, Money.FromCents(100), 20m, true, true, false, []))).Value!;
        var baseline = await store.ReadCatalogueImportBaselineAsync();
        var selected = (await new OrderEntryCatalogueService(store).GetActiveProductAsync(productId))!;
        using var orderService = new OrderEntryService(new OrderEntryCatalogueService(store),
            new Sushi81.Pos.Infrastructure.Settings.SqliteBusinessSettingsStore(factory, runner, clock),
            new Sushi81.Pos.Infrastructure.Order.SqliteOrderStore(factory, runner), new NoopDispatcher(), ids, clock);
        var order = await orderService.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, selected.Aggregate, [], [], 1, selected.CategoryName)],
            FulfilmentMode.Retrait, clock.BusinessDate, new TimeOnly(11, 0), null, null, null, false));
        Assert.IsTrue(order.Succeeded, string.Join(";", order.Issues.Select(issue => issue.Message)));

        var current = baseline.Products.Single(value => value.Id == productId);
        var result = await store.CommitAsync(new CatalogueImportCommitRequest(
            new CatalogueImportPlan(CatalogueImportMode.Update, [ProductModify(current, "P-1-updated")], [], [], "synthetic"), baseline));

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Code)));
        Assert.AreEqual("P-1-updated", (await store.GetProductForEditAsync(productId))!.Code);
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class NoOpImportGateway : ICatalogueWorkbookImportGateway
    {
        public Task<CatalogueImportWorkbook> ReadAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingImportStore : ICatalogueImportStore
    {
        public int CommitCalls { get; private set; }
        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) => Task.FromResult(CatalogueImportBaseline.Empty);
        public Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCalls++;
            return Task.FromResult(CatalogueImportCommitResult.Success(changed: true));
        }
    }

    private sealed class BlockingImportStore : ICatalogueImportStore
    {
        public TaskCompletionSource<bool> Entered { get; } = NewSignal();
        public TaskCompletionSource<bool> Release { get; } = NewSignal();
        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) => Task.FromResult(CatalogueImportBaseline.Empty);
        public async Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return CatalogueImportCommitResult.Success(changed: true);
        }
    }

    private sealed class BlockingNotifier : IDurableChangeNotifier
    {
        public TaskCompletionSource<bool> Entered { get; } = NewSignal();
        public TaskCompletionSource<bool> Release { get; } = NewSignal();
        public async Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    private sealed class EmptyIds : IIdGenerator
    {
        public Guid NewId() => Guid.Empty;
    }

    private sealed class FixedIds : IIdGenerator
    {
        private static readonly Guid Fixed = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public Guid NewId() => Fixed;
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
