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
