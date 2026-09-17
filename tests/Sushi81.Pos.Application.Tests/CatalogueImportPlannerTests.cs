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
}
