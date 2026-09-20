using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class CatalogueImportPlannerEvidenceTests
{
    [TestMethod]
    public void ExistingAndNewCategoryShortCodeCollisionsUseNormalizedKeys()
    {
        var currentCategoryId = Guid.NewGuid();
        var baseline = new CatalogueImportBaseline(
            [new CatalogueImportCategory(currentCategoryId, "Plats", "PL")],
            [Product(Guid.NewGuid(), "P-1", "Current", currentCategoryId, "Plats", "PL")]);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [
                ProductRow(2, "P-1", "Current", "Plats", "PL"),
                ProductRow(3, "P-2", "New", "Desserts", " pl ")
            ], [], [], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, baseline);

        Assert.IsNull(result.Plan);
        CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), "category-short-code-duplicate");
    }

    [TestMethod]
    public void DuplicateCurrentCategoryNameIsBlockingAndNeverGuessesOrThrows()
    {
        var first = new CatalogueImportCategory(Guid.NewGuid(), "Plats", "PL");
        var second = new CatalogueImportCategory(Guid.NewGuid(), " plats ", "PP");
        var baseline = new CatalogueImportBaseline([first, second], [Product(Guid.NewGuid(), "P-1", "Current", first.Id, "Plats", "PL")]);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [ProductRow(2, "P-1", "Current", "PLATS", null)], [], [], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, baseline);

        Assert.IsNull(result.Plan);
        CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), "duplicate-category-name");
    }

    [TestMethod]
    public void DuplicateCurrentCategoryShortCodeBlocksWithoutGuessing()
    {
        var plats = new CatalogueImportCategory(Guid.NewGuid(), "Plats", "PL");
        var desserts = new CatalogueImportCategory(Guid.NewGuid(), "Desserts", " pl ");
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [ProductRow(2, "P-1", "Otherwise valid", "Plats", "PL")], [], [], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update,
            workbook, new CatalogueImportBaseline([plats, desserts], []));

        Assert.IsNull(result.Plan);
        var issue = result.Preview.Issues.Single(value => value.Code == "category-short-code-duplicate");
        Assert.IsNull(issue.ExcelRow);
        Assert.IsNull(issue.FieldKey);
        StringAssert.Contains(issue.Message, "Current Catalogue");
        Assert.AreEqual(0, result.Preview.NewCategoryCount);
        Assert.IsFalse(result.Preview.Issues.Any(value => value.Code is "existing-category-short-code-change" or "duplicate-category-name"));
    }

    [TestMethod]
    public void SameIdProductBusinessEditsEachProduceModify()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var category = new CatalogueImportCategory(categoryId, "Plats", null);
        var current = Product(productId, "P-1", "Product", categoryId, "Plats", null);
        var exported = WorkbookProduct(current);
        var manifest = ProductManifest(productId, exported);
        var cases = new (string Name, CatalogueWorkbookProduct Edit)[]
        {
            ("code", exported with { Code = "P-2" }),
            ("name", exported with { Name = "Renamed" }),
            ("category", exported with { CategoryName = "Desserts" }),
            ("price", exported with { PriceTtc = Money.FromCents(125) }),
            ("vat", exported with { VatRate = 10m }),
            ("discount", exported with { DiscountEligible = true }),
            ("options", exported with { OptionsEnabled = true })
        };

        foreach (var testCase in cases)
        {
            var categories = testCase.Name == "category"
                ? new[] { category, new CatalogueImportCategory(Guid.NewGuid(), "Desserts", null) }
                : new[] { category };
            var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update,
                new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, testCase.Edit, manifest.RowKey, productId)], [], [], [manifest], []),
                new CatalogueImportBaseline(categories, [current]));

            Assert.AreEqual(0, result.Preview.ErrorCount, testCase.Name);
            Assert.AreEqual(1, result.Preview.ProductModifyCount, testCase.Name);
        }
    }

    [TestMethod]
    public void AddOnlyNewCodeNeverUpdatesSimilarCurrentProduct()
    {
        var categoryId = Guid.NewGuid();
        var baseline = new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)], [Product(Guid.NewGuid(), "P-1", "Current", categoryId, "Plats", null)]);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [ProductRow(2, "P-2", "Similar", "Plats", null)], [], [], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, baseline);

        Assert.IsNotNull(result.Plan);
        Assert.AreEqual(1, result.Preview.ProductCreateCount);
        Assert.AreEqual(0, result.Preview.ProductModifyCount);
        Assert.AreEqual(CatalogueImportOperationKind.Create, result.Plan!.Operations.Single().Kind);
    }

    [TestMethod]
    public void OmittedProductGroupAndOptionRowsHaveNoDeleteMeaning()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var current = Product(productId, "P-1", "Product", categoryId, "Plats", null, optionsEnabled: true,
            [new CatalogueImportBaselineOptionGroup(groupId, productId, "Extras", SelectionMode.Multi, true, 1, 2, 0,
                [new CatalogueImportBaselineOption(optionId, groupId, "Sauce", Money.Zero, true, 0)])]);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update,
            new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [], [], [], [], []),
            new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)], [current]));

        Assert.AreEqual(0, result.Preview.ErrorCount);
        Assert.IsNotNull(result.Plan);
        Assert.HasCount(0, result.Plan!.Operations);
        Assert.IsTrue(result.Preview.OmittedRowsAreNotDeleted);
        Assert.IsFalse(result.Plan.Operations.Any(operation => Enum.GetName(operation.Kind) == "Delete"));
    }

    [TestMethod]
    public void IdentityCorruptionCasesAreRowAddressableAndFailClosed()
    {
        var id = Guid.NewGuid();
        var manifest = ProductManifest(id, WorkbookProduct(id));
        var baseline = new CatalogueImportBaseline([new CatalogueImportCategory(Guid.NewGuid(), "Plats", null)], [Product(id, "P-1", "Product", null, "Plats", null)]);
        var valid = ProductRow(2, WorkbookProduct(id), manifest.RowKey, id);
        var cases = new (string Name, IReadOnlyList<CatalogueImportProductRow> Rows, IReadOnlyList<CatalogueImportManifestEntry> Manifest, string Code)[]
        {
            ("malformed", [valid with { ProductId = "not-a-guid" }], [manifest], "malformed-entity-id"),
            ("unknown", [valid with { ProductRowKey = "product:unknown", ProductId = Guid.NewGuid().ToString("D") }], [manifest with { RowKey = "product:unknown", EntityId = Guid.NewGuid() }], "unknown-entity-id"),
            ("mismatched", [valid with { ProductId = Guid.NewGuid().ToString("D") }], [manifest], "misbound-identity"),
            ("wrong-type", [valid with { ProductRowKey = "group:wrong" }], [manifest with { RowKey = "group:wrong", EntityType = "OptionGroup" }], "wrong-entity-type"),
            ("duplicate-row-key", [valid, valid with { ExcelRow = 3 }], [manifest], "duplicate-row-key"),
            ("duplicate-id", [valid, valid with { ExcelRow = 3, ProductRowKey = "product:second" }], [manifest, manifest with { RowKey = "product:second" }], "duplicate-entity-id")
        };

        foreach (var testCase in cases)
        {
            var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update,
                new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, testCase.Rows, [], [], testCase.Manifest, []), baseline);
            Assert.IsNull(result.Plan, testCase.Name);
            CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), testCase.Code, $"{testCase.Name}: {string.Join(",", result.Preview.Issues.Select(issue => issue.Code))}");
        }
    }

    [TestMethod]
    public void InvalidExistingIdentityNeverFallsThroughToCreateOrCascade()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var category = new CatalogueImportCategory(categoryId, "Plats", null);
        var current = Product(productId, "P-1", "Product", categoryId, "Plats", null, optionsEnabled: true,
            [new CatalogueImportBaselineOptionGroup(groupId, productId, "Extras", SelectionMode.Multi, false, 0, 1, 0,
                [new CatalogueImportBaselineOption(optionId, groupId, "Sauce", Money.Zero, true, 0)])]);
        var baseline = new CatalogueImportBaseline([category], [current]);
        var productExport = WorkbookProduct(current);
        var groupExport = new CatalogueWorkbookOptionGroup(groupId, productId, "P-1", "Product", "Extras", SelectionMode.Multi, false, 0, 1, 0, []);
        var optionExport = new CatalogueWorkbookOption(optionId, groupId, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0);
        var bindings = new[]
        {
            ProductManifest(productId, productExport),
            new CatalogueImportManifestEntry("OptionGroup", $"group:{groupId:N}", groupId, $"product:{productId:N}", "OptionGroups", 2, CatalogueWorkbookFingerprint.OptionGroup(groupExport), CatalogueWorkbookFingerprint.ParentProduct("P-1", "Product")),
            new CatalogueImportManifestEntry("Option", $"option:{optionId:N}", optionId, $"group:{groupId:N}", "Options", 2, CatalogueWorkbookFingerprint.Option(optionExport), CatalogueWorkbookFingerprint.ParentOptionGroup("P-1", "Product", "Extras"))
        };

        var malformedProduct = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [ProductRow(2, productExport, $"product:{productId:N}", Guid.Empty) with { ProductId = "NOT-A-GUID" }],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "Extras", "MULTI", false, 0, 1, 0, null, null, null, null)],
            [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0, null, null, null, null, null)],
            bindings, []);
        var malformedProductResult = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, malformedProduct, baseline);
        Assert.AreEqual(0, malformedProductResult.Preview.ProductCreateCount);
        Assert.AreEqual(0, malformedProductResult.Preview.OptionGroupCreateCount);
        Assert.AreEqual(0, malformedProductResult.Preview.OptionCreateCount);
        Assert.AreEqual(0, malformedProductResult.Preview.NewCategoryCount);
        Assert.IsFalse(malformedProductResult.Preview.Issues.Any(issue => issue.Code is "missing-parent" or "ambiguous-parent"));

        var malformedGroup = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "Extras", "MULTI", false, 0, 1, 0, $"product:{productId:N}", $"group:{groupId:N}", productId.ToString("D"), "NOT-A-GUID")], [], bindings, []);
        var malformedGroupResult = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, malformedGroup, baseline);
        Assert.AreEqual(0, malformedGroupResult.Preview.OptionGroupCreateCount);
        Assert.IsFalse(malformedGroupResult.Preview.Issues.Any(issue => issue.Code is "missing-parent" or "ambiguous-parent"));

        var malformedOption = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [], [],
            [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0, $"product:{productId:N}", $"group:{groupId:N}", $"option:{optionId:N}", "NOT-A-GUID", groupId.ToString("D"))], bindings, []);
        var malformedOptionResult = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, malformedOption, baseline);
        Assert.AreEqual(0, malformedOptionResult.Preview.OptionCreateCount);
        Assert.IsFalse(malformedOptionResult.Preview.Issues.Any(issue => issue.Code is "missing-parent" or "ambiguous-parent"));
    }

    [TestMethod]
    public void NewAndExistingParentResolutionIsExplicitAndAmbiguityBlocks()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var current = Product(productId, "P-1", "Product", categoryId, "Plats", null, optionsEnabled: true,
            [new CatalogueImportBaselineOptionGroup(groupId, productId, "Extras", SelectionMode.Multi, false, 0, 2, 0,
                [new CatalogueImportBaselineOption(optionId, groupId, "Sauce", Money.Zero, true, 0)])]);
        var baseline = new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)], [current]);

        var existingGroup = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "New extras", "MULTI", false, 0, 1, 1, null, null, null, null)], [], [], []);
        var existingGroupResult = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, existingGroup, baseline);
        Assert.IsNotNull(existingGroupResult.Plan);
        Assert.IsTrue(existingGroupResult.Plan!.Operations.Single().ParentReference!.IsExisting);

        var existingOption = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [], [],
            [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras", "New sauce", Money.Zero, true, 1, null, null, null, null, null)], [], []);
        var existingOptionResult = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, existingOption, baseline);
        Assert.IsNotNull(existingOptionResult.Plan);
        Assert.IsTrue(existingOptionResult.Plan!.Operations.Single().ParentReference!.IsExisting);

        var productExport = WorkbookProduct(current);
        var groupExport = new CatalogueWorkbookOptionGroup(groupId, productId, "P-1", "Product", "Extras", SelectionMode.Multi, false, 0, 2, 0, []);
        var optionExport = new CatalogueWorkbookOption(optionId, groupId, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0);
        var bindings = new[]
        {
            ProductManifest(productId, productExport),
            new CatalogueImportManifestEntry("OptionGroup", $"group:{groupId:N}", groupId, $"product:{productId:N}", "OptionGroups", 2, CatalogueWorkbookFingerprint.OptionGroup(groupExport), CatalogueWorkbookFingerprint.ParentProduct("P-1", "Product")),
            new CatalogueImportManifestEntry("Option", $"option:{optionId:N}", optionId, $"group:{groupId:N}", "Options", 2, CatalogueWorkbookFingerprint.Option(optionExport), CatalogueWorkbookFingerprint.ParentOptionGroup("P-1", "Product", "Extras"))
        };
        var groupTamper = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update,
            new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [],
                [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "Extras", "MULTI", false, 0, 2, 0, "product:wrong", $"group:{groupId:N}", productId.ToString("D"), groupId.ToString("D"))], [], bindings, []), baseline);
        Assert.IsNull(groupTamper.Plan);
        CollectionAssert.Contains(groupTamper.Preview.Issues.Select(issue => issue.Code).ToArray(), "wrong-parent-binding");

        var optionTamper = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update,
            new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [], [],
                [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0, "product:wrong", $"group:{groupId:N}", $"option:{optionId:N}", optionId.ToString("D"), groupId.ToString("D"))], bindings, []), baseline);
        Assert.IsNull(optionTamper.Plan);
        CollectionAssert.Contains(optionTamper.Preview.Issues.Select(issue => issue.Code).ToArray(), "wrong-parent-binding");

        var ambiguous = current with
        {
            OptionGroups = [
                current.OptionGroups[0],
                new CatalogueImportBaselineOptionGroup(Guid.NewGuid(), productId, " extras ", SelectionMode.Multi, false, 0, 1, 1, [])]
        };
        var ambiguousResult = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update,
            new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [], [],
                [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras", "New sauce", Money.Zero, true, 1, null, null, null, null, null)], [], []),
            new CatalogueImportBaseline(baseline.Categories, [ambiguous]));
        Assert.IsNull(ambiguousResult.Plan);
        CollectionAssert.Contains(ambiguousResult.Preview.Issues.Select(issue => issue.Code).ToArray(), "ambiguous-parent");
    }

    [TestMethod]
    public void CategoryShortCodeMatrixIsExplicitAndFailClosed()
    {
        var existingId = Guid.NewGuid();
        var existing = new CatalogueImportCategory(existingId, "Plats", "PL");
        var categoryCases = new (string Name, CatalogueImportBaseline Baseline, IReadOnlyList<CatalogueImportProductRow> Rows, string? Error, bool PlanExpected)[]
        {
            ("existing blank preserves", new([existing], []), [ProductRow(2, "P-1", "Current", "Plats", null)], null, true),
            ("existing normalized same accepted", new([existing], []), [ProductRow(2, "P-1", "Current", "Plats", " pl ")], null, true),
            ("existing different blocks", new([existing], []), [ProductRow(2, "P-1", "Current", "Plats", "XX")], "existing-category-short-code-change", false),
            ("existing blank current cannot be set", new([existing with { ShortCode = null }], []), [ProductRow(2, "P-1", "Current", "Plats", "PL")], "existing-category-short-code-change", false),
            ("new blank allowed", CatalogueImportBaseline.Empty, [ProductRow(2, "P-1", "New", "Desserts", null)], null, true),
            ("new repeated blank and equivalent", CatalogueImportBaseline.Empty, [ProductRow(2, "P-1", "One", "Desserts", null), ProductRow(3, "P-2", "Two", "Desserts", " pl ")], null, true),
            ("new repeated conflict", CatalogueImportBaseline.Empty, [ProductRow(2, "P-1", "One", "Desserts", "PL"), ProductRow(3, "P-2", "Two", "Desserts", "XX")], "conflicting-category-short-code", false),
            ("current versus new normalized collision", new([existing], []), [ProductRow(2, "P-1", "Current", "Plats", "PL"), ProductRow(3, "P-2", "New", "Desserts", " pl ")], "category-short-code-duplicate", false),
            ("two new normalized collision", CatalogueImportBaseline.Empty, [ProductRow(2, "P-1", "One", "Desserts", "PL"), ProductRow(3, "P-2", "Two", "Drinks", " pl ")], "category-short-code-duplicate", false)
        };

        foreach (var testCase in categoryCases)
        {
            var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly,
                new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, testCase.Rows, [], [], [], []), testCase.Baseline);
            Assert.AreEqual(testCase.PlanExpected, result.Plan is not null, testCase.Name);
            if (testCase.Error is not null)
                CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), testCase.Error, testCase.Name);
            if (testCase.Name == "existing blank current cannot be set")
            {
                var issue = result.Preview.Issues.Single(issue => issue.Code == "existing-category-short-code-change");
                Assert.AreEqual("Products", issue.Worksheet);
                Assert.AreEqual(2, issue.ExcelRow);
                Assert.AreEqual("category_short_code", issue.FieldKey);
            }
            if (testCase.Name == "new repeated conflict")
                Assert.IsFalse(result.Preview.Issues.Any(issue => issue.Code == "existing-category-short-code-change"));
        }
    }

    [TestMethod]
    public void ExistingCategoryRepeatedRowsKeepSpecificShortCodeGuidanceInUpdateAndAddOnlyModes()
    {
        var baseline = new CatalogueImportBaseline(
            [new CatalogueImportCategory(Guid.NewGuid(), "Plats", "PL")], []);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [
                ProductRow(2, "P-1", "Normal", "Plats", "PL"),
                ProductRow(3, "P-2", "Blank preserve", "Plats", null),
                ProductRow(4, "P-3", "Changed", "Plats", "XX")
            ], [], [], [], []);

        foreach (var mode in new[] { CatalogueImportMode.Update, CatalogueImportMode.AddOnly })
        {
            var result = new CatalogueImportPlanner().Plan(mode, workbook, baseline);

            Assert.IsTrue(result.HasErrors, mode.ToString());
            Assert.IsNull(result.Plan, mode.ToString());
            var issue = result.Preview.Issues.Single(value => value.Code == "existing-category-short-code-change");
            Assert.AreEqual("Products", issue.Worksheet, mode.ToString());
            Assert.AreEqual(4, issue.ExcelRow, mode.ToString());
            Assert.AreEqual("category_short_code", issue.FieldKey, mode.ToString());
            Assert.IsFalse(result.Preview.Issues.Any(value => value.Code == "conflicting-category-short-code"), mode.ToString());
            Assert.IsFalse(result.Preview.Issues.Any(value => value.Code == "existing-category-short-code-change" && value.ExcelRow is 2 or 3), mode.ToString());
        }
    }

    [TestMethod]
    public void DomainValidationMatrixBlocksInvalidProductAndOptionStructure()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var category = new CatalogueImportCategory(categoryId, "Plats", null);
        var current = Product(productId, "P-1", "Product", categoryId, "Plats", null, optionsEnabled: true,
            [new CatalogueImportBaselineOptionGroup(groupId, productId, "Extras", SelectionMode.Multi, true, 2, 3, 0,
                [new CatalogueImportBaselineOption(optionId, groupId, "Sauce", Money.Zero, true, 0)])]);
        var baseline = new CatalogueImportBaseline([category], [current]);
        var cases = new (string Name, CatalogueImportWorkbook Workbook, string Code)[]
        {
            ("negative price", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.FromCents(-1))], [], [], [], []), "price-negative"),
            ("vat range", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 101m)], [], [], [], []), "vat-range"),
            ("single min max", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true)], [GroupRow(2, "P-1", "Product", "Extras", "SINGLE", true, 1, 2, 0)], [], [], []), "invalid-group-structure"),
            ("multi missing bounds", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true)], [GroupRow(2, "P-1", "Product", "Extras", "MULTI", true, null, null, 0)], [], [], []), "invalid-group-structure"),
            ("multi min greater than max", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true)], [GroupRow(2, "P-1", "Product", "Extras", "MULTI", true, 3, 1, 0)], [], [], []), "invalid-group-structure"),
            ("required multi min zero", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true)], [GroupRow(2, "P-1", "Product", "Extras", "MULTI", true, 0, 1, 0)], [], [], []), "required-field"),
            ("negative group order", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true)], [GroupRow(2, "P-1", "Product", "Extras", "MULTI", false, 0, 1, -1)], [], [], []), "invalid-option-structure"),
            ("negative option order", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true)], [GroupRow(2, "P-1", "Product", "Extras", "MULTI", false, 0, 1, 0)], [OptionRow(2, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, -1)], [], []), "invalid-option-structure"),
            ("insufficient active choices", new(CatalogueWorkbookSchema.ContractVersion, [], [], [], [], []), "required-active-choices")
        };

        foreach (var testCase in cases)
        {
            var caseBaseline = testCase.Name == "insufficient active choices"
                ? baseline
                : new CatalogueImportBaseline([category], []);
            var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, testCase.Workbook, caseBaseline);
            Assert.IsNull(result.Plan, testCase.Name);
            CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), testCase.Code, $"{testCase.Name}: {string.Join(",", result.Preview.Issues.Select(issue => issue.Code))}");
        }
    }

    [TestMethod]
    public void StaleWorkbookTruthTableHasFiveDeterministicOutcomes()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var category = new CatalogueImportCategory(categoryId, "Plats", null);
        var baselineProduct = Product(productId, "P-1", "Baseline", categoryId, "Plats", null);
        var exported = WorkbookProduct(baselineProduct);
        var manifest = ProductManifest(productId, exported);
        var liveChanged = baselineProduct with { Name = "Live" };
        var workbookChanged = exported with { Name = "Workbook" };
        var cases = new (string Name, CatalogueImportWorkbook Workbook, CatalogueImportBaselineProduct Live, bool Plan, int Modify, string? Error)[]
        {
            ("both baseline", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, exported, manifest.RowKey, productId)], [], [], [manifest], []), baselineProduct, true, 0, null),
            ("workbook only", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, workbookChanged, manifest.RowKey, productId)], [], [], [manifest], []), baselineProduct, true, 1, null),
            ("live only", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, exported, manifest.RowKey, productId)], [], [], [manifest], []), liveChanged, true, 0, null),
            ("same new live and workbook", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, workbookChanged, manifest.RowKey, productId)], [], [], [manifest], []), liveChanged with { Name = "Workbook" }, true, 0, null),
            ("different new live and workbook", new(CatalogueWorkbookSchema.ContractVersion, [ProductRow(2, workbookChanged, manifest.RowKey, productId)], [], [], [manifest], []), liveChanged, false, 1, "stale-conflict")
        };

        foreach (var testCase in cases)
        {
            var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, testCase.Workbook,
                new CatalogueImportBaseline([category], [testCase.Live]));
            Assert.AreEqual(testCase.Plan, result.Plan is not null, testCase.Name);
            Assert.AreEqual(testCase.Modify, result.Preview.ProductModifyCount, testCase.Name);
            if (testCase.Error is not null)
                CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), testCase.Error, testCase.Name);
        }
    }

    [TestMethod]
    public void RepeatedPlanningProducesIdenticalPlanReferencesAndIssues()
    {
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [ProductRow(2, "P-1", "Product", "New category", "NC")],
            [GroupRow(2, "P-1", "Product", "Extras", "MULTI", false, 0, 1, 0)],
            [OptionRow(2, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0)], [], []);
        var planner = new CatalogueImportPlanner();
        var first = planner.Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);
        var second = planner.Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);

        Assert.IsNotNull(first.Plan);
        Assert.AreEqual(string.Join('|', first.Preview.Issues.Select(issue => $"{issue.Code}:{issue.ExcelRow}:{issue.FieldKey}")), string.Join('|', second.Preview.Issues.Select(issue => $"{issue.Code}:{issue.ExcelRow}:{issue.FieldKey}")));
        CollectionAssert.AreEqual(first.Plan!.Operations.Select(operation => $"{operation.EntityType}:{operation.Kind}:{operation.LocalKey}:{operation.EntityReference?.LocalKey}:{operation.ParentReference?.LocalKey}").ToArray(),
            second.Plan!.Operations.Select(operation => $"{operation.EntityType}:{operation.Kind}:{operation.LocalKey}:{operation.EntityReference?.LocalKey}:{operation.ParentReference?.LocalKey}").ToArray());
    }

    private static CatalogueImportBaselineProduct Product(Guid id, string code, string name, Guid? categoryId, string categoryName, string? categoryShortCode, bool optionsEnabled = false, IReadOnlyList<CatalogueImportBaselineOptionGroup>? groups = null)
        => new(id, code, name, categoryId ?? Guid.NewGuid(), categoryName, categoryShortCode, Money.FromCents(100), 20m, true, false, optionsEnabled, groups ?? []);

    private static CatalogueWorkbookProduct WorkbookProduct(CatalogueImportBaselineProduct product)
        => new(product.Id, product.Code, product.Name, product.CategoryName, product.CategoryShortCode, product.PriceTtc, product.VatRate, product.IsActive, product.DiscountEligible, product.OptionsEnabled, []);

    private static CatalogueWorkbookProduct WorkbookProduct(Guid id)
        => new(id, "P-1", "Product", "Plats", null, Money.FromCents(100), 20m, true, false, false, []);

    private static CatalogueImportManifestEntry ProductManifest(Guid id, CatalogueWorkbookProduct product)
        => new("Product", $"product:{id:N}", id, null, "Products", 2, CatalogueWorkbookFingerprint.Product(product));

    private static CatalogueImportProductRow ProductRow(int row, string code, string name, string categoryName, string? shortCode, Money price = default, decimal vat = 20m, bool active = true, bool discount = false, bool options = false)
        => new(row, code, name, categoryName, shortCode, price == default ? Money.FromCents(100) : price, vat, active, discount, options, null, null);

    private static CatalogueImportProductRow ProductRow(int row, CatalogueWorkbookProduct product, string rowKey, Guid id)
        => new(row, product.Code, product.Name, product.CategoryName, product.CategoryShortCode, product.PriceTtc, product.VatRate, product.IsActive, product.DiscountEligible, product.OptionsEnabled, rowKey, id.ToString("D"));

    private static CatalogueImportOptionGroupRow GroupRow(int row, string productCode, string productName, string groupName, string mode, bool required, int? min, int? max, int order)
        => new(row, productCode, productName, groupName, mode, required, min, max, order, null, null, null, null);

    private static CatalogueImportOptionRow OptionRow(int row, string productCode, string productName, string groupName, string optionName, Money price, bool active, int order)
        => new(row, productCode, productName, groupName, optionName, price, active, order, null, null, null, null, null);
}
