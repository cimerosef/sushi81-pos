using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class M10Wp3CatalogueImportCommitValidatorTests
{
    [TestMethod]
    public void MalformedDuplicateOperationShapeFailsClosedWithStableIssues()
    {
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var values = ProductValues("P-1", "One");
        var operation = Existing(CatalogueImportOperationKind.Modify, productId, values, categoryId);
        var plan = new CatalogueImportPlan(CatalogueImportMode.Update, [operation, operation], [], [], "synthetic");

        var issues = CatalogueImportCommitValidator.Validate(plan, Baseline(categoryId, productId));

        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "duplicate-operation");
        Assert.IsTrue(issues.All(issue => issue.Severity == CatalogueImportIssueSeverity.Error));
    }

    [TestMethod]
    public void AddOnlyExistingBindingIsRejectedEvenWhenPayloadIsOtherwiseValid()
    {
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var operation = Existing(CatalogueImportOperationKind.Modify, productId, ProductValues("P-1", "One"), categoryId);
        var plan = new CatalogueImportPlan(CatalogueImportMode.AddOnly, [operation], [], [], "synthetic");

        var issues = CatalogueImportCommitValidator.Validate(plan, Baseline(categoryId, productId));

        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "add-only-existing-binding");
    }

    [TestMethod]
    public void EntityActionMatrixRejectsMissingModifyAndGroupStateActions()
    {
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var groupId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var product = Existing(CatalogueImportOperationKind.Activate, productId, ProductValues("P-1", "Renamed"), categoryId);
        var group = new CatalogueImportOperation(
            CatalogueImportEntityType.OptionGroup,
            CatalogueImportOperationKind.Activate,
            groupId,
            $"group:{groupId:N}", 2, "OptionGroups",
            new Dictionary<string, string?> { ["productCode"] = "P-1", ["name"] = "Extras", ["selectionMode"] = "MULTI", ["isRequired"] = "false", ["displayOrder"] = "0" },
            CatalogueImportEntityReference.Existing(groupId, $"group:{groupId:N}"),
            ParentReference: CatalogueImportEntityReference.Existing(productId, $"product:{productId:N}"));

        var issues = CatalogueImportCommitValidator.Validate(
            new CatalogueImportPlan(CatalogueImportMode.Update, [product, group], [], [], "synthetic"),
            Baseline(categoryId, productId, groupId));

        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "modify-missing");
        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "invalid-group-action");
    }

    [TestMethod]
    public void ResultingDuplicateProductCodeIsBlocking()
    {
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var baseline = new CatalogueImportBaseline(
            [new(categoryId, "Plats", null)],
            [Product(firstId, categoryId, "P-1", "One"), Product(secondId, categoryId, "P-2", "Two")]);
        var operation = Existing(CatalogueImportOperationKind.Modify, secondId, ProductValues("P-1", "Two"), categoryId);

        var issues = CatalogueImportCommitValidator.Validate(
            new CatalogueImportPlan(CatalogueImportMode.Update, [operation], [], [], "synthetic"), baseline);

        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "duplicate-product-code");
    }

    [TestMethod]
    public void ContradictoryCombinedProductOperationsAreOrderIndependent()
    {
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var modify = Existing(CatalogueImportOperationKind.Modify, productId, ProductValues("P-2", "Two"), categoryId);
        var activate = Existing(CatalogueImportOperationKind.Activate, productId, ProductValues("P-3", "Three"), categoryId);
        var baseline = Baseline(categoryId, productId);

        var forward = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [modify, activate], [], [], "synthetic"), baseline);
        var reverse = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [activate, modify], [], [], "synthetic"), baseline);

        CollectionAssert.Contains(forward.Select(issue => issue.Code).ToArray(), "contradictory-operation");
        CollectionAssert.AreEqual(
            forward.Select(issue => issue.Code + "|" + issue.Message).ToArray(),
            reverse.Select(issue => issue.Code + "|" + issue.Message).ToArray());
    }

    [TestMethod]
    public void DefinedCombinedProductModifyAndStateUsesOneFinalPayload()
    {
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var values = ProductValues("P-1", "One");
        var modify = Existing(CatalogueImportOperationKind.Modify, productId, values, categoryId);
        var activate = Existing(CatalogueImportOperationKind.Activate, productId, new Dictionary<string, string?>(values), categoryId);

        var issues = CatalogueImportCommitValidator.Validate(
            new CatalogueImportPlan(CatalogueImportMode.Update, [activate, modify], [], [], "synthetic"), Baseline(categoryId, productId));

        Assert.IsFalse(issues.Any(issue => issue.Code is "contradictory-operation" or "state-operation-missing" or "modify-missing"));
    }

    [TestMethod]
    public void InvalidModeAndPlannedCategoryCollisionsFailClosed()
    {
        var plan = new CatalogueImportPlan((CatalogueImportMode)99, [], [],
            [new("category:new:a", "Desserts", "DE"), new("category:new:b", " desserts ", " de ")], "synthetic");

        var issues = CatalogueImportCommitValidator.Validate(plan, CatalogueImportBaseline.Empty);

        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "invalid-mode");
        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "category-name-duplicate");
        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "category-short-code-duplicate");
    }

    [TestMethod]
    public void BaselineFingerprintIgnoresCollectionOrderButChangesWithBusinessState()
    {
        var categoryA = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var categoryB = Guid.Parse("00000000-0000-0000-0000-000000000005");
        var productA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var productB = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var baseline = new CatalogueImportBaseline([new(categoryA, "Plats", null), new(categoryB, "Desserts", "DE")],
            [Product(productA, categoryA, "P-1", "One"), Product(productB, categoryB, "P-2", "Two")]);
        var reordered = new CatalogueImportBaseline(baseline.Categories.Reverse().ToArray(), baseline.Products.Reverse().ToArray());
        var changed = reordered with { Products = [Product(productA, categoryA, "P-1", "Renamed"), Product(productB, categoryB, "P-2", "Two")] };

        Assert.AreEqual(CatalogueImportBaselineFingerprint.Compute(baseline), CatalogueImportBaselineFingerprint.Compute(reordered));
        Assert.AreNotEqual(CatalogueImportBaselineFingerprint.Compute(baseline), CatalogueImportBaselineFingerprint.Compute(changed));
    }

    [TestMethod]
    public void WrongLocalKeysAndReferencesAreBlocking()
    {
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var operation = new CatalogueImportOperation(
            CatalogueImportEntityType.Product, CatalogueImportOperationKind.Modify, productId, "product:wrong", 2, "Products", ProductValues("P-1", "One"),
            CatalogueImportEntityReference.Existing(productId, $"product:{productId:N}"),
            CatalogueImportEntityReference.Existing(categoryId, $"category:{categoryId:N}"));

        var issues = CatalogueImportCommitValidator.Validate(
            new CatalogueImportPlan(CatalogueImportMode.Update, [operation], [], [], "synthetic"), Baseline(categoryId, productId));

        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "misbound-reference");
        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "invalid-local-key");
    }

    [TestMethod]
    public void ChildParentDisplayPayloadMismatchIsBlocking()
    {
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var groupId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var values = new Dictionary<string, string?>
        {
            ["productCode"] = "WRONG", ["name"] = "Extras", ["selectionMode"] = "MULTI", ["isRequired"] = "false", ["minSelections"] = "0", ["maxSelections"] = "1", ["displayOrder"] = "0"
        };
        var operation = new CatalogueImportOperation(
            CatalogueImportEntityType.OptionGroup, CatalogueImportOperationKind.Modify, groupId, $"group:{groupId:N}", 2, "OptionGroups", values,
            CatalogueImportEntityReference.Existing(groupId, $"group:{groupId:N}"),
            ParentReference: CatalogueImportEntityReference.Existing(productId, $"product:{productId:N}"));

        var issues = CatalogueImportCommitValidator.Validate(
            new CatalogueImportPlan(CatalogueImportMode.Update, [operation], [], [], "synthetic"), Baseline(categoryId, productId, groupId));

        CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), "parent-payload-mismatch");
    }

    private static CatalogueImportOperation Existing(CatalogueImportOperationKind kind, Guid id, IReadOnlyDictionary<string, string?> values, Guid categoryId) =>
        new(CatalogueImportEntityType.Product, kind, id, $"product:{id:N}", 2, "Products", values,
            CatalogueImportEntityReference.Existing(id, $"product:{id:N}"),
            CatalogueImportEntityReference.Existing(categoryId, $"category:{categoryId:N}"));

    private static Dictionary<string, string?> ProductValues(string code, string name) => new()
    {
        ["code"] = code, ["name"] = name, ["category"] = "Plats", ["categoryShortCode"] = null,
        ["priceCents"] = "100", ["vatRate"] = "20", ["isActive"] = "true", ["discountEligible"] = "true", ["optionsEnabled"] = "false"
    };

    private static CatalogueImportBaseline Baseline(Guid categoryId, Guid productId, Guid? groupId = null) =>
        new([new(categoryId, "Plats", null)], [Product(productId, categoryId, "P-1", "One", groupId)]);

    private static CatalogueImportBaselineProduct Product(Guid id, Guid categoryId, string code, string name, Guid? groupId = null) =>
        new(id, code, name, categoryId, "Plats", null, Money.FromCents(100), 20m, false, true, false,
            groupId is null ? [] : [new(groupId.Value, id, "Extras", SelectionMode.Multi, false, 0, 1, 0, [])]);
}
