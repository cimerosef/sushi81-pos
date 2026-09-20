using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Catalogue;

/// <summary>The two explicit Catalogue workbook import modes.</summary>
public enum CatalogueImportMode
{
    Update,
    AddOnly
}

public enum CatalogueImportIssueSeverity
{
    Error,
    Warning
}

/// <summary>Stable, row-addressable parser/planner diagnostic.</summary>
public sealed record CatalogueImportIssue(
    CatalogueImportIssueSeverity Severity,
    string Code,
    string Message,
    string? Worksheet = null,
    int? ExcelRow = null,
    string? FieldKey = null,
    IReadOnlyDictionary<string, string>? Arguments = null)
{
    public bool IsBlocking => Severity == CatalogueImportIssueSeverity.Error;
}

/// <summary>Opaque metadata manifest binding for one exported entity row.</summary>
public sealed record CatalogueImportManifestEntry(
    string EntityType,
    string RowKey,
    Guid EntityId,
    string? ParentRowKey,
    string Worksheet,
    int RowNumber,
    string BaselineFingerprint,
    string? ParentDisplayFingerprint = null);

/// <summary>Raw, library-neutral Product worksheet row.</summary>
public sealed record CatalogueImportProductRow(
    int ExcelRow,
    string? ProductCode,
    string? ProductName,
    string? CategoryName,
    string? CategoryShortCode,
    Money? PriceTtc,
    decimal? VatRate,
    bool? IsActive,
    bool? DiscountEligible,
    bool? OptionsEnabled,
    string? ProductRowKey,
    string? ProductId);

/// <summary>Raw, library-neutral OptionGroup worksheet row.</summary>
public sealed record CatalogueImportOptionGroupRow(
    int ExcelRow,
    string? ProductCode,
    string? ProductName,
    string? GroupName,
    string? SelectionMode,
    bool? IsRequired,
    int? MinSelections,
    int? MaxSelections,
    int? DisplayOrder,
    string? ProductRowKey,
    string? OptionGroupRowKey,
    string? ProductId,
    string? OptionGroupId);

/// <summary>Raw, library-neutral Option worksheet row.</summary>
public sealed record CatalogueImportOptionRow(
    int ExcelRow,
    string? ProductCode,
    string? ProductName,
    string? OptionGroupName,
    string? OptionName,
    Money? PriceAdjustmentTtc,
    bool? IsActive,
    int? DisplayOrder,
    string? ProductRowKey,
    string? OptionGroupRowKey,
    string? OptionRowKey,
    string? OptionId,
    string? OptionGroupId);

/// <summary>Parsed workbook with issues kept separate from the immutable raw model.</summary>
public sealed record CatalogueImportWorkbook(
    string ContractVersion,
    IReadOnlyList<CatalogueImportProductRow> Products,
    IReadOnlyList<CatalogueImportOptionGroupRow> OptionGroups,
    IReadOnlyList<CatalogueImportOptionRow> Options,
    IReadOnlyList<CatalogueImportManifestEntry> Manifest,
    IReadOnlyList<CatalogueImportIssue> Issues,
    string? SourceName = null)
{
    public bool HasErrors => Issues.Any(issue => issue.IsBlocking);
}

/// <summary>Current Category state used by the pure import planner.</summary>
public sealed record CatalogueImportCategory(Guid Id, string Name, string? ShortCode)
{
    public string NormalizedName => CatalogueNormalization.Key(Name);
    public string? NormalizedShortCode => string.IsNullOrWhiteSpace(ShortCode) ? null : CatalogueNormalization.Key(ShortCode);
}

/// <summary>Complete current Product state, including all child relationships.</summary>
public sealed record CatalogueImportBaselineProduct(
    Guid Id,
    string Code,
    string Name,
    Guid CategoryId,
    string CategoryName,
    string? CategoryShortCode,
    Money PriceTtc,
    decimal VatRate,
    bool IsActive,
    bool DiscountEligible,
    bool OptionsEnabled,
    IReadOnlyList<CatalogueImportBaselineOptionGroup> OptionGroups)
{
    public string NormalizedCode => CatalogueNormalization.Key(Code);
}

public sealed record CatalogueImportBaselineOptionGroup(
    Guid Id,
    Guid ProductId,
    string Name,
    SelectionMode SelectionMode,
    bool IsRequired,
    int? MinSelections,
    int? MaxSelections,
    int DisplayOrder,
    IReadOnlyList<CatalogueImportBaselineOption> Options);

public sealed record CatalogueImportBaselineOption(
    Guid Id,
    Guid OptionGroupId,
    string Name,
    Money PriceAdjustmentTtc,
    bool IsActive,
    int DisplayOrder);

public sealed record CatalogueImportBaseline(
    IReadOnlyList<CatalogueImportCategory> Categories,
    IReadOnlyList<CatalogueImportBaselineProduct> Products)
{
    public static CatalogueImportBaseline Empty { get; } = new([], []);
}

