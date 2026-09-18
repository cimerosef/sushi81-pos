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

        var rich = RichBaseline(categoryA, productA, Guid.Parse("00000000-0000-0000-0000-000000000006"), Guid.Parse("00000000-0000-0000-0000-000000000007"), "PL");
        var richProduct = rich.Products.Single();
        var richGroup = richProduct.OptionGroups.Single();
        var richReordered = rich with
        {
            Categories = rich.Categories.Reverse().ToArray(),
            Products = [richProduct with { OptionGroups = [richGroup with { Options = richGroup.Options.Reverse().ToArray() }] }]
        };
        var changedParent = rich with
        {
            Products = [richProduct with
            {
                OptionGroups = [richGroup with
                {
                    ProductId = categoryB,
                    Options = [richGroup.Options.Single() with { OptionGroupId = categoryB }]
                }]
            }]
        };
        Assert.AreEqual(CatalogueImportBaselineFingerprint.Compute(rich), CatalogueImportBaselineFingerprint.Compute(richReordered));
        Assert.AreNotEqual(CatalogueImportBaselineFingerprint.Compute(rich), CatalogueImportBaselineFingerprint.Compute(changedParent));
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

    [TestMethod]
    public void DuplicateCreateModifyAndStateActionsAreRejected()
    {
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var baseline = Baseline(categoryId, productId);
        var cases = new[]
        {
            new[] { CreateProduct("product:new:a", categoryId), CreateProduct("product:new:a", categoryId) },
            new[] { Existing(CatalogueImportOperationKind.Modify, productId, ProductValues("P-2", "Two"), categoryId), Existing(CatalogueImportOperationKind.Modify, productId, ProductValues("P-2", "Two"), categoryId) },
            new[] { Existing(CatalogueImportOperationKind.Activate, productId, With(ProductValues("P-1", "One"), "isActive", "true"), categoryId), Existing(CatalogueImportOperationKind.Activate, productId, With(ProductValues("P-1", "One"), "isActive", "true"), categoryId) },
            new[] { Existing(CatalogueImportOperationKind.Deactivate, productId, With(ProductValues("P-1", "One"), "isActive", "false"), categoryId), Existing(CatalogueImportOperationKind.Deactivate, productId, With(ProductValues("P-1", "One"), "isActive", "false"), categoryId) },
        };

        foreach (var operations in cases)
        {
            var issues = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, operations, [], [], "synthetic"), baseline);
            Assert.IsTrue(issues.Any(issue => issue.Code is "duplicate-operation" or "duplicate-create" or "duplicate-modify" or "duplicate-state-operation"), string.Join(";", issues.Select(issue => issue.Code)));
        }
    }

    [TestMethod]
    public void StateReferenceAndParentMatrixFailsClosed()
    {
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var groupId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var optionId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var baseline = RichBaseline(categoryId, productId, groupId, optionId, "PL");
        var productModifyState = Existing(CatalogueImportOperationKind.Modify, productId, With(ProductValues("P-1", "One"), "isActive", "true"), categoryId);
        var optionModifyState = OptionOperation(CatalogueImportOperationKind.Modify, optionId, groupId, "Sauce", false) with { Values = OptionValues("Sauce", false, "true") };
        var productNonStateFromState = Existing(CatalogueImportOperationKind.Activate, productId, With(ProductValues("P-2", "Two"), "isActive", "true"), categoryId);
        var optionNonStateFromState = OptionOperation(CatalogueImportOperationKind.Activate, optionId, groupId, "Changed", false) with { Values = OptionValues("Changed", false, "true") };
        var activateMismatch = Existing(CatalogueImportOperationKind.Activate, productId, With(ProductValues("P-1", "One"), "isActive", "false"), categoryId);
        var deactivateMismatch = Existing(CatalogueImportOperationKind.Deactivate, productId, With(ProductValues("P-1", "One"), "isActive", "true"), categoryId);
        var missingCategory = Existing(CatalogueImportOperationKind.Modify, productId, ProductValues("P-1", "One"), categoryId) with { CategoryReference = null };
        var mistypedCategory = Existing(CatalogueImportOperationKind.Modify, productId, ProductValues("P-1", "One"), categoryId) with { CategoryReference = CatalogueImportEntityReference.Existing(categoryId, "category:deadbeef") };
        var missingProductParent = OptionGroupOperation(CatalogueImportOperationKind.Modify, groupId, productId, "P-1") with { ParentReference = null };
        var mistypedProductParent = OptionGroupOperation(CatalogueImportOperationKind.Modify, groupId, productId, "P-1") with { ParentReference = CatalogueImportEntityReference.Existing(productId, "product:deadbeef") };
        var missingGroupParent = OptionOperation(CatalogueImportOperationKind.Modify, optionId, groupId, "Sauce", false) with { ParentReference = null };
        var mistypedGroupParent = OptionOperation(CatalogueImportOperationKind.Modify, optionId, groupId, "Sauce", false) with { ParentReference = CatalogueImportEntityReference.Existing(groupId, "group:deadbeef") };

        var cases = new[]
        {
            (productModifyState, "state-operation-missing"), (optionModifyState, "state-operation-missing"),
            (productNonStateFromState, "modify-missing"), (optionNonStateFromState, "modify-missing"),
            (activateMismatch, "state-payload-mismatch"), (deactivateMismatch, "state-payload-mismatch"),
            (missingCategory, "category-reference-missing"), (mistypedCategory, "unknown-category-reference"),
            (missingProductParent, "parent-reference-missing"), (mistypedProductParent, "unknown-parent-reference"),
            (missingGroupParent, "parent-reference-missing"), (mistypedGroupParent, "unknown-parent-reference"),
        };
        foreach (var (operation, code) in cases)
        {
            var issues = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [operation], [], [], "synthetic"), baseline);
            CollectionAssert.Contains(issues.Select(issue => issue.Code).ToArray(), code, operation.EntityType + ":" + string.Join(",", issues.Select(issue => issue.Code)));
        }
    }

    [TestMethod]
    public void CategoryPayloadOrphanAndCombinedOptionEvidenceAreBlockingAndOrderIndependent()
    {
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var groupId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var optionId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var baseline = RichBaseline(categoryId, productId, groupId, optionId, "PL");
        var orphan = new CatalogueImportPlan(CatalogueImportMode.Update, [], [], [new("category:new:orphan", "Desserts", "DE")], "synthetic");
        CollectionAssert.Contains(CatalogueImportCommitValidator.Validate(orphan, baseline).Select(issue => issue.Code).ToArray(), "orphan-category");

        var nameMismatch = Existing(CatalogueImportOperationKind.Modify, productId, With(ProductValues("P-1", "One"), "category", "Desserts"), categoryId);
        var shortCodeMismatch = Existing(CatalogueImportOperationKind.Modify, productId, With(ProductValues("P-1", "One"), "categoryShortCode", "DE"), categoryId);
        var nameIssues = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [nameMismatch], [], [], "synthetic"), baseline);
        var codeIssues = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [shortCodeMismatch], [], [], "synthetic"), baseline);
        CollectionAssert.Contains(nameIssues.Select(issue => issue.Code).ToArray(), "category-payload-mismatch");
        CollectionAssert.Contains(codeIssues.Select(issue => issue.Code).ToArray(), "category-short-code-mismatch");

        var modify = OptionOperation(CatalogueImportOperationKind.Modify, optionId, groupId, "Sauce", false);
        var activateDifferent = OptionOperation(CatalogueImportOperationKind.Activate, optionId, groupId, "Changed", true);
        var forward = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [modify, activateDifferent], [], [], "synthetic"), baseline);
        var reverse = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [activateDifferent, modify], [], [], "synthetic"), baseline);
        CollectionAssert.Contains(forward.Select(issue => issue.Code).ToArray(), "contradictory-operation");
        CollectionAssert.AreEqual(forward.Select(issue => issue.Code + "|" + issue.Message).ToArray(), reverse.Select(issue => issue.Code + "|" + issue.Message).ToArray());

        var alteredReferences = activateDifferent with { ParentReference = CatalogueImportEntityReference.Existing(groupId, "group:other") };
        var refIssues = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [modify, alteredReferences], [], [], "synthetic"), baseline);
        CollectionAssert.Contains(refIssues.Select(issue => issue.Code).ToArray(), "contradictory-operation");
    }

    [TestMethod]
    public void ActivateAndDeactivateAndContradictoryOptionReferencesAreOrderIndependent()
    {
        var categoryId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var productId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var groupId = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var optionId = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var baseline = RichBaseline(categoryId, productId, groupId, optionId, "PL");
        var activate = OptionOperation(CatalogueImportOperationKind.Activate, optionId, groupId, "Sauce", true);
        var deactivate = OptionOperation(CatalogueImportOperationKind.Deactivate, optionId, groupId, "Sauce", false);
        var stateForward = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [activate, deactivate], [], [], "synthetic"), baseline);
        var stateReverse = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [deactivate, activate], [], [], "synthetic"), baseline);
        CollectionAssert.Contains(stateForward.Select(issue => issue.Code).ToArray(), "contradictory-state-operation");
        CollectionAssert.AreEqual(stateForward.Select(issue => issue.Code + "|" + issue.Message).ToArray(), stateReverse.Select(issue => issue.Code + "|" + issue.Message).ToArray());

        var modify = OptionOperation(CatalogueImportOperationKind.Modify, optionId, groupId, "Sauce", false);
        var wrongReference = activate with { ParentReference = CatalogueImportEntityReference.Existing(groupId, "group:wrong") };
        var referencesForward = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [modify, wrongReference], [], [], "synthetic"), baseline);
        var referencesReverse = CatalogueImportCommitValidator.Validate(new CatalogueImportPlan(CatalogueImportMode.Update, [wrongReference, modify], [], [], "synthetic"), baseline);
        CollectionAssert.Contains(referencesForward.Select(issue => issue.Code).ToArray(), "contradictory-operation");
        CollectionAssert.AreEqual(referencesForward.Select(issue => issue.Code + "|" + issue.Message).ToArray(), referencesReverse.Select(issue => issue.Code + "|" + issue.Message).ToArray());
    }

    private static CatalogueImportOperation Existing(CatalogueImportOperationKind kind, Guid id, IReadOnlyDictionary<string, string?> values, Guid categoryId) =>
        new(CatalogueImportEntityType.Product, kind, id, $"product:{id:N}", 2, "Products", values,
            CatalogueImportEntityReference.Existing(id, $"product:{id:N}"),
            CatalogueImportEntityReference.Existing(categoryId, $"category:{categoryId:N}"));

    private static CatalogueImportOperation CreateProduct(string localKey, Guid categoryId) =>
        new(CatalogueImportEntityType.Product, CatalogueImportOperationKind.Create, null, localKey, 2, "Products", ProductValues("P-new", "New"),
            CatalogueImportEntityReference.New(localKey), CatalogueImportEntityReference.Existing(categoryId, $"category:{categoryId:N}"));

    private static CatalogueImportOperation OptionGroupOperation(CatalogueImportOperationKind kind, Guid groupId, Guid productId, string productCode) =>
        new(CatalogueImportEntityType.OptionGroup, kind, groupId, $"group:{groupId:N}", 2, "OptionGroups",
            new Dictionary<string, string?> { ["productCode"] = productCode, ["name"] = "Extras", ["selectionMode"] = "MULTI", ["isRequired"] = "false", ["minSelections"] = "0", ["maxSelections"] = "1", ["displayOrder"] = "0" },
            CatalogueImportEntityReference.Existing(groupId, $"group:{groupId:N}"), ParentReference: CatalogueImportEntityReference.Existing(productId, $"product:{productId:N}"));

    private static CatalogueImportOperation OptionOperation(CatalogueImportOperationKind kind, Guid optionId, Guid groupId, string name, bool active) =>
        new(CatalogueImportEntityType.Option, kind, optionId, $"option:{optionId:N}", 2, "Options", OptionValues(name, false, active.ToString()),
            CatalogueImportEntityReference.Existing(optionId, $"option:{optionId:N}"), ParentReference: CatalogueImportEntityReference.Existing(groupId, $"group:{groupId:N}"));

    private static Dictionary<string, string?> OptionValues(string name, bool active, string? activeOverride = null) => new()
    {
        ["name"] = name, ["priceAdjustmentCents"] = "0", ["isActive"] = activeOverride ?? active.ToString(), ["displayOrder"] = "0"
    };

    private static Dictionary<string, string?> ProductValues(string code, string name) => new()
    {
        ["code"] = code, ["name"] = name, ["category"] = "Plats", ["categoryShortCode"] = null,
        ["priceCents"] = "100", ["vatRate"] = "20", ["isActive"] = "true", ["discountEligible"] = "true", ["optionsEnabled"] = "false"
    };

    private static Dictionary<string, string?> With(IReadOnlyDictionary<string, string?> source, string key, string? value)
    {
        var copy = new Dictionary<string, string?>(source, StringComparer.Ordinal);
        copy[key] = value;
        return copy;
    }

    private static CatalogueImportBaseline Baseline(Guid categoryId, Guid productId, Guid? groupId = null) =>
        new([new(categoryId, "Plats", null)], [Product(productId, categoryId, "P-1", "One", groupId)]);

    private static CatalogueImportBaseline RichBaseline(Guid categoryId, Guid productId, Guid groupId, Guid optionId, string? shortCode) =>
        new([new(categoryId, "Plats", shortCode)],
            [new(productId, "P-1", "One", categoryId, "Plats", shortCode, Money.FromCents(100), 20m, false, true, true,
                [new(groupId, productId, "Extras", SelectionMode.Multi, false, 0, 1, 0,
                    [new(optionId, groupId, "Sauce", Money.Zero, false, 0)])])]);

    private static CatalogueImportBaselineProduct Product(Guid id, Guid categoryId, string code, string name, Guid? groupId = null) =>
        new(id, code, name, categoryId, "Plats", null, Money.FromCents(100), 20m, false, true, false,
            groupId is null ? [] : [new(groupId.Value, id, "Extras", SelectionMode.Multi, false, 0, 1, 0, [])]);
}
