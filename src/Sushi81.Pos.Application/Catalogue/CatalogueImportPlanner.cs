using System.Security.Cryptography;
using System.Text;
using Sushi81.Pos.Domain;

#pragma warning disable CA1822, CA1859, CA1305

namespace Sushi81.Pos.Application.Catalogue;

/// <summary>
/// Pure deterministic overlay/validation planner.  It deliberately has no
/// persistence, workbook-library or UI dependency and produces no Delete operation.
/// </summary>
public sealed class CatalogueImportPlanner
{
    public CatalogueImportResult CreatePreview(CatalogueImportMode mode, CatalogueImportWorkbook workbook, CatalogueImportBaseline baseline, string? sourceName = null) => Plan(mode, workbook, baseline, sourceName);

    public CatalogueImportResult Plan(
        CatalogueImportMode mode,
        CatalogueImportWorkbook workbook,
        CatalogueImportBaseline baseline,
        string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(baseline);

        var issues = new List<CatalogueImportIssue>(workbook.Issues ?? []);
        var effectiveSource = sourceName ?? workbook.SourceName;
        if (!Enum.IsDefined(mode)) AddError(issues, "invalid-mode", "Import mode is invalid.");
        if (!string.Equals(workbook.ContractVersion, CatalogueWorkbookSchema.ContractVersion, StringComparison.Ordinal))
            AddError(issues, "unsupported-contract-version", "Workbook contract version is unsupported.");

        var currentCategories = baseline.Categories ?? [];
        var currentProducts = baseline.Products ?? [];
        var categoriesByName = currentCategories.GroupBy(category => category.NormalizedName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var duplicate in categoriesByName.Where(pair => pair.Value.Length > 1))
            AddError(issues, "duplicate-category-name", $"Current Catalogue contains duplicate Category name '{duplicate.Key}'.");

        var productsById = currentProducts.GroupBy(value => value.Id).ToDictionary(value => value.Key, value => value.First());
        var groupsById = currentProducts.SelectMany(value => value.OptionGroups).GroupBy(value => value.Id).ToDictionary(value => value.Key, value => value.First());
        var optionsById = currentProducts.SelectMany(value => value.OptionGroups).SelectMany(value => value.Options).GroupBy(value => value.Id).ToDictionary(value => value.Key, value => value.First());
        var manifest = new Dictionary<string, CatalogueImportManifestEntry>(StringComparer.Ordinal);
        foreach (var entry in workbook.Manifest ?? [])
            if (!manifest.TryAdd(entry.RowKey, entry)) AddError(issues, "duplicate-manifest-key", "Manifest RowKey is duplicated.");
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenIds = new HashSet<Guid>();
        var productCandidates = new List<ProductCandidate>();
        var productByLocalKey = new Dictionary<string, ProductCandidate>(StringComparer.Ordinal);
        var productByExistingId = new Dictionary<Guid, ProductCandidate>();

        foreach (var row in workbook.Products ?? [])
        {
            var identity = ResolveIdentity(mode, "Product", row.ProductId, row.ProductRowKey, row.ExcelRow, "Products", manifest, seenKeys, seenIds, issues);
            var current = identity.Id is { } productId && productsById.TryGetValue(productId, out var existing) ? existing : null;
            if (identity.IsExisting && current is null)
                AddError(issues, "unknown-entity-id", "Product identity is not present in the current Catalogue.", "Products", row.ExcelRow, "product_id");

            var localKey = identity.IsExisting ? $"product:{identity.Id!.Value:N}" : NewLocalKey("product", row.ExcelRow, row.ProductCode, row.ProductName);
            var code = Display(row.ProductCode);
            var name = Display(row.ProductName);
            var categoryName = Display(row.CategoryName);
            if (code.Length == 0) AddError(issues, "required-field", "Product code is required.", "Products", row.ExcelRow, "product_code");
            if (name.Length == 0) AddError(issues, "required-field", "Product name is required.", "Products", row.ExcelRow, "product_name");
            if (categoryName.Length == 0) AddError(issues, "required-field", "Category name is required.", "Products", row.ExcelRow, "category_name");
            if (row.PriceTtc is null) AddError(issues, "invalid-scalar", "Product price is missing or invalid.", "Products", row.ExcelRow, "price_ttc");
            if (row.VatRate is null) AddError(issues, "invalid-scalar", "VAT rate is missing or invalid.", "Products", row.ExcelRow, "vat_rate");
            if (row.IsActive is null) AddError(issues, "invalid-scalar", "Active value is missing or invalid.", "Products", row.ExcelRow, "is_active");
            if (row.DiscountEligible is null) AddError(issues, "invalid-scalar", "Discount eligible value is missing or invalid.", "Products", row.ExcelRow, "discount_eligible");
            if (row.OptionsEnabled is null) AddError(issues, "invalid-scalar", "Options enabled value is missing or invalid.", "Products", row.ExcelRow, "options_enabled");

            var categoryKey = CatalogueNormalization.Key(categoryName);
            var category = categoriesByName.TryGetValue(categoryKey, out var matches) ? matches.SingleOrDefault() : null;
            var categoryId = category?.Id ?? DeterministicGuid($"category:{categoryKey}");
            var shortCode = NormalizeOptional(row.CategoryShortCode);
            if (category is not null)
            {
                if (shortCode is not null && category.NormalizedShortCode is null)
                    AddError(issues, "existing-category-short-code-change", "An existing Category with no short code cannot receive one through import.", "Products", row.ExcelRow, "category_short_code");
                else if (shortCode is not null && !string.Equals(CatalogueNormalization.Key(shortCode), category.NormalizedShortCode, StringComparison.Ordinal))
                    AddError(issues, "existing-category-short-code-change", "Existing Category short code changes are not allowed through import.", "Products", row.ExcelRow, "category_short_code");
                shortCode = category.ShortCode;
            }

            var candidate = new ProductCandidate(
                row, identity, localKey, current, category, categoryId,
                code, name, categoryName, shortCode, row.PriceTtc ?? Money.Zero,
                row.VatRate ?? 0m, row.IsActive ?? false, row.DiscountEligible ?? false,
                row.OptionsEnabled ?? false);
            candidate = PreserveLiveWhenWorkbookUnchanged(candidate, manifest);
            productCandidates.Add(candidate);
            productByLocalKey[localKey] = candidate;
            if (identity.Id is { } existingId) productByExistingId[existingId] = candidate;

            if (identity.IsExisting && current is not null)
                ValidateProductStaleness(candidate, manifest, issues);
        }

        // A resulting catalogue contains omitted current Products unchanged.
        var resultingProducts = new List<ProductCandidate>();
        resultingProducts.AddRange(productCandidates);
        foreach (var current in currentProducts.Where(value => productCandidates.All(candidate => candidate.Identity.Id != value.Id)))
            resultingProducts.Add(ProductCandidate.FromCurrent(current));

        var codeGroups = resultingProducts.Where(value => value.Code.Length > 0)
            .GroupBy(value => CatalogueNormalization.Key(value.Code), StringComparer.Ordinal);
        foreach (var duplicate in codeGroups.Where(group => group.Count() > 1))
            AddError(issues, "duplicate-product-code", $"Product code '{duplicate.Key}' is duplicated in the resulting Catalogue.");
        if (mode == CatalogueImportMode.AddOnly)
        {
            foreach (var candidate in productCandidates)
                if (currentProducts.Any(product => string.Equals(product.NormalizedCode, CatalogueNormalization.Key(candidate.Code), StringComparison.Ordinal)))
                    AddError(issues, "add-only-product-code-collision", "Add-only Product code collides with an existing Product.", "Products", candidate.Row.ExcelRow, "product_code");
        }

        var groupCandidates = new List<GroupCandidate>();
        var groupByLocalKey = new Dictionary<string, GroupCandidate>(StringComparer.Ordinal);
        foreach (var row in workbook.OptionGroups ?? [])
        {
            var identity = ResolveIdentity(mode, "OptionGroup", row.OptionGroupId, row.OptionGroupRowKey, row.ExcelRow, "OptionGroups", manifest, seenKeys, seenIds, issues);
            var current = identity.Id is { } groupId && groupsById.TryGetValue(groupId, out var existing) ? existing : null;
            if (identity.IsExisting && current is null)
                AddError(issues, "unknown-entity-id", "OptionGroup identity is not present in the current Catalogue.", "OptionGroups", row.ExcelRow, "option_group_id");

            var product = ResolveProductParent(row.ProductRowKey, row.ProductCode, row.ProductName, mode, identity, current?.ProductId, productCandidates, resultingProducts, manifest, issues, "OptionGroups", row.ExcelRow);
            var localKey = identity.IsExisting ? $"group:{identity.Id!.Value:N}" : NewLocalKey("group", row.ExcelRow, row.GroupName, row.ProductCode);
            var groupName = Display(row.GroupName);
            if (groupName.Length == 0) AddError(issues, "required-field", "Option group name is required.", "OptionGroups", row.ExcelRow, "group_name");
            var selectionMode = SelectionMode.Single;
            if (row.SelectionMode is null || !TrySelectionMode(row.SelectionMode, out selectionMode)) AddError(issues, "invalid-group-structure", "Selection mode is invalid.", "OptionGroups", row.ExcelRow, "selection_mode");
            if (row.IsRequired is null) AddError(issues, "invalid-scalar", "Required value is missing or invalid.", "OptionGroups", row.ExcelRow, "is_required");
            if (row.DisplayOrder is null) AddError(issues, "invalid-group-structure", "Display order is missing or invalid.", "OptionGroups", row.ExcelRow, "display_order");

            var candidate = new GroupCandidate(row, identity, localKey, current, product,
                groupName, selectionMode, row.IsRequired ?? false, row.MinSelections, row.MaxSelections, row.DisplayOrder ?? 0);
            candidate = PreserveLiveWhenWorkbookUnchanged(candidate, manifest);
            groupCandidates.Add(candidate);
            groupByLocalKey[localKey] = candidate;
            if (identity.IsExisting && current is not null) ValidateGroupStaleness(candidate, manifest, issues);
        }

        var resultingGroups = new List<GroupCandidate>();
        resultingGroups.AddRange(groupCandidates);
        foreach (var current in currentProducts.SelectMany(value => value.OptionGroups).Where(value => groupCandidates.All(candidate => candidate.Identity.Id != value.Id)))
        {
            var parent = resultingProducts.FirstOrDefault(value => value.Identity.Id == current.ProductId) ?? ProductCandidate.FromCurrent(currentProducts.Single(value => value.Id == current.ProductId));
            resultingGroups.Add(GroupCandidate.FromCurrent(current, parent));
        }

        var optionCandidates = new List<OptionCandidate>();
        foreach (var row in workbook.Options ?? [])
        {
            var identity = ResolveIdentity(mode, "Option", row.OptionId, row.OptionRowKey, row.ExcelRow, "Options", manifest, seenKeys, seenIds, issues);
            var current = identity.Id is { } optionId && optionsById.TryGetValue(optionId, out var existing) ? existing : null;
            if (identity.IsExisting && current is null)
                AddError(issues, "unknown-entity-id", "Option identity is not present in the current Catalogue.", "Options", row.ExcelRow, "option_id");
            var parent = ResolveGroupParent(row.OptionGroupRowKey, row.ProductCode, row.ProductName, row.OptionGroupName, mode, identity, current?.OptionGroupId, groupCandidates, resultingGroups, productCandidates, resultingProducts, manifest, issues, "Options", row.ExcelRow);
            var optionName = Display(row.OptionName);
            if (optionName.Length == 0) AddError(issues, "required-field", "Option name is required.", "Options", row.ExcelRow, "option_name");
            if (row.PriceAdjustmentTtc is null) AddError(issues, "invalid-scalar", "Option price adjustment is missing or invalid.", "Options", row.ExcelRow, "price_adjustment_ttc");
            if (row.IsActive is null) AddError(issues, "invalid-scalar", "Active value is missing or invalid.", "Options", row.ExcelRow, "is_active");
            if (row.DisplayOrder is null) AddError(issues, "invalid-scalar", "Display order is missing or invalid.", "Options", row.ExcelRow, "display_order");
            var localKey = identity.IsExisting ? $"option:{identity.Id!.Value:N}" : NewLocalKey("option", row.ExcelRow, optionName, row.ProductCode);
            var candidate = new OptionCandidate(row, identity, localKey, current, parent,
                optionName, row.PriceAdjustmentTtc ?? Money.Zero, row.IsActive ?? false, row.DisplayOrder ?? 0);
            candidate = PreserveLiveWhenWorkbookUnchanged(candidate, manifest);
            optionCandidates.Add(candidate);
            if (identity.IsExisting && current is not null) ValidateOptionStaleness(candidate, manifest, issues);
        }

        var resultingOptions = new List<OptionCandidate>();
        resultingOptions.AddRange(optionCandidates);
        foreach (var current in currentProducts.SelectMany(value => value.OptionGroups).SelectMany(value => value.Options).Where(value => optionCandidates.All(candidate => candidate.Identity.Id != value.Id)))
        {
            var group = resultingGroups.FirstOrDefault(value => value.Id == current.OptionGroupId)
                ?? resultingGroups.First(value => value.Current?.Id == current.OptionGroupId);
            resultingOptions.Add(OptionCandidate.FromCurrent(current, group));
        }

        ValidateCategories(productCandidates, currentCategories, categoriesByName, issues);
        ValidateResultingCatalogue(resultingProducts, resultingGroups, resultingOptions, issues);

        var operations = new List<CatalogueImportOperation>();
        var affected = new List<CatalogueImportAffectedRow>();
        foreach (var candidate in productCandidates)
        {
            var kinds = new List<CatalogueImportOperationKind>();
            if (!candidate.Identity.IsExisting) kinds.Add(CatalogueImportOperationKind.Create);
            else if (candidate.Current is not null && ProductChanged(candidate)) kinds.Add(CatalogueImportOperationKind.Modify);
            if (candidate.Identity.IsExisting && candidate.Current is not null && candidate.IsActive != candidate.Current.IsActive)
                kinds.Add(candidate.IsActive ? CatalogueImportOperationKind.Activate : CatalogueImportOperationKind.Deactivate);
            AddOperations(operations, affected, CatalogueImportEntityType.Product, candidate.Identity.Id, candidate.LocalKey, candidate, kinds,
                new Dictionary<string, string?> { ["code"] = candidate.Code, ["name"] = candidate.Name, ["category"] = candidate.CategoryName, ["categoryShortCode"] = candidate.CategoryShortCode, ["priceCents"] = candidate.PriceTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture), ["vatRate"] = candidate.VatRate.ToString(System.Globalization.CultureInfo.InvariantCulture), ["isActive"] = candidate.IsActive.ToString(), ["discountEligible"] = candidate.DiscountEligible.ToString(), ["optionsEnabled"] = candidate.OptionsEnabled.ToString() });
        }
        foreach (var candidate in groupCandidates)
        {
            var kinds = new List<CatalogueImportOperationKind>();
            if (!candidate.Identity.IsExisting) kinds.Add(CatalogueImportOperationKind.Create);
            else if (candidate.Current is not null && GroupChanged(candidate)) kinds.Add(CatalogueImportOperationKind.Modify);
            AddOperations(operations, affected, CatalogueImportEntityType.OptionGroup, candidate.Identity.Id, candidate.LocalKey, candidate, kinds,
                new Dictionary<string, string?> { ["productCode"] = candidate.Product.Code, ["name"] = candidate.Name, ["selectionMode"] = candidate.SelectionMode.ToString(), ["isRequired"] = candidate.IsRequired.ToString(), ["minSelections"] = candidate.MinSelections?.ToString(), ["maxSelections"] = candidate.MaxSelections?.ToString(), ["displayOrder"] = candidate.DisplayOrder.ToString() });
        }
        foreach (var candidate in optionCandidates)
        {
            var kinds = new List<CatalogueImportOperationKind>();
            if (!candidate.Identity.IsExisting) kinds.Add(CatalogueImportOperationKind.Create);
            else if (candidate.Current is not null && OptionChanged(candidate)) kinds.Add(CatalogueImportOperationKind.Modify);
            if (candidate.Identity.IsExisting && candidate.Current is not null && candidate.IsActive != candidate.Current.IsActive)
                kinds.Add(candidate.IsActive ? CatalogueImportOperationKind.Activate : CatalogueImportOperationKind.Deactivate);
            AddOperations(operations, affected, CatalogueImportEntityType.Option, candidate.Identity.Id, candidate.LocalKey, candidate, kinds,
                new Dictionary<string, string?> { ["name"] = candidate.Name, ["priceAdjustmentCents"] = candidate.PriceAdjustmentTtc.Cents.ToString(), ["isActive"] = candidate.IsActive.ToString(), ["displayOrder"] = candidate.DisplayOrder.ToString() });
        }