/// <summary>Mandatory one-snapshot read seam for import preview.</summary>
public interface ICatalogueImportBaselineQueries
{
    Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default);
}

/// <summary>Optional combined read-side name reserved for the future WP3 import store.</summary>
public interface ICatalogueImportStore : ICatalogueImportBaselineQueries
{
    Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// The immutable preview capture handed to the single WP3 commit transaction.
/// The baseline is carried by value so the persistence boundary can compare the
/// complete Catalogue on the same live connection before its first write.
/// </summary>
public sealed record CatalogueImportCommitRequest(
    CatalogueImportPlan Plan,
    CatalogueImportBaseline PreviewBaseline,
    string? ExpectedBaselineFingerprint = null)
{
    public string BaselineFingerprint => ExpectedBaselineFingerprint ?? CatalogueImportBaselineFingerprint.Compute(PreviewBaseline);
}

/// <summary>Durable result of one atomic Catalogue import attempt.</summary>
public sealed record CatalogueImportCommitResult(
    bool Succeeded,
    bool Changed,
    IReadOnlyList<CatalogueImportIssue> Issues,
    IReadOnlyDictionary<string, Guid>? AllocatedIds = null)
{
    public bool IsBlocking => !Succeeded || Issues.Any(issue => issue.IsBlocking);

    public static CatalogueImportCommitResult Success(bool changed, IReadOnlyDictionary<string, Guid>? allocatedIds = null) =>
        new(true, changed, [], allocatedIds ?? new Dictionary<string, Guid>(StringComparer.Ordinal));

    public static CatalogueImportCommitResult Failure(params CatalogueImportIssue[] issues) =>
        new(false, false, issues);
}

public interface ICatalogueWorkbookImportGateway
{
    Task<CatalogueImportWorkbook> ReadAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default);
    Task<CatalogueImportWorkbook> ParseAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default) => ReadAsync(source, sourceName, cancellationToken);
}

public enum CatalogueImportEntityType
{
    Product,
    OptionGroup,
    Option,
    Category
}

public enum CatalogueImportOperationKind
{
    Create,
    Modify,
    Activate,
    Deactivate
}

/// <summary>
/// Stable reference carried by an import plan. Existing records carry their
/// opaque durable id and local row key; planned records carry only a local key.
/// </summary>
public sealed record CatalogueImportEntityReference(Guid? ExistingId, string LocalKey)
{
    public bool IsExisting => ExistingId is not null;
    public Guid? EntityId => ExistingId;

    public static CatalogueImportEntityReference Existing(Guid id, string localKey) => new(id, localKey);

    public static CatalogueImportEntityReference New(string localKey) => new(null, localKey);
}

/// <summary>New Category data carried by a preview plan without a preview-generated Guid.</summary>
public sealed record CatalogueImportPlannedCategory(string LocalKey, string Name, string? ShortCode)
{
    public string CategoryLocalKey => LocalKey;
    public string? DisplayShortCode => ShortCode;
    public string NormalizedName => CatalogueNormalization.Key(Name);
    public string? NormalizedShortCode => string.IsNullOrWhiteSpace(ShortCode) ? null : CatalogueNormalization.Key(ShortCode);
}

/// <summary>One explicit operation for the later WP3 commit path. There is deliberately no Delete kind.</summary>
public sealed record CatalogueImportOperation(
    CatalogueImportEntityType EntityType,
    CatalogueImportOperationKind Kind,
    Guid? EntityId,
    string LocalKey,
    int? ExcelRow,
    string? Worksheet,
    IReadOnlyDictionary<string, string?> Values,
    CatalogueImportEntityReference? EntityReference = null,
    CatalogueImportEntityReference? CategoryReference = null,
    CatalogueImportEntityReference? ParentReference = null);

public sealed record CatalogueImportAffectedRow(
    string Worksheet,
    int ExcelRow,
    CatalogueImportEntityType EntityType,
    string LocalKey,
    IReadOnlyList<CatalogueImportOperationKind> Operations);

/// <summary>Immutable no-delete candidate handed to WP3. New records have local keys only.</summary>
public sealed record CatalogueImportPlan(
    CatalogueImportMode Mode,
    IReadOnlyList<CatalogueImportOperation> Operations,
    IReadOnlyList<CatalogueImportAffectedRow> AffectedRows,
    IReadOnlyList<CatalogueImportPlannedCategory> NewCategories,
    string? SourceName)
{
}

public sealed record CatalogueImportPreview(
    CatalogueImportMode Mode,
    string? SourceName,
    int ProductCreateCount,
    int ProductModifyCount,
    int ProductActivateCount,
    int ProductDeactivateCount,
    int OptionGroupCreateCount,
    int OptionGroupModifyCount,
    int OptionCreateCount,
    int OptionModifyCount,
    int OptionActivateCount,
    int OptionDeactivateCount,
    int NewCategoryCount,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<CatalogueImportIssue> Issues,
    IReadOnlyList<CatalogueImportAffectedRow> AffectedRows,
    bool OmittedRowsAreNotDeleted = true,
    bool DatabaseWasChanged = false,
    bool ExistingRecordsWillNotBeUpdated = false);

