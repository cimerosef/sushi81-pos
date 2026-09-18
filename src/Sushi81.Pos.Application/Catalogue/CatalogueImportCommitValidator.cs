using System.Globalization;
using Sushi81.Pos.Domain;

#pragma warning disable CA1859

namespace Sushi81.Pos.Application.Catalogue;

/// <summary>
/// Pure commit-boundary validation for an immutable import plan.  The SQLite
/// store calls this validator after reading its same-transaction baseline so
/// persistence cannot apply a looser interpretation than the preview planner.
/// </summary>
public static class CatalogueImportCommitValidator
{
    public static IReadOnlyList<CatalogueImportIssue> Validate(CatalogueImportPlan plan, CatalogueImportBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(baseline);
        var issues = new List<CatalogueImportIssue>();
        var categories = BuildMap(baseline.Categories ?? [], value => value.Id, issues, "duplicate-baseline-category-id");
        var products = BuildMap(baseline.Products ?? [], value => value.Id, issues, "duplicate-baseline-product-id");
        var groups = BuildMap((baseline.Products ?? []).SelectMany(value => value.OptionGroups), value => value.Id, issues, "duplicate-baseline-group-id");
        var options = BuildMap((baseline.Products ?? []).SelectMany(value => value.OptionGroups).SelectMany(value => value.Options), value => value.Id, issues, "duplicate-baseline-option-id");
        var operations = plan.Operations ?? [];

        if (!Enum.IsDefined(plan.Mode)) Add(issues, "invalid-mode", "The import mode is invalid.");
        ValidateCombinedOperations(operations, issues);
        ValidateCategories(plan, baseline, categories, operations, issues);
        ValidateOperationReferences(plan, categories, products, groups, options, issues);
        ValidateOperations(plan, baseline, categories, products, groups, options, issues);
        ValidateResultingState(plan, baseline, categories, products, groups, options, issues);

        return issues
            .OrderBy(issue => issue.Worksheet ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(issue => issue.ExcelRow ?? 0)
            .ThenBy(issue => issue.FieldKey ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.Message, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateCategories(CatalogueImportPlan plan, CatalogueImportBaseline baseline,
        IReadOnlyDictionary<Guid, CatalogueImportCategory> current, IReadOnlyList<CatalogueImportOperation> operations,
        List<CatalogueImportIssue> issues)
    {
        var planned = (plan.NewCategories ?? []).ToArray();
        var plannedKeys = new HashSet<string>(StringComparer.Ordinal);
        var plannedNames = new HashSet<string>(StringComparer.Ordinal);
        var plannedCodes = new HashSet<string>(StringComparer.Ordinal);
        var currentNames = (baseline.Categories ?? []).Select(value => value.NormalizedName).ToHashSet(StringComparer.Ordinal);
        var currentCodes = (baseline.Categories ?? []).Where(value => value.NormalizedShortCode is not null).Select(value => value.NormalizedShortCode!).ToHashSet(StringComparer.Ordinal);
        foreach (var category in planned)
        {
            if (string.IsNullOrWhiteSpace(category.LocalKey) || !category.LocalKey.StartsWith("category:new:", StringComparison.Ordinal) || !plannedKeys.Add(category.LocalKey))
                Add(issues, "invalid-category-key", "A planned Category local key is missing, mistyped or duplicated.");
            var name = CatalogueNormalization.Display(category.Name);
            if (name.Length == 0 || !plannedNames.Add(category.NormalizedName) || currentNames.Contains(category.NormalizedName))
                Add(issues, "category-name-duplicate", "A planned Category name is empty, duplicated or already exists.");
            if (CatalogueValidation.ValidateCategoryShortCode(category.ShortCode) is { } error)
                Add(issues, "category-short-code-invalid", error);
            if (category.NormalizedShortCode is { } code && (!plannedCodes.Add(code) || currentCodes.Contains(code)))
                Add(issues, "category-short-code-duplicate", "A planned Category short code is duplicated or already exists.");
        }

        foreach (var duplicate in (baseline.Categories ?? []).GroupBy(value => value.NormalizedName, StringComparer.Ordinal).Where(value => value.Count() > 1))
            Add(issues, "duplicate-category-name", $"The current Catalogue contains duplicate normalized Category name '{duplicate.Key}'.");
        foreach (var duplicate in (baseline.Categories ?? []).Where(value => value.NormalizedShortCode is not null).GroupBy(value => value.NormalizedShortCode!, StringComparer.Ordinal).Where(value => value.Count() > 1))
            Add(issues, "category-short-code-duplicate", "The current Catalogue contains duplicate normalized Category short codes.");

        var referenced = operations.Where(value => value.EntityType == CatalogueImportEntityType.Product)
            .Select(value => value.CategoryReference)
            .Where(value => value is not null && !value.IsExisting)
            .Select(value => value!.LocalKey).ToHashSet(StringComparer.Ordinal);
        foreach (var category in planned.Where(value => !referenced.Contains(value.LocalKey)))
            Add(issues, "orphan-category", "A planned Category is not referenced by a Product operation.");
    }

    private static void ValidateOperationReferences(CatalogueImportPlan plan,
        IReadOnlyDictionary<Guid, CatalogueImportCategory> categories,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineProduct> products,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineOptionGroup> groups,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineOption> options,
        List<CatalogueImportIssue> issues)
    {
        var planCategories = FirstByKey(plan.NewCategories ?? [], value => value.LocalKey);
        var productCreates = FirstByKey((plan.Operations ?? []).Where(value => value.EntityType == CatalogueImportEntityType.Product && value.Kind == CatalogueImportOperationKind.Create), value => value.LocalKey);
        var productOperations = (plan.Operations ?? []).Where(value => value.EntityType == CatalogueImportEntityType.Product).GroupBy(value => value.LocalKey, StringComparer.Ordinal).ToDictionary(value => value.Key, value => value.First(), StringComparer.Ordinal);
        var groupCreates = FirstByKey((plan.Operations ?? []).Where(value => value.EntityType == CatalogueImportEntityType.OptionGroup && value.Kind == CatalogueImportOperationKind.Create), value => value.LocalKey);
        foreach (var operation in plan.Operations ?? [])
        {
            var prefix = operation.EntityType switch
            {
                CatalogueImportEntityType.Product => "product:",
                CatalogueImportEntityType.OptionGroup => "group:",
                CatalogueImportEntityType.Option => "option:",
                _ => "category:"
            };
            if (string.IsNullOrWhiteSpace(operation.LocalKey))
                Add(issues, "invalid-local-key", "Operation local keys must be non-empty.", operation);
            if (operation.EntityType == CatalogueImportEntityType.Category)
                Add(issues, "category-operation-forbidden", "Categories are created only through the planned Category list.", operation);
            if (!Enum.IsDefined(operation.EntityType) || !Enum.IsDefined(operation.Kind))
            {
                Add(issues, "invalid-operation", "The import plan contains an unknown entity or operation kind.", operation);
                continue;
            }
            var isCreate = operation.Kind == CatalogueImportOperationKind.Create;
            if (isCreate != (operation.EntityId is null)) Add(issues, "invalid-operation-id", "Create and existing operation IDs do not match their operation kind.", operation);
            if (operation.EntityReference is null || operation.EntityReference.LocalKey != operation.LocalKey || operation.EntityReference.ExistingId != operation.EntityId)
                Add(issues, "misbound-reference", "The operation entity reference must exactly match its local key and durable id.", operation);
            if (isCreate)
            {
                if (!operation.LocalKey.StartsWith(prefix + "new:", StringComparison.Ordinal)) Add(issues, "invalid-local-key", "Create local key has the wrong entity namespace.", operation);
            }
            else if (operation.EntityId is null || !operation.LocalKey.Equals(prefix + operation.EntityId.Value.ToString("N"), StringComparison.Ordinal))
                Add(issues, "invalid-local-key", "Existing local key is not the canonical entity key.", operation);
            if (plan.Mode == CatalogueImportMode.AddOnly && !isCreate) Add(issues, "add-only-existing-binding", "Add-only operations cannot target existing entities.", operation);

            if (operation.EntityId is { } entityId)
            {
                var present = operation.EntityType switch
                {
                    CatalogueImportEntityType.Product => products.ContainsKey(entityId),
                    CatalogueImportEntityType.OptionGroup => groups.ContainsKey(entityId),
                    CatalogueImportEntityType.Option => options.ContainsKey(entityId),
                    _ => false
                };
                if (!present) Add(issues, "unknown-entity-id", "Operation identity is not present in the current Catalogue.", operation);
            }

            if (operation.EntityType == CatalogueImportEntityType.Product)
            {
                if (operation.CategoryReference is null) Add(issues, "category-reference-missing", "Every Product operation requires an explicit Category reference.", operation);
                else ValidateCategoryReference(operation, operation.CategoryReference, categories, planCategories, issues);
                if (operation.ParentReference is not null) Add(issues, "unexpected-parent-reference", "Product operations cannot carry a parent reference.", operation);
            }
            else if (operation.EntityType == CatalogueImportEntityType.OptionGroup)
            {
                if (operation.CategoryReference is not null) Add(issues, "unexpected-category-reference", "OptionGroup operations cannot carry a Category reference.", operation);
                if (operation.ParentReference is null) Add(issues, "parent-reference-missing", "Every OptionGroup operation requires an explicit Product parent.", operation);
                else ValidateProductParent(operation, operation.ParentReference, products, productCreates, productOperations, issues);
                if (operation.EntityId is { } groupId && groups.TryGetValue(groupId, out var existingGroup) && operation.ParentReference?.ExistingId != existingGroup.ProductId)
                    Add(issues, "reparent-forbidden", "An existing OptionGroup cannot be re-parented by import.", operation);
            }
            else if (operation.EntityType == CatalogueImportEntityType.Option)
            {
                if (operation.CategoryReference is not null) Add(issues, "unexpected-category-reference", "Option operations cannot carry a Category reference.", operation);
                if (operation.ParentReference is null) Add(issues, "parent-reference-missing", "Every Option operation requires an explicit OptionGroup parent.", operation);
                else ValidateGroupParent(operation, operation.ParentReference, groups, groupCreates, issues);
                if (operation.EntityId is { } optionId && options.TryGetValue(optionId, out var existingOption) && operation.ParentReference?.ExistingId != existingOption.OptionGroupId)
                    Add(issues, "reparent-forbidden", "An existing Option cannot be re-parented by import.", operation);
            }
            ValidateScalarPayload(operation, issues);
        }
    }

    private static void ValidateScalarPayload(CatalogueImportOperation operation, List<CatalogueImportIssue> issues)
    {
        string[] required = operation.EntityType switch
        {
            CatalogueImportEntityType.Product => ["code", "name", "category", "priceCents", "vatRate", "isActive", "discountEligible", "optionsEnabled"],
            CatalogueImportEntityType.OptionGroup => ["productCode", "name", "selectionMode", "isRequired", "displayOrder"],
            CatalogueImportEntityType.Option => ["name", "priceAdjustmentCents", "isActive", "displayOrder"],
            _ => []
        };
        foreach (var key in required)
            if (string.IsNullOrWhiteSpace(Get(operation, key))) Add(issues, "invalid-scalar", $"Required import value '{key}' is missing.", operation);
        if (operation.EntityType == CatalogueImportEntityType.Product)
        {
            if (!long.TryParse(Get(operation, "priceCents"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) Add(issues, "invalid-scalar", "Product price cents is malformed.", operation);
            if (!decimal.TryParse(Get(operation, "vatRate"), NumberStyles.Number, CultureInfo.InvariantCulture, out _)) Add(issues, "invalid-scalar", "Product VAT rate is malformed.", operation);
            if (!bool.TryParse(Get(operation, "isActive"), out _) || !bool.TryParse(Get(operation, "discountEligible"), out _) || !bool.TryParse(Get(operation, "optionsEnabled"), out _)) Add(issues, "invalid-scalar", "Product boolean value is malformed.", operation);
        }
        else if (operation.EntityType == CatalogueImportEntityType.OptionGroup)
        {
            if (!string.Equals(Get(operation, "selectionMode"), "SINGLE", StringComparison.OrdinalIgnoreCase) && !string.Equals(Get(operation, "selectionMode"), "MULTI", StringComparison.OrdinalIgnoreCase)) Add(issues, "invalid-scalar", "OptionGroup selection mode is malformed.", operation);
            if (!bool.TryParse(Get(operation, "isRequired"), out _)) Add(issues, "invalid-scalar", "OptionGroup required value is malformed.", operation);
            if (!int.TryParse(Get(operation, "displayOrder"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) Add(issues, "invalid-scalar", "OptionGroup display order is malformed.", operation);
            foreach (var key in new[] { "minSelections", "maxSelections" })
                if (!string.IsNullOrWhiteSpace(Get(operation, key)) && !int.TryParse(Get(operation, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) Add(issues, "invalid-scalar", "OptionGroup selection limit is malformed.", operation);
        }
        else if (operation.EntityType == CatalogueImportEntityType.Option)
        {
            if (!long.TryParse(Get(operation, "priceAdjustmentCents"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _) || !bool.TryParse(Get(operation, "isActive"), out _) || !int.TryParse(Get(operation, "displayOrder"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) Add(issues, "invalid-scalar", "Option scalar value is malformed.", operation);
        }
    }

    private static void ValidateCategoryReference(CatalogueImportOperation operation, CatalogueImportEntityReference reference,
        IReadOnlyDictionary<Guid, CatalogueImportCategory> categories, IReadOnlyDictionary<string, CatalogueImportPlannedCategory> planned,
        List<CatalogueImportIssue> issues)
    {
        CatalogueImportCategory? category = null;
        if (reference.IsExisting)
        {
            if (reference.ExistingId is null || !categories.TryGetValue(reference.ExistingId.Value, out category) || reference.LocalKey != "category:" + reference.ExistingId.Value.ToString("N"))
                Add(issues, "unknown-category-reference", "Existing Category reference is not canonical or is missing from the baseline.", operation);
        }
        else if (!reference.LocalKey.StartsWith("category:new:", StringComparison.Ordinal) || !planned.TryGetValue(reference.LocalKey, out var plannedCategory))
            Add(issues, "unknown-category-reference", "New Category reference does not resolve to a planned Category.", operation);
        else
            ValidateCategoryPayload(operation, plannedCategory.Name, plannedCategory.ShortCode, issues);

        if (category is not null) ValidateCategoryPayload(operation, category.Name, category.ShortCode, issues);
    }

    private static void ValidateCategoryPayload(CatalogueImportOperation operation, string name, string? shortCode, List<CatalogueImportIssue> issues)
    {
        var value = Get(operation, "category");
        if (value is null || !string.Equals(CatalogueNormalization.Key(value), CatalogueNormalization.Key(name), StringComparison.Ordinal))
            Add(issues, "category-payload-mismatch", "Product Category display data does not match its explicit Category reference.", operation);
        var code = Get(operation, "categoryShortCode");
        var expected = string.IsNullOrWhiteSpace(shortCode) ? null : CatalogueNormalization.Key(shortCode);
        var actual = string.IsNullOrWhiteSpace(code) ? null : CatalogueNormalization.Key(code);
        if (!string.Equals(actual, expected, StringComparison.Ordinal)) Add(issues, "category-short-code-mismatch", "Product Category short-code data does not match its explicit Category reference.", operation);
    }

    private static void ValidateProductParent(CatalogueImportOperation operation, CatalogueImportEntityReference reference,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineProduct> products, IReadOnlyDictionary<string, CatalogueImportOperation> creates,
        IReadOnlyDictionary<string, CatalogueImportOperation> productOperations, List<CatalogueImportIssue> issues)
    {
        if (reference.IsExisting)
        {
            if (reference.ExistingId is null || !products.ContainsKey(reference.ExistingId.Value) || reference.LocalKey != "product:" + reference.ExistingId.Value.ToString("N"))
                Add(issues, "unknown-parent-reference", "Existing Product parent reference is not canonical or is missing.", operation);
        }
        else if (!reference.LocalKey.StartsWith("product:new:", StringComparison.Ordinal) || !creates.ContainsKey(reference.LocalKey))
            Add(issues, "unknown-parent-reference", "New Product parent reference does not resolve to a Product Create operation.", operation);
        var expected = ResolveFinalProductCode(reference, products, productOperations);
        if (expected is not null && !string.Equals(CatalogueNormalization.Key(Get(operation, "productCode")), CatalogueNormalization.Key(expected), StringComparison.Ordinal))
            Add(issues, "parent-payload-mismatch", "OptionGroup Product display data does not match its explicit parent reference.", operation);
    }

    private static void ValidateGroupParent(CatalogueImportOperation operation, CatalogueImportEntityReference reference,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineOptionGroup> groups, IReadOnlyDictionary<string, CatalogueImportOperation> creates, List<CatalogueImportIssue> issues)
    {
        if (reference.IsExisting)
        {
            if (reference.ExistingId is null || !groups.ContainsKey(reference.ExistingId.Value) || reference.LocalKey != "group:" + reference.ExistingId.Value.ToString("N"))
                Add(issues, "unknown-parent-reference", "Existing OptionGroup parent reference is not canonical or is missing.", operation);
        }
        else if (!reference.LocalKey.StartsWith("group:new:", StringComparison.Ordinal) || !creates.ContainsKey(reference.LocalKey))
            Add(issues, "unknown-parent-reference", "New OptionGroup parent reference does not resolve to a Group Create operation.", operation);
    }

    private static string? ResolveFinalProductCode(CatalogueImportEntityReference reference,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineProduct> products, IReadOnlyDictionary<string, CatalogueImportOperation> productOperations)
    {
        if (reference.IsExisting && reference.ExistingId is { } id && products.TryGetValue(id, out var current))
        {
            var operation = productOperations.Values.FirstOrDefault(value => value.EntityId == id);
            return operation is null ? current.Code : Get(operation, "code") ?? current.Code;
        }
        return productOperations.TryGetValue(reference.LocalKey, out var created) ? Get(created, "code") : null;
    }

    private static void ValidateOperations(CatalogueImportPlan plan, CatalogueImportBaseline baseline,
        IReadOnlyDictionary<Guid, CatalogueImportCategory> categories,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineProduct> products,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineOptionGroup> groups,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineOption> options,
        List<CatalogueImportIssue> issues)
    {
        foreach (var entityGroup in (plan.Operations ?? []).GroupBy(value => (value.EntityType, value.LocalKey)))
        {
            var set = entityGroup.ToArray();
            var canonical = CanonicalOperation(set);
            if (set.Select(value => value.Kind).Distinct().Count() != set.Length)
                Add(issues, "duplicate-operation", "An entity contains a duplicate operation kind.", canonical);
            if (set.Count(value => value.Kind == CatalogueImportOperationKind.Create) > 1)
                Add(issues, "duplicate-create", "An entity has more than one Create operation.", canonical);
            if (set.Any(value => value.Kind == CatalogueImportOperationKind.Create) && set.Length > 1)
                Add(issues, "mixed-create", "Create cannot be mixed with another operation kind.", canonical);
            if (canonical.EntityType == CatalogueImportEntityType.OptionGroup && set.Any(value => value.Kind is CatalogueImportOperationKind.Activate or CatalogueImportOperationKind.Deactivate))
                Add(issues, "invalid-group-action", "OptionGroup does not support Activate or Deactivate operations.", canonical);
            if (set.Count(value => value.Kind is CatalogueImportOperationKind.Activate or CatalogueImportOperationKind.Deactivate) > 1)
                Add(issues, "duplicate-state-operation", "An entity cannot carry duplicate or contradictory state operations.", canonical);
            if (set.Any(value => value.Kind == CatalogueImportOperationKind.Activate) && set.Any(value => value.Kind == CatalogueImportOperationKind.Deactivate))
                Add(issues, "contradictory-state-operation", "Activate and Deactivate cannot be combined.", canonical);

            if (canonical.EntityId is not { } id) continue;
            if (canonical.EntityType == CatalogueImportEntityType.Product && products.TryGetValue(id, out var product)) ValidateProductDelta(set, canonical, product, categories, issues);
            if (canonical.EntityType == CatalogueImportEntityType.OptionGroup && groups.TryGetValue(id, out var group)) ValidateGroupDelta(set, canonical, group, products, issues);
            if (canonical.EntityType == CatalogueImportEntityType.Option && options.TryGetValue(id, out var option)) ValidateOptionDelta(set, canonical, option, issues);
        }
    }

    private static void ValidateCombinedOperations(IReadOnlyList<CatalogueImportOperation> operations, List<CatalogueImportIssue> issues)
    {
        foreach (var group in operations.GroupBy(value => (value.EntityType, value.LocalKey)))
        {
            var canonical = CanonicalOperation(group);
            foreach (var operation in group)
            {
                var samePayload = operation.EntityId == canonical.EntityId && Equals(operation.EntityReference, canonical.EntityReference)
                    && Equals(operation.CategoryReference, canonical.CategoryReference) && Equals(operation.ParentReference, canonical.ParentReference)
                    && ValuesEqual(operation.Values, canonical.Values);
                if (!samePayload)
                    Add(issues, "contradictory-operation", "Operations for one entity must carry one canonical final payload and reference set; only the operation kind may differ.", operation);
            }
        }
    }

    private static CatalogueImportOperation CanonicalOperation(IEnumerable<CatalogueImportOperation> operations) =>
        operations.OrderBy(OperationPayload, StringComparer.Ordinal).ThenBy(value => value.Kind).First();

    private static string OperationPayload(CatalogueImportOperation operation)
    {
        var values = string.Join("\u001f", (operation.Values ?? new Dictionary<string, string?>()).OrderBy(value => value.Key, StringComparer.Ordinal).Select(value => value.Key + "=" + value.Value));
        return string.Join("\u001e", operation.EntityType, operation.LocalKey, operation.EntityId?.ToString("N") ?? string.Empty,
            operation.EntityReference?.ExistingId?.ToString("N") ?? string.Empty, operation.EntityReference?.LocalKey ?? string.Empty,
            operation.CategoryReference?.ExistingId?.ToString("N") ?? string.Empty, operation.CategoryReference?.LocalKey ?? string.Empty,
            operation.ParentReference?.ExistingId?.ToString("N") ?? string.Empty, operation.ParentReference?.LocalKey ?? string.Empty, values);
    }

    private static void ValidateProductDelta(IReadOnlyList<CatalogueImportOperation> operations, CatalogueImportOperation canonical, CatalogueImportBaselineProduct current,
        IReadOnlyDictionary<Guid, CatalogueImportCategory> categories, List<CatalogueImportIssue> issues)
    {
        var desiredCode = Get(canonical, "code");
        var desiredName = Get(canonical, "name");
        var desiredCategory = ResolveCategory(canonical, categories);
        var nonActiveChanged = desiredCode is null || desiredName is null || desiredCategory is null ||
            !string.Equals(CatalogueNormalization.Display(desiredCode), current.Code, StringComparison.Ordinal) ||
            !string.Equals(CatalogueNormalization.Display(desiredName), current.Name, StringComparison.Ordinal) ||
            desiredCategory.Value != current.CategoryId || ParseMoney(canonical, "priceCents") != current.PriceTtc.Cents || ParseDecimal(canonical, "vatRate") != current.VatRate ||
            ParseBool(canonical, "discountEligible") != current.DiscountEligible || ParseBool(canonical, "optionsEnabled") != current.OptionsEnabled;
        RequireModify(operations, nonActiveChanged, issues, "Product", canonical);
        ValidateState(operations, current.IsActive, ParseBool(canonical, "isActive"), issues, canonical);
    }

    private static void ValidateGroupDelta(IReadOnlyList<CatalogueImportOperation> operations, CatalogueImportOperation canonical, CatalogueImportBaselineOptionGroup current,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineProduct> products, List<CatalogueImportIssue> issues)
    {
        var mode = ParseMode(canonical, "selectionMode");
        var changed = !string.Equals(CatalogueNormalization.Display(Get(canonical, "name")), current.Name, StringComparison.Ordinal) || mode != current.SelectionMode || ParseBool(canonical, "isRequired") != current.IsRequired || ParseNullableInt(canonical, "minSelections") != current.MinSelections || ParseNullableInt(canonical, "maxSelections") != current.MaxSelections || ParseInt(canonical, "displayOrder") != current.DisplayOrder;
        RequireModify(operations, changed, issues, "OptionGroup", canonical);
    }

    private static void ValidateOptionDelta(IReadOnlyList<CatalogueImportOperation> operations, CatalogueImportOperation canonical, CatalogueImportBaselineOption current, List<CatalogueImportIssue> issues)
    {
        var changed = !string.Equals(CatalogueNormalization.Display(Get(canonical, "name")), current.Name, StringComparison.Ordinal) || ParseMoney(canonical, "priceAdjustmentCents") != current.PriceAdjustmentTtc.Cents || ParseInt(canonical, "displayOrder") != current.DisplayOrder;
        RequireModify(operations, changed, issues, "Option", canonical);
        ValidateState(operations, current.IsActive, ParseBool(canonical, "isActive"), issues, canonical);
    }

    private static void RequireModify(IReadOnlyList<CatalogueImportOperation> operations, bool changed, List<CatalogueImportIssue> issues, string entity, CatalogueImportOperation first)
    {
        var hasModify = operations.Any(value => value.Kind == CatalogueImportOperationKind.Modify);
        if (changed && !hasModify) Add(issues, "modify-missing", $"{entity} business changes require exactly one Modify operation.", first);
        if (!changed && hasModify) Add(issues, "modify-redundant", $"Unchanged {entity} cannot carry a redundant Modify operation.", first);
        if (operations.Count(value => value.Kind == CatalogueImportOperationKind.Modify) > 1) Add(issues, "duplicate-modify", $"{entity} cannot carry duplicate Modify operations.", first);
    }

    private static void ValidateState(IReadOnlyList<CatalogueImportOperation> operations, bool current, bool desired, List<CatalogueImportIssue> issues, CatalogueImportOperation canonical)
    {
        var state = operations.Where(value => value.Kind is CatalogueImportOperationKind.Activate or CatalogueImportOperationKind.Deactivate).OrderBy(OperationPayload, StringComparer.Ordinal).ThenBy(value => value.Kind).FirstOrDefault();
        if (current == desired)
        {
            if (state is not null) Add(issues, "state-redundant", "An unchanged active state cannot carry a state operation.", state);
        }
        else
        {
            var expected = desired ? CatalogueImportOperationKind.Activate : CatalogueImportOperationKind.Deactivate;
            if (state?.Kind != expected) Add(issues, "state-operation-missing", "Active-state changes require the matching state operation.", canonical);
        }
        if (state is not null && ParseBool(state, "isActive") != desired) Add(issues, "state-payload-mismatch", "State operation payload does not match its operation kind.", state);
    }

    private static void ValidateResultingState(CatalogueImportPlan plan, CatalogueImportBaseline baseline,
        IReadOnlyDictionary<Guid, CatalogueImportCategory> categories,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineProduct> products,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineOptionGroup> groups,
        IReadOnlyDictionary<Guid, CatalogueImportBaselineOption> options,
        List<CatalogueImportIssue> issues)
    {
        var productStates = products.Values.ToDictionary(value => value.Id, value => new ProductState(value.Code, value.Name, value.CategoryId, value.PriceTtc.Cents, value.VatRate, value.IsActive, value.DiscountEligible, value.OptionsEnabled));
        foreach (var group in (plan.Operations ?? []).Where(value => value.EntityType == CatalogueImportEntityType.Product).GroupBy(value => value.LocalKey))
        {
            var canonical = CanonicalOperation(group);
            var id = canonical.EntityId ?? DeterministicId(canonical.LocalKey);
            var category = ResolveCategory(canonical, categories) ?? DeterministicId(canonical.CategoryReference?.LocalKey ?? "category");
            productStates[id] = new(Get(canonical, "code") ?? string.Empty, Get(canonical, "name") ?? string.Empty, category, ParseMoney(canonical, "priceCents"), ParseDecimal(canonical, "vatRate"), ParseBool(canonical, "isActive"), ParseBool(canonical, "discountEligible"), ParseBool(canonical, "optionsEnabled"));
        }
        foreach (var duplicate in productStates.Values.GroupBy(value => CatalogueNormalization.Key(value.Code), StringComparer.Ordinal).Where(value => value.Key.Length == 0 || value.Count() > 1))
            Add(issues, "duplicate-product-code", "The resulting Catalogue contains a duplicate or empty Product code.");
        foreach (var value in productStates.Values)
            if (CatalogueValidation.ValidateProduct(value.Code, value.Name, value.CategoryId, Money.FromCents(value.PriceCents), value.VatRate) is { } error) Add(issues, "invalid-product", error);

        var groupStates = groups.Values.ToDictionary(value => value.Id, value => new GroupState(value.ProductId, value.Name, value.SelectionMode, value.IsRequired, value.MinSelections, value.MaxSelections, value.DisplayOrder));
        foreach (var group in (plan.Operations ?? []).Where(value => value.EntityType == CatalogueImportEntityType.OptionGroup).GroupBy(value => value.LocalKey))
        {
            var canonical = CanonicalOperation(group);
            groupStates[canonical.EntityId ?? DeterministicId(canonical.LocalKey)] = new(ResolveParentProduct(canonical), Get(canonical, "name") ?? string.Empty, ParseMode(canonical, "selectionMode"), ParseBool(canonical, "isRequired"), ParseNullableInt(canonical, "minSelections"), ParseNullableInt(canonical, "maxSelections"), ParseInt(canonical, "displayOrder"));
        }
        foreach (var duplicate in groupStates.Values.GroupBy(value => (value.ProductId, value.Order)).Where(value => value.Count() > 1)) Add(issues, "invalid-group-structure", "Option group display order must be unique within a Product.");
        foreach (var value in groupStates.Values)
            if (CatalogueValidation.ValidateGroup(new OptionGroup(Guid.Empty, value.ProductId, value.Name, value.Mode, value.Required, value.Min, value.Max, value.Order, default, default)) is { } error) Add(issues, "invalid-group-structure", error);

        var optionStates = options.Values.ToDictionary(value => value.Id, value => new OptionState(value.OptionGroupId, value.Name, value.PriceAdjustmentTtc.Cents, value.IsActive, value.DisplayOrder));
        foreach (var option in (plan.Operations ?? []).Where(value => value.EntityType == CatalogueImportEntityType.Option).GroupBy(value => value.LocalKey))
        {
            var canonical = CanonicalOperation(option);
            optionStates[canonical.EntityId ?? DeterministicId(canonical.LocalKey)] = new(ResolveParentGroup(canonical), Get(canonical, "name") ?? string.Empty, ParseMoney(canonical, "priceAdjustmentCents"), ParseBool(canonical, "isActive"), ParseInt(canonical, "displayOrder"));
        }
        foreach (var duplicate in optionStates.Values.GroupBy(value => (value.GroupId, value.Order)).Where(value => value.Count() > 1)) Add(issues, "invalid-option-structure", "Option display order must be unique within an OptionGroup.");
        foreach (var value in optionStates.Values)
            if (CatalogueValidation.ValidateOption(new ProductOption(Guid.Empty, value.GroupId, value.Name, Money.FromCents(value.PriceCents), value.Active, value.Order, default, default)) is { } error) Add(issues, "invalid-option-structure", error);
        foreach (var product in productStates.Where(value => value.Value.Options))
            foreach (var group in groupStates.Where(value => value.Value.ProductId == product.Key))
            {
                var active = optionStates.Count(value => value.Value.GroupId == group.Key && value.Value.Active);
                var minimum = group.Value.Mode == SelectionMode.Single ? (group.Value.Required ? 1 : 0) : group.Value.Min ?? 0;
                if (active < minimum) Add(issues, "required-active-choices", "Option group does not have enough active choices.");
            }
    }

    private static Guid? ResolveCategory(CatalogueImportOperation operation, IReadOnlyDictionary<Guid, CatalogueImportCategory> categories) => operation.CategoryReference?.ExistingId is { } id && categories.ContainsKey(id) ? id : operation.CategoryReference?.IsExisting == true ? null : DeterministicId(operation.CategoryReference?.LocalKey ?? "category");
    private static Guid ResolveParentProduct(CatalogueImportOperation operation) => operation.ParentReference?.ExistingId ?? DeterministicId(operation.ParentReference?.LocalKey ?? "product");
    private static Guid ResolveParentGroup(CatalogueImportOperation operation) => operation.ParentReference?.ExistingId ?? DeterministicId(operation.ParentReference?.LocalKey ?? "group");
    private static Guid DeterministicId(string key) => new(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("import:" + key))[..16]);
    private static Dictionary<TKey, TValue> BuildMap<TValue, TKey>(IEnumerable<TValue> values, Func<TValue, TKey> keySelector, List<CatalogueImportIssue> issues, string duplicateCode)
        where TKey : notnull
    {
        var result = new Dictionary<TKey, TValue>();
        foreach (var group in values.GroupBy(keySelector))
        {
            if (group.Skip(1).Any()) Add(issues, duplicateCode, "The import baseline contains duplicate durable identities.");
            result[group.Key] = group.First();
        }
        return result;
    }
    private static Dictionary<string, TValue> FirstByKey<TValue>(IEnumerable<TValue> values, Func<TValue, string> keySelector)
        => values.GroupBy(keySelector, StringComparer.Ordinal).ToDictionary(value => value.Key, value => value.First(), StringComparer.Ordinal);
    private static bool ValuesEqual(IReadOnlyDictionary<string, string?>? left, IReadOnlyDictionary<string, string?>? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }
    private static string? Get(CatalogueImportOperation operation, string key) => operation.Values is not null && operation.Values.TryGetValue(key, out var value) ? value : null;
    private static long ParseMoney(CatalogueImportOperation operation, string key) => long.TryParse(Get(operation, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : long.MinValue;
    private static decimal ParseDecimal(CatalogueImportOperation operation, string key) => decimal.TryParse(Get(operation, key), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : decimal.MinValue;
    private static bool ParseBool(CatalogueImportOperation operation, string key) => bool.TryParse(Get(operation, key), out var value) && value;
    private static int ParseInt(CatalogueImportOperation operation, string key) => int.TryParse(Get(operation, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : int.MinValue;
    private static int? ParseNullableInt(CatalogueImportOperation operation, string key) => string.IsNullOrWhiteSpace(Get(operation, key)) ? null : int.TryParse(Get(operation, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : int.MinValue;
    private static SelectionMode ParseMode(CatalogueImportOperation operation, string key) => string.Equals(Get(operation, key), "MULTI", StringComparison.OrdinalIgnoreCase) ? SelectionMode.Multi : SelectionMode.Single;
    private static void Add(List<CatalogueImportIssue> issues, string code, string message, CatalogueImportOperation? operation = null) => issues.Add(new(CatalogueImportIssueSeverity.Error, code, message, operation?.Worksheet, operation?.ExcelRow));

    private readonly record struct ProductState(string Code, string Name, Guid CategoryId, long PriceCents, decimal VatRate, bool Active, bool Discount, bool Options);
    private readonly record struct GroupState(Guid ProductId, string Name, SelectionMode Mode, bool Required, int? Min, int? Max, int Order);
    private readonly record struct OptionState(Guid GroupId, string Name, long PriceCents, bool Active, int Order);
}
