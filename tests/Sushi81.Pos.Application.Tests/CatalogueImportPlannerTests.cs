using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class CatalogueImportPlannerTests
{
    [TestMethod]
    public void AddOnlyFirstInitializationCreatesWithoutDurableIds()
    {
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Product", "Plats", "PL", Money.FromCents(100), 20m, true, false, false, null, null)], [], [], [], []);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);
        Assert.AreEqual(0, result.Preview.ErrorCount);
        Assert.AreEqual(1, result.Preview.ProductCreateCount);
        Assert.IsNotNull(result.Plan);
        Assert.HasCount(1, result.Plan!.Operations);
        Assert.IsNull(result.Plan.Operations[0].EntityId);
        Assert.AreEqual(CatalogueImportOperationKind.Create, result.Plan.Operations[0].Kind);
    }

    [TestMethod]
    public void AddOnlyProductCodeCollisionAndExistingBindingAreBlocking()
    {
        var id = Guid.NewGuid();
        var baseline = new CatalogueImportBaseline([], [new CatalogueImportBaselineProduct(id, "P-1", "Current", Guid.NewGuid(), "Plats", null, Money.FromCents(100), 20m, true, false, false, [])]);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "New", "Plats", null, Money.FromCents(100), 20m, true, false, false, "product:existing", id.ToString("D"))], [], [],
            [new CatalogueImportManifestEntry("Product", "product:existing", id, null, "Products", 2, "baseline")], []);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, baseline);
        Assert.IsGreaterThan(0, result.Preview.ErrorCount);
        Assert.IsNull(result.Plan);
        CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), "add-only-existing-binding");
        CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), "add-only-product-code-collision");
    }

    [TestMethod]
    public void OmittedRowsRemainUntouchedAndStaleConflictBlocksEditedEntity()
    {
        var id = Guid.NewGuid();
        var baselineProduct = new CatalogueImportBaselineProduct(id, "P-1", "Current", Guid.NewGuid(), "Plats", null, Money.FromCents(100), 20m, true, false, false, []);
        var exported = new CatalogueWorkbookProduct(id, "P-1", "Exported", "Plats", null, Money.FromCents(100), 20m, true, false, false, []);
        var manifest = new CatalogueImportManifestEntry("Product", $"product:{id:N}", id, null, "Products", 2, CatalogueWorkbookFingerprint.Product(exported));
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Workbook edit", "Plats", null, Money.FromCents(100), 20m, true, false, false, manifest.RowKey, id.ToString("D"))], [], [], [manifest], []);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, new CatalogueImportBaseline([], [baselineProduct]));
        Assert.IsGreaterThan(0, result.Preview.ErrorCount);
        Assert.IsNull(result.Plan);
        CollectionAssert.Contains(result.Preview.Issues.Select(issue => issue.Code).ToArray(), "stale-conflict");
    }

    [TestMethod]
    public void WorkbookUnchangedSinceExportPreservesNewerLiveValueWithoutOverwrite()
    {
        var id = Guid.NewGuid();
        var exported = new CatalogueWorkbookProduct(id, "P-1", "Exported", "Plats", null, Money.FromCents(100), 20m, true, false, false, []);
        var current = new CatalogueImportBaselineProduct(id, "P-1", "Live newer", Guid.NewGuid(), "Plats", null, Money.FromCents(100), 20m, true, false, false, []);
        var manifest = new CatalogueImportManifestEntry("Product", $"product:{id:N}", id, null, "Products", 2, CatalogueWorkbookFingerprint.Product(exported));
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Exported", "Plats", null, Money.FromCents(100), 20m, true, false, false, manifest.RowKey, id.ToString("D"))], [], [], [manifest], []);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, new CatalogueImportBaseline([], [current]));
        Assert.AreEqual(0, result.Preview.ErrorCount);
        Assert.AreEqual(0, result.Preview.ProductModifyCount);
        Assert.HasCount(0, result.Plan!.Operations);
    }

    [TestMethod]
    public void UpdateSameIdEditPlansModifyAndStateTransitionSeparately()
    {
        var id = Guid.NewGuid(); var categoryId = Guid.NewGuid();
        var current = new CatalogueImportBaselineProduct(id, "P-1", "Old", categoryId, "Plats", null, Money.FromCents(100), 20m, true, false, false, []);
        var exported = new CatalogueWorkbookProduct(id, "P-1", "Old", "Plats", null, Money.FromCents(100), 20m, true, false, false, []);
        var binding = new CatalogueImportManifestEntry("Product", $"product:{id:N}", id, null, "Products", 2, CatalogueWorkbookFingerprint.Product(exported));
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "New", "Plats", null, Money.FromCents(125), 20m, false, false, false, binding.RowKey, id.ToString("D"))], [], [], [binding], []);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)], [current]));
        Assert.AreEqual(0, result.Preview.ErrorCount);
        Assert.AreEqual(1, result.Preview.ProductModifyCount);
        Assert.AreEqual(1, result.Preview.ProductDeactivateCount);
        Assert.IsNotNull(result.Plan);
    }

    [TestMethod]
    public void NewGroupsAndOptionsResolveParentsByExactNormalizedKeys()
    {
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Product", "Plats", null, Money.FromCents(100), 20m, true, false, true, null, null)],
            [new CatalogueImportOptionGroupRow(2, "p-1", "Product", "Extras", "MULTI", false, 0, 2, 0, null, null, null, null)],
            [new CatalogueImportOptionRow(2, "P-1", "Product", "extras", "Sauce", Money.FromCents(25), true, 0, null, null, null, null, null)], [], []);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);
        Assert.AreEqual(0, result.Preview.ErrorCount);
        Assert.AreEqual(1, result.Preview.ProductCreateCount);
        Assert.AreEqual(1, result.Preview.OptionGroupCreateCount);
        Assert.AreEqual(1, result.Preview.OptionCreateCount);
        Assert.IsNotNull(result.Plan);
    }

    [TestMethod]
    public void PureStateTransitionsDoNotCountAsModify()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var product = new CatalogueImportBaselineProduct(productId, "P-1", "Product", categoryId, "Plats", null, Money.FromCents(100), 20m, true, false, true,
            [new CatalogueImportBaselineOptionGroup(groupId, productId, "Extras", SelectionMode.Multi, false, 0, 1, 0,
                [new CatalogueImportBaselineOption(optionId, groupId, "Sauce", Money.Zero, true, 0)])]);
        var productExport = new CatalogueWorkbookProduct(productId, "P-1", "Product", "Plats", null, Money.FromCents(100), 20m, true, false, true,
            [new CatalogueWorkbookOptionGroup(groupId, productId, "P-1", "Product", "Extras", SelectionMode.Multi, false, 0, 1, 0,
                [new CatalogueWorkbookOption(optionId, groupId, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0)])]);
        var manifest = new[]
        {
            new CatalogueImportManifestEntry("Product", $"product:{productId:N}", productId, null, "Products", 2, CatalogueWorkbookFingerprint.Product(productExport)),
            new CatalogueImportManifestEntry("OptionGroup", $"group:{groupId:N}", groupId, $"product:{productId:N}", "OptionGroups", 2, CatalogueWorkbookFingerprint.OptionGroup(productExport.OptionGroups[0]), CatalogueWorkbookFingerprint.ParentProduct("P-1", "Product")),
            new CatalogueImportManifestEntry("Option", $"option:{optionId:N}", optionId, $"group:{groupId:N}", "Options", 2, CatalogueWorkbookFingerprint.Option(productExport.OptionGroups[0].Options[0]), CatalogueWorkbookFingerprint.ParentOptionGroup("P-1", "Product", "Extras"))
        };
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Product", "Plats", null, Money.FromCents(100), 20m, false, false, true, manifest[0].RowKey, productId.ToString("D"))],
            [],
            [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras", "Sauce", Money.Zero, false, 0, manifest[0].RowKey, manifest[1].RowKey, manifest[2].RowKey, optionId.ToString("D"), groupId.ToString("D"))],
            manifest, []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)], [product]));

        Assert.AreEqual(0, result.Preview.ProductModifyCount);
        Assert.AreEqual(1, result.Preview.ProductDeactivateCount);
        Assert.AreEqual(0, result.Preview.OptionModifyCount);
        Assert.AreEqual(1, result.Preview.OptionDeactivateCount);
    }

    [TestMethod]
    public void CategoryShortCodeUniquenessUsesNormalizedKeyAndPreservesDisplay()
    {
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [
                new CatalogueImportProductRow(2, "P-1", "One", "New A", "pl", Money.Zero, 20m, true, false, false, null, null),
                new CatalogueImportProductRow(3, "P-2", "Two", "New A", " PL ", Money.Zero, 20m, true, false, false, null, null),
                new CatalogueImportProductRow(4, "P-3", "Three", "New B", "PL", Money.Zero, 20m, true, false, false, null, null)
            ], [], [], [], []);
        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);

        Assert.IsTrue(result.Preview.Issues.Any(issue => issue.Code == "category-short-code-duplicate"));
        Assert.IsNull(result.Plan);
        Assert.AreEqual(2, result.Preview.NewCategoryCount);
    }

    [TestMethod]
    public void PlanReferencesAreSelfContainedAndNewEntitiesHaveNoDurableIds()
    {
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Product", "New category", "NC", Money.Zero, 20m, false, false, true, null, null)],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "Extras", "MULTI", false, 0, 1, 0, null, null, null, null)],
            [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras", "Sauce", Money.FromCents(25), true, 0, null, null, null, null, null)], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);

        Assert.IsNotNull(result.Plan);
        var product = result.Plan!.Operations.Single(operation => operation.EntityType == CatalogueImportEntityType.Product);
        var group = result.Plan.Operations.Single(operation => operation.EntityType == CatalogueImportEntityType.OptionGroup);
        var option = result.Plan.Operations.Single(operation => operation.EntityType == CatalogueImportEntityType.Option);
        Assert.IsNull(product.EntityId);
        Assert.IsNull(group.EntityId);
        Assert.IsNull(option.EntityId);
        Assert.IsFalse(product.CategoryReference!.IsExisting);
        Assert.IsFalse(group.ParentReference!.IsExisting);
        Assert.IsFalse(option.ParentReference!.IsExisting);
        Assert.AreEqual(group.LocalKey, option.ParentReference.LocalKey, ignoreCase: false);
        Assert.HasCount(1, result.Plan.NewCategories);
        Assert.IsFalse(result.Plan.NewCategories[0].LocalKey.Contains("Guid", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void SelectionModeAcceptsOnlySingleOrMultiWords()
    {
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true, null, null)],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "Extras", "1", false, 0, 1, 0, null, null, null, null)], [], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.AddOnly, workbook, CatalogueImportBaseline.Empty);

        Assert.IsTrue(result.Preview.Issues.Any(issue => issue.Code == "invalid-group-structure"));
        Assert.IsNull(result.Plan);
    }

    [TestMethod]
    public void OriginalExportedParentDescriptorsRemainValidAfterLiveRename()
    {
        var categoryId = Guid.NewGuid(); var productId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var optionId = Guid.NewGuid();
        var exportedGroup = new CatalogueWorkbookOptionGroup(groupId, productId, "P-1", "Old Product", "Extras", SelectionMode.Multi, false, 0, 1, 0, []);
        var exportedOption = new CatalogueWorkbookOption(optionId, groupId, "P-1", "Old Product", "Extras", "Sauce", Money.Zero, true, 0);
        var manifest = new[]
        {
            new CatalogueImportManifestEntry("Product", $"product:{productId:N}", productId, null, "Products", 2, CatalogueWorkbookFingerprint.Product(new CatalogueWorkbookProduct(productId, "P-1", "Old Product", "Plats", null, Money.Zero, 20m, true, false, true, [exportedGroup with { Options = [exportedOption] }]))),
            new CatalogueImportManifestEntry("OptionGroup", $"group:{groupId:N}", groupId, $"product:{productId:N}", "OptionGroups", 2, CatalogueWorkbookFingerprint.OptionGroup(exportedGroup), CatalogueWorkbookFingerprint.ParentProduct("P-1", "Old Product")),
            new CatalogueImportManifestEntry("Option", $"option:{optionId:N}", optionId, $"group:{groupId:N}", "Options", 2, CatalogueWorkbookFingerprint.Option(exportedOption), CatalogueWorkbookFingerprint.ParentOptionGroup("P-1", "Old Product", "Extras"))
        };
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Old Product", "Plats", null, Money.Zero, 20m, true, false, true, manifest[0].RowKey, productId.ToString("D"))],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Old Product", "Extras", "MULTI", false, 0, 1, 0, manifest[0].RowKey, manifest[1].RowKey, productId.ToString("D"), groupId.ToString("D"))],
            [new CatalogueImportOptionRow(2, "P-1", "Old Product", "Extras", "Sauce", Money.Zero, true, 0, manifest[0].RowKey, manifest[1].RowKey, manifest[2].RowKey, optionId.ToString("D"), groupId.ToString("D"))], manifest, []);
        var current = new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)],
            [new CatalogueImportBaselineProduct(productId, "P-1", "New Product", categoryId, "Plats", null, Money.Zero, 20m, true, false, true,
                [new CatalogueImportBaselineOptionGroup(groupId, productId, "New Extras", SelectionMode.Multi, false, 0, 1, 0, [new CatalogueImportBaselineOption(optionId, groupId, "Sauce", Money.Zero, true, 0)])])]);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, current);

        Assert.AreEqual(0, result.Preview.ErrorCount);
    }

    [TestMethod]
    public void ContradictoryOptionParentDescriptorBlocksEvenWhenIdsAreValid()
    {
        var categoryId = Guid.NewGuid(); var productId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var optionId = Guid.NewGuid();
        var current = new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)],
            [new CatalogueImportBaselineProduct(productId, "P-1", "Product", categoryId, "Plats", null, Money.Zero, 20m, true, false, true,
                [new CatalogueImportBaselineOptionGroup(groupId, productId, "Extras", SelectionMode.Multi, false, 0, 1, 0, [new CatalogueImportBaselineOption(optionId, groupId, "Sauce", Money.Zero, true, 0)])])]);
        var export = new CatalogueWorkbookOption(optionId, groupId, "P-1", "Product", "Extras", "Sauce", Money.Zero, true, 0);
        var manifest = new CatalogueImportManifestEntry("Option", $"option:{optionId:N}", optionId, $"group:{groupId:N}", "Options", 2, CatalogueWorkbookFingerprint.Option(export), CatalogueWorkbookFingerprint.ParentOptionGroup("P-1", "Product", "Extras"));
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [], [],
            [new CatalogueImportOptionRow(2, "WRONG", "Product", "Extras", "Sauce", Money.Zero, true, 0, $"product:{productId:N}", $"group:{groupId:N}", manifest.RowKey, optionId.ToString("D"), groupId.ToString("D"))],
            [new CatalogueImportManifestEntry("Product", $"product:{productId:N}", productId, null, "Products", 2, "x"), new CatalogueImportManifestEntry("OptionGroup", $"group:{groupId:N}", groupId, $"product:{productId:N}", "OptionGroups", 2, "x", CatalogueWorkbookFingerprint.ParentProduct("P-1", "Product")), manifest], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, current);

        Assert.IsTrue(result.Preview.Issues.Any(issue => issue.Code == "wrong-parent-binding"));
        Assert.IsNull(result.Plan);
    }

    [TestMethod]
    public void UpdateBlankIdentityCreatesAndOmittedCurrentRowsRemainWithoutDelete()
    {
        var categoryId = Guid.NewGuid();
        var existingId = Guid.NewGuid();
        var current = new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)],
            [new CatalogueImportBaselineProduct(existingId, "OLD", "Existing", categoryId, "Plats", null, Money.Zero, 20m, true, false, false, [])]);
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "NEW", "New", "Plats", null, Money.Zero, 20m, true, false, false, null, null)], [], [], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, current);

        Assert.AreEqual(0, result.Preview.ErrorCount);
        Assert.AreEqual(1, result.Preview.ProductCreateCount);
        Assert.IsNotNull(result.Plan);
        Assert.IsTrue(result.Plan!.Operations.All(operation => operation.Kind != (CatalogueImportOperationKind)999));
        Assert.IsNull(result.Plan.Operations.Single().EntityId);
    }

    [TestMethod]
    public void OptionGroupAndOptionEditsProduceSeparateBusinessAndStateCounts()
    {
        var categoryId = Guid.NewGuid(); var productId = Guid.NewGuid(); var groupId = Guid.NewGuid(); var optionId = Guid.NewGuid();
        var current = new CatalogueImportBaseline([new CatalogueImportCategory(categoryId, "Plats", null)],
            [new CatalogueImportBaselineProduct(productId, "P-1", "Product", categoryId, "Plats", null, Money.Zero, 20m, true, false, true,
                [new CatalogueImportBaselineOptionGroup(groupId, productId, "Extras", SelectionMode.Multi, false, 0, 1, 0,
                    [new CatalogueImportBaselineOption(optionId, groupId, "Old", Money.Zero, true, 0)])])]);
        var exportedGroup = new CatalogueWorkbookOptionGroup(groupId, productId, "P-1", "Product", "Extras", SelectionMode.Multi, false, 0, 1, 0, []);
        var exportedOption = new CatalogueWorkbookOption(optionId, groupId, "P-1", "Product", "Extras", "Old", Money.Zero, true, 0);
        var manifest = new[]
        {
            new CatalogueImportManifestEntry("OptionGroup", $"group:{groupId:N}", groupId, $"product:{productId:N}", "OptionGroups", 2, CatalogueWorkbookFingerprint.OptionGroup(exportedGroup), CatalogueWorkbookFingerprint.ParentProduct("P-1", "Product")),
            new CatalogueImportManifestEntry("Option", $"option:{optionId:N}", optionId, $"group:{groupId:N}", "Options", 2, CatalogueWorkbookFingerprint.Option(exportedOption), CatalogueWorkbookFingerprint.ParentOptionGroup("P-1", "Product", "Extras")),
            new CatalogueImportManifestEntry("Product", $"product:{productId:N}", productId, null, "Products", 2, CatalogueWorkbookFingerprint.Product(new CatalogueWorkbookProduct(productId, "P-1", "Product", "Plats", null, Money.Zero, 20m, true, false, true, [])))
        };
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "Extras renamed", "MULTI", false, 0, 2, 1, $"product:{productId:N}", $"group:{groupId:N}", productId.ToString("D"), groupId.ToString("D"))],
            [new CatalogueImportOptionRow(2, "P-1", "Product", "Extras renamed", "New", Money.FromCents(25), false, 1, $"product:{productId:N}", $"group:{groupId:N}", $"option:{optionId:N}", optionId.ToString("D"), groupId.ToString("D"))], manifest, []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, current);

        Assert.AreEqual(1, result.Preview.OptionGroupModifyCount);
        Assert.AreEqual(1, result.Preview.OptionModifyCount);
        Assert.AreEqual(1, result.Preview.OptionDeactivateCount);
    }

    [TestMethod]
    public void InvalidValuesAndUnknownBindingsBlockWholePlan()
    {
        var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
            [new CatalogueImportProductRow(2, "P-1", "Product", "Plats", null, Money.FromCents(-1), 101m, true, false, false, "product:unknown", Guid.NewGuid().ToString("D"))],
            [new CatalogueImportOptionGroupRow(2, "P-1", "Product", "Extras", "MULTI", true, 2, 1, -1, "product:unknown", "group:unknown", Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"))], [], [], []);

        var result = new CatalogueImportPlanner().Plan(CatalogueImportMode.Update, workbook, CatalogueImportBaseline.Empty);

        Assert.IsGreaterThan(0, result.Preview.ErrorCount);
        Assert.IsNull(result.Plan);
    }
}