public sealed record CatalogueImportResult(CatalogueImportPreview Preview, CatalogueImportPlan? Plan, CatalogueImportBaseline? PreviewBaseline = null)
{
    public bool HasErrors => Preview.ErrorCount > 0;
}

/// <summary>
/// Canonical, order-independent token for the complete current Catalogue.
/// It intentionally includes opaque identities and parent relationships so a
/// commit cannot silently apply a plan to a different hierarchy.
/// </summary>
public static class CatalogueImportBaselineFingerprint
{
    public static string Compute(CatalogueImportBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var text = new System.Text.StringBuilder("sushi81-catalogue-baseline-v1;");
        foreach (var category in (baseline.Categories ?? []).OrderBy(value => value.Id))
        {
            Append(text, "category", category.Id.ToString("D"), category.Name, category.ShortCode);
        }

        foreach (var product in (baseline.Products ?? []).OrderBy(value => value.Id))
        {
            Append(text, "product", product.Id.ToString("D"), product.Code, product.Name, product.CategoryId.ToString("D"), product.CategoryName,
                product.CategoryShortCode, product.PriceTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture),
                product.VatRate.ToString(System.Globalization.CultureInfo.InvariantCulture), product.IsActive ? "1" : "0",
                product.DiscountEligible ? "1" : "0", product.OptionsEnabled ? "1" : "0");
            foreach (var group in (product.OptionGroups ?? []).OrderBy(value => value.Id))
            {
                Append(text, "group", group.Id.ToString("D"), group.ProductId.ToString("D"), group.Name, group.SelectionMode.ToString(),
                    group.IsRequired ? "1" : "0", group.MinSelections?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    group.MaxSelections?.ToString(System.Globalization.CultureInfo.InvariantCulture), group.DisplayOrder.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (var option in (group.Options ?? []).OrderBy(value => value.Id))
                {
                    Append(text, "option", option.Id.ToString("D"), option.OptionGroupId.ToString("D"), option.Name,
                        option.PriceAdjustmentTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture), option.IsActive ? "1" : "0",
                        option.DisplayOrder.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    private static void Append(System.Text.StringBuilder text, string kind, params string?[] values)
    {
        text.Append(kind.Length).Append(':').Append(kind).Append(';');
        foreach (var value in values)
        {
            if (value is null) text.Append("N;");
            else text.Append('V').Append(value.Length).Append(':').Append(value).Append(';');
        }
    }
}

/// <summary>Application-owned canonical typed fingerprint shared by WP1 export and WP2 planning.</summary>
public static class CatalogueWorkbookFingerprint
{
    public static string ParentProduct(string code, string name) => Fingerprint(
        ("product-code", "string", code),
        ("product-name", "string", name));

    public static string ParentOptionGroup(string productCode, string productName, string groupName) => Fingerprint(
        ("product-code", "string", productCode),
        ("product-name", "string", productName),
        ("option-group-name", "string", groupName));

    public static string Product(CatalogueWorkbookProduct product) => Fingerprint(
        ("code", "string", product.Code),
        ("name", "string", product.Name),
        ("category-name", "string", product.CategoryName),
        ("category-short-code", "string", product.CategoryShortCode ?? string.Empty),
        ("price-cents", "int64", product.PriceTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("vat-rate", "decimal", product.VatRate.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("active", "bool", product.IsActive ? "true" : "false"),
        ("discount-eligible", "bool", product.DiscountEligible ? "true" : "false"),
        ("options-enabled", "bool", product.OptionsEnabled ? "true" : "false"));

    public static string OptionGroup(CatalogueWorkbookOptionGroup group) => Fingerprint(
        ("name", "string", group.Name),
        ("selection-mode", "enum", group.SelectionMode.ToString()),
        ("required", "bool", group.IsRequired ? "true" : "false"),
        ("min-selections", "int32", group.MinSelections?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("max-selections", "int32", group.MaxSelections?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("display-order", "int32", group.DisplayOrder.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public static string Option(CatalogueWorkbookOption option) => Fingerprint(
        ("name", "string", option.Name),
        ("price-adjustment-cents", "int64", option.PriceAdjustmentTtc.Cents.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ("active", "bool", option.IsActive ? "true" : "false"),
        ("display-order", "int32", option.DisplayOrder.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private static string Fingerprint(params (string Name, string Type, string? Value)[] fields)
    {
        var canonical = new System.Text.StringBuilder();
        foreach (var (name, type, value) in fields)
        {
            canonical.Append(name.Length).Append(':').Append(name);
            canonical.Append(type.Length).Append(':').Append(type);
            if (value is null) canonical.Append("N;");
            else canonical.Append('V').Append(value.Length).Append(':').Append(value).Append(';');
        }

        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }
}