        var orderedIssues = issues.OrderBy(issue => issue.Worksheet ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(issue => issue.ExcelRow ?? 0).ThenBy(issue => issue.FieldKey ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal).ThenBy(issue => issue.Message, StringComparer.Ordinal).ToArray();
        var errorCount = orderedIssues.Count(issue => issue.IsBlocking);
        var warningCount = orderedIssues.Length - errorCount;
        var newCategories = NewCategories(productCandidates, currentCategories);
        var preview = new CatalogueImportPreview(mode, effectiveSource,
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Product && op.Kind == CatalogueImportOperationKind.Create),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Product && op.Kind == CatalogueImportOperationKind.Modify),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Product && op.Kind == CatalogueImportOperationKind.Activate),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Product && op.Kind == CatalogueImportOperationKind.Deactivate),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.OptionGroup && op.Kind == CatalogueImportOperationKind.Create),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.OptionGroup && op.Kind == CatalogueImportOperationKind.Modify),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Option && op.Kind == CatalogueImportOperationKind.Create),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Option && op.Kind == CatalogueImportOperationKind.Modify),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Option && op.Kind == CatalogueImportOperationKind.Activate),
            operations.Count(op => op.EntityType == CatalogueImportEntityType.Option && op.Kind == CatalogueImportOperationKind.Deactivate),
            newCategories.Count, errorCount, warningCount, orderedIssues, affected.OrderBy(value => value.Worksheet, StringComparer.Ordinal).ThenBy(value => value.ExcelRow).ToArray(), true, false, mode == CatalogueImportMode.AddOnly);
        var plan = errorCount == 0 ? new CatalogueImportPlan(mode, operations, preview.AffectedRows, newCategories, effectiveSource) : null;
        return new CatalogueImportResult(preview, plan);
    }

    private static Identity ResolveIdentity(CatalogueImportMode mode, string type, string? idText, string? rowKey, int row, string worksheet,
        IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, HashSet<string> seenKeys, HashSet<Guid> seenIds, List<CatalogueImportIssue> issues)
    {
        var hasId = !string.IsNullOrWhiteSpace(idText);
        var hasKey = !string.IsNullOrWhiteSpace(rowKey);
        if (mode == CatalogueImportMode.AddOnly && (hasId || hasKey))
            AddError(issues, "add-only-existing-binding", "Add-only rows must not contain existing entity identity or binding.", worksheet, row, hasId ? "entity-id" : "row-key");
        Guid? id = null;
        if (hasId)
        {
            if (!Guid.TryParse(idText, out var parsed) || parsed == Guid.Empty)
                AddError(issues, "malformed-entity-id", "Entity identity is malformed.", worksheet, row, "entity-id");
            else if (!seenIds.Add(parsed)) AddError(issues, "duplicate-entity-id", "Entity identity is duplicated in the workbook.", worksheet, row, "entity-id");
            else id = parsed;
        }
        if (hasKey && !seenKeys.Add(rowKey!)) AddError(issues, "duplicate-row-key", "Row binding key is duplicated in the workbook.", worksheet, row, "row-key");
        if (hasId != hasKey && mode == CatalogueImportMode.Update)
            AddError(issues, "misbound-identity", "Existing entity identity and row binding must be supplied together.", worksheet, row, "row-key");
        CatalogueImportManifestEntry? entry = null;
        if (hasKey)
        {
            if (!manifest.TryGetValue(rowKey!, out entry)) AddError(issues, "unknown-row-key", "Row binding key is not present in the workbook manifest.", worksheet, row, "row-key");
            else if (!string.Equals(entry.EntityType, type, StringComparison.Ordinal)) AddError(issues, "wrong-entity-type", "Row binding entity type is incorrect.", worksheet, row, "row-key");
            else if (id is { } parsed && entry.EntityId != parsed) AddError(issues, "misbound-identity", "Entity identity does not match its manifest binding.", worksheet, row, "entity-id");
        }
        return new Identity(id, hasId && hasKey && entry is not null && string.Equals(entry.EntityType, type, StringComparison.Ordinal), hasKey ? rowKey : null);
    }

    private static ProductCandidate ResolveProductParent(string? parentRowKey, string? code, string? name, CatalogueImportMode mode, Identity childIdentity, Guid? existingParentId,
        IReadOnlyList<ProductCandidate> explicitProducts, IReadOnlyList<ProductCandidate> resultingProducts, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest,
        List<CatalogueImportIssue> issues, string worksheet, int row)
    {
        if (childIdentity.IsExisting && existingParentId is { } parentId)
        {
            var bound = explicitProducts.FirstOrDefault(value => value.Identity.Id == parentId) ?? resultingProducts.FirstOrDefault(value => value.Identity.Id == parentId);
            if (parentRowKey is null || !manifest.TryGetValue(parentRowKey, out var entry) || entry.EntityType != "Product" || entry.EntityId != parentId)
                AddError(issues, "wrong-parent-binding", "Existing child parent binding is invalid.", worksheet, row, "product_row_key");
            if (bound is not null)
            {
                var currentParent = bound.Current;
                if (!string.IsNullOrWhiteSpace(code) && !string.Equals(CatalogueNormalization.Key(code), bound.NormalizedCode, StringComparison.Ordinal) && (currentParent is null || !string.Equals(CatalogueNormalization.Key(code), currentParent.NormalizedCode, StringComparison.Ordinal)))
                    AddError(issues, "wrong-parent-binding", "Descriptive parent Product code is inconsistent with the bound/planned parent.", worksheet, row, "product_code");
                if (!string.IsNullOrWhiteSpace(name) && !string.Equals(CatalogueNormalization.Key(name), CatalogueNormalization.Key(bound.Name), StringComparison.Ordinal) && (currentParent is null || !string.Equals(CatalogueNormalization.Key(name), CatalogueNormalization.Key(currentParent.Name), StringComparison.Ordinal)))
                    AddError(issues, "wrong-parent-binding", "Descriptive parent Product name is inconsistent with the bound/planned parent.", worksheet, row, "product_name");
                return bound;
            }
        }

        var key = CatalogueNormalization.Key(code);
        var matches = resultingProducts.Where(value => string.Equals(CatalogueNormalization.Key(value.Code), key, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            AddError(issues, matches.Length == 0 ? "missing-parent" : "ambiguous-parent", "Parent Product must resolve to exactly one Product by normalized code.", worksheet, row, "product_code");
        var resolved = matches.FirstOrDefault() ?? ProductCandidate.Placeholder(code);
        if (resolved.Name.Length > 0 && !string.IsNullOrWhiteSpace(name) && !string.Equals(CatalogueNormalization.Key(name), CatalogueNormalization.Key(resolved.Name), StringComparison.Ordinal))
            AddError(issues, "wrong-parent-binding", "Descriptive parent Product name is inconsistent with the resolved parent.", worksheet, row, "product_name");
        return resolved;
    }

    private static GroupParent ResolveGroupParent(string? groupRowKey, string? productCode, string? productName, string? groupName, CatalogueImportMode mode, Identity childIdentity, Guid? existingGroupId,
        IReadOnlyList<GroupCandidate> explicitGroups, IReadOnlyList<GroupCandidate> resultingGroups, IReadOnlyList<ProductCandidate> explicitProducts, IReadOnlyList<ProductCandidate> resultingProducts,
        IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, List<CatalogueImportIssue> issues, string worksheet, int row)
    {
        if (childIdentity.IsExisting && existingGroupId is { } groupId)
        {
            var bound = explicitGroups.FirstOrDefault(value => value.Identity.Id == groupId) ?? resultingGroups.FirstOrDefault(value => value.Id == groupId);
            if (groupRowKey is null || !manifest.TryGetValue(groupRowKey, out var entry) || entry.EntityType != "OptionGroup" || entry.EntityId != groupId)
                AddError(issues, "wrong-parent-binding", "Existing Option parent binding is invalid.", worksheet, row, "option_group_row_key");
            if (bound is not null)
            {
                var oldName = bound.Current?.Name;
                if (!string.IsNullOrWhiteSpace(productName) && !string.Equals(CatalogueNormalization.Key(productName), CatalogueNormalization.Key(bound.Product.Name), StringComparison.Ordinal) && (bound.Product.Current is null || !string.Equals(CatalogueNormalization.Key(productName), CatalogueNormalization.Key(bound.Product.Current.Name), StringComparison.Ordinal)))
                    AddError(issues, "wrong-parent-binding", "Descriptive parent Product name is inconsistent with the bound/planned parent.", worksheet, row, "product_name");
                if (!string.IsNullOrWhiteSpace(groupName) && !string.Equals(CatalogueNormalization.Key(groupName), CatalogueNormalization.Key(bound.Name), StringComparison.Ordinal) && !string.Equals(CatalogueNormalization.Key(groupName), CatalogueNormalization.Key(oldName), StringComparison.Ordinal))
                    AddError(issues, "wrong-parent-binding", "Descriptive OptionGroup name is inconsistent with the bound/planned parent.", worksheet, row, "option_group_name");
                return new GroupParent(bound);
            }
        }

        var products = resultingProducts.Where(value => string.Equals(value.NormalizedCode, CatalogueNormalization.Key(productCode), StringComparison.Ordinal)).ToArray();
        if (products.Length != 1)
        {
            AddError(issues, products.Length == 0 ? "missing-parent" : "ambiguous-parent", "Parent Product must resolve to exactly one Product by normalized code.", worksheet, row, "product_code");
            return new GroupParent(GroupCandidate.Placeholder(ProductCandidate.Placeholder(productCode), groupName));
        }
        var groups = resultingGroups.Where(value => value.Product.Id == products[0].Id && string.Equals(CatalogueNormalization.Key(value.Name), CatalogueNormalization.Key(groupName), StringComparison.Ordinal)).ToArray();
        if (groups.Length != 1)
            AddError(issues, groups.Length == 0 ? "missing-parent" : "ambiguous-parent", "Parent OptionGroup must resolve to exactly one group by exact normalized name.", worksheet, row, "option_group_name");
        var resolvedGroup = groups.FirstOrDefault() ?? GroupCandidate.Placeholder(products[0], groupName);
        if (resolvedGroup.Product.Name.Length > 0 && !string.IsNullOrWhiteSpace(productName) && !string.Equals(CatalogueNormalization.Key(productName), CatalogueNormalization.Key(resolvedGroup.Product.Name), StringComparison.Ordinal))
            AddError(issues, "wrong-parent-binding", "Descriptive parent Product name is inconsistent with the resolved parent.", worksheet, row, "product_name");
        return new GroupParent(resolvedGroup);
    }

    private static void ValidateProductStaleness(ProductCandidate candidate, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, List<CatalogueImportIssue> issues)
    {
        if (candidate.Identity.Key is null || !manifest.TryGetValue(candidate.Identity.Key, out var entry) || candidate.Current is null) return;
        var current = ToWorkbook(candidate.Current);
        var workbook = candidate.ToWorkbook();
        var workbookFingerprint = CatalogueWorkbookFingerprint.Product(workbook);
        var liveFingerprint = CatalogueWorkbookFingerprint.Product(current);
        var baseline = entry.BaselineFingerprint;
        if (!string.Equals(workbookFingerprint, baseline, StringComparison.Ordinal) && !string.Equals(liveFingerprint, baseline, StringComparison.Ordinal) && !string.Equals(workbookFingerprint, liveFingerprint, StringComparison.Ordinal))
            AddError(issues, "stale-conflict", "Workbook edits conflict with newer live Product data.", "Products", candidate.Row.ExcelRow, "product_row_key");
    }

    private static void ValidateGroupStaleness(GroupCandidate candidate, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, List<CatalogueImportIssue> issues)
    {
        if (candidate.Identity.Key is null || !manifest.TryGetValue(candidate.Identity.Key, out var entry) || candidate.Current is null) return;
        var workbook = CatalogueWorkbookFingerprint.OptionGroup(candidate.ToWorkbook());
        var live = CatalogueWorkbookFingerprint.OptionGroup(ToWorkbook(candidate.Current, candidate.Product));
        if (!string.Equals(workbook, entry.BaselineFingerprint, StringComparison.Ordinal) && !string.Equals(live, entry.BaselineFingerprint, StringComparison.Ordinal) && !string.Equals(workbook, live, StringComparison.Ordinal))
            AddError(issues, "stale-conflict", "Workbook edits conflict with newer live OptionGroup data.", "OptionGroups", candidate.Row.ExcelRow, "option_group_row_key");
    }

    private static void ValidateOptionStaleness(OptionCandidate candidate, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, List<CatalogueImportIssue> issues)
    {
        if (candidate.Identity.Key is null || !manifest.TryGetValue(candidate.Identity.Key, out var entry) || candidate.Current is null) return;
        var workbook = CatalogueWorkbookFingerprint.Option(candidate.ToWorkbook());
        var live = CatalogueWorkbookFingerprint.Option(ToWorkbook(candidate.Current, candidate.Parent.Group));
        if (!string.Equals(workbook, entry.BaselineFingerprint, StringComparison.Ordinal) && !string.Equals(live, entry.BaselineFingerprint, StringComparison.Ordinal) && !string.Equals(workbook, live, StringComparison.Ordinal))
            AddError(issues, "stale-conflict", "Workbook edits conflict with newer live Option data.", "Options", candidate.Row.ExcelRow, "option_row_key");
    }

    private static ProductCandidate PreserveLiveWhenWorkbookUnchanged(ProductCandidate candidate, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest)
    {
        if (candidate.Current is null || candidate.Identity.Key is null || !manifest.TryGetValue(candidate.Identity.Key, out var entry)) return candidate;
        var workbookFingerprint = CatalogueWorkbookFingerprint.Product(candidate.ToWorkbook());
        var liveFingerprint = CatalogueWorkbookFingerprint.Product(ToWorkbook(candidate.Current));
        if (string.Equals(workbookFingerprint, entry.BaselineFingerprint, StringComparison.Ordinal) && !string.Equals(liveFingerprint, entry.BaselineFingerprint, StringComparison.Ordinal))
            return candidate with { Category = new(candidate.Current.CategoryId, candidate.Current.CategoryName, candidate.Current.CategoryShortCode), CategoryId = candidate.Current.CategoryId, Code = candidate.Current.Code, Name = candidate.Current.Name, CategoryName = candidate.Current.CategoryName, CategoryShortCode = candidate.Current.CategoryShortCode, PriceTtc = candidate.Current.PriceTtc, VatRate = candidate.Current.VatRate, IsActive = candidate.Current.IsActive, DiscountEligible = candidate.Current.DiscountEligible, OptionsEnabled = candidate.Current.OptionsEnabled };
        return candidate;
    }

    private static GroupCandidate PreserveLiveWhenWorkbookUnchanged(GroupCandidate candidate, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest)
    {
        if (candidate.Current is null || candidate.Identity.Key is null || !manifest.TryGetValue(candidate.Identity.Key, out var entry)) return candidate;
        var workbookFingerprint = CatalogueWorkbookFingerprint.OptionGroup(candidate.ToWorkbook());
        var liveFingerprint = CatalogueWorkbookFingerprint.OptionGroup(ToWorkbook(candidate.Current, candidate.Product));
        if (string.Equals(workbookFingerprint, entry.BaselineFingerprint, StringComparison.Ordinal) && !string.Equals(liveFingerprint, entry.BaselineFingerprint, StringComparison.Ordinal))
            return candidate with { Name = candidate.Current.Name, SelectionMode = candidate.Current.SelectionMode, IsRequired = candidate.Current.IsRequired, MinSelections = candidate.Current.MinSelections, MaxSelections = candidate.Current.MaxSelections, DisplayOrder = candidate.Current.DisplayOrder };
        return candidate;
    }

    private static OptionCandidate PreserveLiveWhenWorkbookUnchanged(OptionCandidate candidate, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest)
    {
        if (candidate.Current is null || candidate.Identity.Key is null || !manifest.TryGetValue(candidate.Identity.Key, out var entry)) return candidate;
        var workbookFingerprint = CatalogueWorkbookFingerprint.Option(candidate.ToWorkbook());
        var liveFingerprint = CatalogueWorkbookFingerprint.Option(ToWorkbook(candidate.Current, candidate.Parent.Group));
        if (string.Equals(workbookFingerprint, entry.BaselineFingerprint, StringComparison.Ordinal) && !string.Equals(liveFingerprint, entry.BaselineFingerprint, StringComparison.Ordinal))
            return candidate with { Name = candidate.Current.Name, PriceAdjustmentTtc = candidate.Current.PriceAdjustmentTtc, IsActive = candidate.Current.IsActive, DisplayOrder = candidate.Current.DisplayOrder };
        return candidate;
    }

    private static void ValidateCategories(IReadOnlyList<ProductCandidate> products, IReadOnlyList<CatalogueImportCategory> current, IReadOnlyDictionary<string, CatalogueImportCategory[]> currentByName, List<CatalogueImportIssue> issues)
    {
        foreach (var group in products.GroupBy(value => CatalogueNormalization.Key(value.CategoryName), StringComparer.Ordinal))
        {
            var proposals = group.Select(value => value.Row.CategoryShortCode).Where(value => !string.IsNullOrWhiteSpace(value)).Select(CatalogueNormalization.Key).Distinct(StringComparer.Ordinal).ToArray();
            if (proposals.Length > 1)
                foreach (var row in group) AddError(issues, "conflicting-category-short-code", "Repeated Category rows propose conflicting short codes.", "Products", row.Row.ExcelRow, "category_short_code");
            if (currentByName.TryGetValue(group.Key, out var existing) && existing.Length == 1 && proposals.Length > 0 && existing[0].NormalizedShortCode is { } currentCode && proposals[0] != currentCode)
                AddError(issues, "existing-category-short-code-change", "Existing Category short code changes are not allowed through import.");
        }
        var shortCodes = current.Where(value => value.NormalizedShortCode is not null).GroupBy(value => value.NormalizedShortCode!, StringComparer.Ordinal).ToDictionary(value => value.Key, value => value.Count(), StringComparer.Ordinal);
        var newCategoryProposals = products.Where(value => !currentByName.ContainsKey(CatalogueNormalization.Key(value.CategoryName)))
            .GroupBy(value => CatalogueNormalization.Key(value.CategoryName), StringComparer.Ordinal)
            .Select(group => (Category: group.Key, Codes: group.Select(value => NormalizeOptional(value.Row.CategoryShortCode)).Where(value => value is not null).Distinct(StringComparer.Ordinal).ToArray()))
            .ToArray();
        foreach (var categoryProposal in newCategoryProposals)
        {
            foreach (var proposal in categoryProposal.Codes)
            {
                if (shortCodes.TryGetValue(proposal!, out var count) && count > 0) AddError(issues, "category-short-code-duplicate", "New Category short code collides with an existing Category.");
                if (CatalogueValidation.ValidateCategoryShortCode(proposal) is { } lengthError) AddError(issues, "category-short-code-too-long", lengthError);
            }
        }
        foreach (var duplicate in newCategoryProposals.SelectMany(value => value.Codes).GroupBy(value => value, StringComparer.Ordinal).Where(value => value.Count() > 1))
            AddError(issues, "category-short-code-duplicate", "Category short code is duplicated across new Categories.");
    }

    private static void ValidateResultingCatalogue(IReadOnlyList<ProductCandidate> products, IReadOnlyList<GroupCandidate> groups, IReadOnlyList<OptionCandidate> options, List<CatalogueImportIssue> issues)
    {
        foreach (var product in products)
        {
            var error = CatalogueValidation.ValidateProduct(product.Code, product.Name, product.CategoryId, product.PriceTtc, product.VatRate);
            if (error is not null) AddError(issues, InferCode(error), error, "Products", product.ExcelRow == 0 ? null : product.ExcelRow, "product");
        }
        foreach (var group in groups)
        {
            var error = CatalogueValidation.ValidateGroup(new OptionGroup(group.Id, group.Product.Identity.Id ?? Guid.Empty, group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections, group.DisplayOrder, default, default));
            if (error is not null) AddError(issues, InferCode(error), error, "OptionGroups", group.ExcelRow == 0 ? null : group.ExcelRow, "group");
            if (groups.Where(value => value.Product.Id == group.Product.Id).Count(value => value.DisplayOrder == group.DisplayOrder) > 1)
                AddError(issues, "invalid-group-structure", "Option group display order must be unique within a Product.", "OptionGroups", group.ExcelRow == 0 ? null : group.ExcelRow, "display_order");
        }
        foreach (var option in options)
        {
            var error = CatalogueValidation.ValidateOption(new ProductOption(option.Id, option.Parent.Group.Id, option.Name, option.PriceAdjustmentTtc, option.IsActive, option.DisplayOrder, default, default));
            if (error is not null) AddError(issues, InferCode(error), error, "Options", option.ExcelRow == 0 ? null : option.ExcelRow, "option");
            if (options.Where(value => value.Parent.Group.Id == option.Parent.Group.Id).Count(value => value.DisplayOrder == option.DisplayOrder) > 1)
                AddError(issues, "invalid-option-structure", "Option display order must be unique within an OptionGroup.", "Options", option.ExcelRow == 0 ? null : option.ExcelRow, "display_order");
        }
        foreach (var product in products)
        {
            var productGroups = groups.Where(group => group.Product.Id == product.Id).ToArray();
            foreach (var group in productGroups)
            {
                var active = options.Count(option => option.Parent.Group.Id == group.Id && option.IsActive);
                var minimum = group.SelectionMode == SelectionMode.Single ? (group.IsRequired ? 1 : 0) : group.MinSelections ?? 0;
                if (product.OptionsEnabled && active < minimum)
                    AddError(issues, "required-active-choices", "Option group does not have enough active choices.", "OptionGroups", group.ExcelRow == 0 ? null : group.ExcelRow, "is_required");
            }
        }
    }

    private static IReadOnlyList<CatalogueImportCategory> NewCategories(IReadOnlyList<ProductCandidate> products, IReadOnlyList<CatalogueImportCategory> current)
    {
        var currentNames = current.Select(value => value.NormalizedName).ToHashSet(StringComparer.Ordinal);
        return products.GroupBy(value => CatalogueNormalization.Key(value.CategoryName), StringComparer.Ordinal)
            .Where(group => group.Key.Length > 0 && !currentNames.Contains(group.Key))
            .Select(group => new CatalogueImportCategory(DeterministicGuid($"category:{group.Key}"), group.First().CategoryName, group.Select(value => NormalizeOptional(value.Row.CategoryShortCode)).FirstOrDefault(value => value is not null)))
            .OrderBy(value => value.NormalizedName, StringComparer.Ordinal).ToArray();
    }

    private static bool ProductChanged(ProductCandidate value) => value.Current is null || value.Code != value.Current.Code || value.Name != value.Current.Name || value.CategoryId != value.Current.CategoryId || value.PriceTtc != value.Current.PriceTtc || value.VatRate != value.Current.VatRate || value.IsActive != value.Current.IsActive || value.DiscountEligible != value.Current.DiscountEligible || value.OptionsEnabled != value.Current.OptionsEnabled;
    private static bool GroupChanged(GroupCandidate value) => value.Current is null || value.Name != value.Current.Name || value.SelectionMode != value.Current.SelectionMode || value.IsRequired != value.Current.IsRequired || value.MinSelections != value.Current.MinSelections || value.MaxSelections != value.Current.MaxSelections || value.DisplayOrder != value.Current.DisplayOrder;
    private static bool OptionChanged(OptionCandidate value) => value.Current is null || value.Name != value.Current.Name || value.PriceAdjustmentTtc != value.Current.PriceAdjustmentTtc || value.IsActive != value.Current.IsActive || value.DisplayOrder != value.Current.DisplayOrder;

    private static void AddOperations(List<CatalogueImportOperation> operations, List<CatalogueImportAffectedRow> affected, CatalogueImportEntityType type, Guid? id, string localKey, RowBase? row, List<CatalogueImportOperationKind> kinds, IReadOnlyDictionary<string, string?> values)
    {
        if (row is null || kinds.Count == 0) return;
        foreach (var kind in kinds) operations.Add(new(type, kind, id, localKey, row.ExcelRow, row.Worksheet, values));
        affected.Add(new(row.Worksheet, row.ExcelRow, type, localKey, kinds));
    }

    private static string Display(string? value) => CatalogueNormalization.Display(value);
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : CatalogueNormalization.Display(value);
    private static string NewLocalKey(string type, int row, params string?[] values) => $"{type}:new:{row}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values.Select(value => value ?? string.Empty)))))[..12].ToLowerInvariant()}";
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
    private static bool TrySelectionMode(string? value, out SelectionMode mode) => Enum.TryParse(value?.Trim(), true, out mode);
    private static string InferCode(string message) => message.Contains("required", StringComparison.OrdinalIgnoreCase) ? "required-field" : message.Contains("VAT", StringComparison.OrdinalIgnoreCase) ? "vat-range" : message.Contains("price", StringComparison.OrdinalIgnoreCase) ? "price-negative" : message.Contains("Option", StringComparison.OrdinalIgnoreCase) ? "invalid-option-structure" : "invalid-group-structure";
    private static void AddError(List<CatalogueImportIssue> issues, string code, string message, string? worksheet = null, int? row = null, string? field = null) => issues.Add(new(CatalogueImportIssueSeverity.Error, code, message, worksheet, row, field));

    private sealed record Identity(Guid? Id, bool IsExisting, string? Key = null);
    private sealed record GroupParent(GroupCandidate Group);
    private abstract record RowBase(int ExcelRow, string Worksheet);
    private sealed record ProductCandidate(CatalogueImportProductRow Row, Identity Identity, string LocalKey, CatalogueImportBaselineProduct? Current, CatalogueImportCategory? Category, Guid CategoryId, string Code, string Name, string CategoryName, string? CategoryShortCode, Money PriceTtc, decimal VatRate, bool IsActive, bool DiscountEligible, bool OptionsEnabled) : RowBase(Row.ExcelRow, "Products")
    {
        public Guid Id => Identity.Id ?? DeterministicGuid(LocalKey);
        public string NormalizedCode => CatalogueNormalization.Key(Code);
        public static ProductCandidate FromCurrent(CatalogueImportBaselineProduct current) => new(new(0, current.Code, current.Name, current.CategoryName, current.CategoryShortCode, current.PriceTtc, current.VatRate, current.IsActive, current.DiscountEligible, current.OptionsEnabled, $"product:{current.Id:N}", current.Id.ToString("D")), new(current.Id, true, $"product:{current.Id:N}"), $"product:{current.Id:N}", current, new(current.CategoryId, current.CategoryName, current.CategoryShortCode), current.CategoryId, current.Code, current.Name, current.CategoryName, current.CategoryShortCode, current.PriceTtc, current.VatRate, current.IsActive, current.DiscountEligible, current.OptionsEnabled);
        public static ProductCandidate Placeholder(string? code) => new(new(0, code, null, null, null, null, null, null, null, null, null, null), new(null, false), "product:placeholder", null, null, DeterministicGuid("placeholder-product"), Display(code), "", "", null, Money.Zero, 0, false, false, false);
        public CatalogueWorkbookProduct ToWorkbook() => new(Identity.Id ?? DeterministicGuid(LocalKey), Code, Name, CategoryName, CategoryShortCode, PriceTtc, VatRate, IsActive, DiscountEligible, OptionsEnabled, []);
    }
    private sealed record GroupCandidate(CatalogueImportOptionGroupRow Row, Identity Identity, string LocalKey, CatalogueImportBaselineOptionGroup? Current, ProductCandidate Product, string Name, SelectionMode SelectionMode, bool IsRequired, int? MinSelections, int? MaxSelections, int DisplayOrder) : RowBase(Row.ExcelRow, "OptionGroups")
    {
        public Guid Id => Identity.Id ?? DeterministicGuid(LocalKey);
        public static GroupCandidate FromCurrent(CatalogueImportBaselineOptionGroup current, ProductCandidate parent) => new(new(0, parent.Code, parent.Name, current.Name, current.SelectionMode.ToString(), current.IsRequired, current.MinSelections, current.MaxSelections, current.DisplayOrder, $"product:{parent.Id:N}", $"group:{current.Id:N}", parent.Id.ToString("D"), current.Id.ToString("D")), new(current.Id, true, $"group:{current.Id:N}"), $"group:{current.Id:N}", current, parent, current.Name, current.SelectionMode, current.IsRequired, current.MinSelections, current.MaxSelections, current.DisplayOrder);
        public static GroupCandidate Placeholder(ProductCandidate product, string? name) => new(new(0, product.Code, product.Name, name, "SINGLE", false, null, null, 0, null, null, null, null), new(null, false), "group:placeholder", null, product, Display(name), SelectionMode.Single, false, null, null, 0);
        public CatalogueWorkbookOptionGroup ToWorkbook() => new(Id, Product.Identity.Id ?? DeterministicGuid(Product.LocalKey), Product.Code, Product.Name, Name, SelectionMode, IsRequired, MinSelections, MaxSelections, DisplayOrder, []);
    }
    private sealed record OptionCandidate(CatalogueImportOptionRow Row, Identity Identity, string LocalKey, CatalogueImportBaselineOption? Current, GroupParent Parent, string Name, Money PriceAdjustmentTtc, bool IsActive, int DisplayOrder) : RowBase(Row.ExcelRow, "Options")
    {
        public Guid Id => Identity.Id ?? DeterministicGuid(LocalKey);
        public static OptionCandidate FromCurrent(CatalogueImportBaselineOption current, GroupCandidate parent) => new(new(0, parent.Product.Code, parent.Product.Name, parent.Name, current.Name, current.PriceAdjustmentTtc, current.IsActive, current.DisplayOrder, $"product:{parent.Product.Id:N}", $"group:{parent.Id:N}", $"option:{current.Id:N}", current.Id.ToString("D"), parent.Id.ToString("D")), new(current.Id, true, $"option:{current.Id:N}"), $"option:{current.Id:N}", current, new(parent), current.Name, current.PriceAdjustmentTtc, current.IsActive, current.DisplayOrder);
        public CatalogueWorkbookOption ToWorkbook() => new(Id, Parent.Group.Id, Parent.Group.Product.Code, Parent.Group.Product.Name, Parent.Group.Name, Name, PriceAdjustmentTtc, IsActive, DisplayOrder);
    }

    private static CatalogueWorkbookProduct ToWorkbook(CatalogueImportBaselineProduct value) => new(value.Id, value.Code, value.Name, value.CategoryName, value.CategoryShortCode, value.PriceTtc, value.VatRate, value.IsActive, value.DiscountEligible, value.OptionsEnabled, value.OptionGroups.Select(group => new CatalogueWorkbookOptionGroup(group.Id, value.Id, value.Code, value.Name, group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections, group.DisplayOrder, group.Options.Select(option => new CatalogueWorkbookOption(option.Id, group.Id, value.Code, value.Name, group.Name, option.Name, option.PriceAdjustmentTtc, option.IsActive, option.DisplayOrder)).ToArray())).ToArray());
    private static CatalogueWorkbookOptionGroup ToWorkbook(CatalogueImportBaselineOptionGroup value, ProductCandidate product) => new(value.Id, value.ProductId, product.Code, product.Name, value.Name, value.SelectionMode, value.IsRequired, value.MinSelections, value.MaxSelections, value.DisplayOrder, []);
    private static CatalogueWorkbookOption ToWorkbook(CatalogueImportBaselineOption value, GroupCandidate group) => new(value.Id, value.OptionGroupId, group.Product.Code, group.Product.Name, group.Name, value.Name, value.PriceAdjustmentTtc, value.IsActive, value.DisplayOrder);
}

/// <summary>Read-only parser/baseline/planner orchestration for WP2 preview.</summary>
public sealed class CatalogueImportService(
    ICatalogueWorkbookImportGateway workbookGateway,
    ICatalogueImportBaselineQueries baselineQueries)
{
    private readonly ICatalogueWorkbookImportGateway workbookGateway = workbookGateway ?? throw new ArgumentNullException(nameof(workbookGateway));
    private readonly ICatalogueImportBaselineQueries baselineQueries = baselineQueries ?? throw new ArgumentNullException(nameof(baselineQueries));

    public async Task<CatalogueImportResult> PreviewAsync(Stream source, CatalogueImportMode mode, string? sourceName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var workbook = await workbookGateway.ReadAsync(source, sourceName, cancellationToken);
        var baseline = workbook.HasErrors ? CatalogueImportBaseline.Empty : await baselineQueries.ReadCatalogueImportBaselineAsync(cancellationToken);
        return new CatalogueImportPlanner().Plan(mode, workbook, baseline, sourceName);
    }
}
