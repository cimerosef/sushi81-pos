using System.Globalization;
using ClosedXML.Excel;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

#pragma warning disable CA1859
#pragma warning disable CA1031

namespace Sushi81.Pos.Infrastructure.Catalogue;

/// <summary>ClosedXML-only untrusted workbook reader.  No ClosedXML type crosses the Application boundary.</summary>
public sealed class ClosedXmlCatalogueWorkbookImportGateway : ICatalogueWorkbookImportGateway
{
    public Task<CatalogueImportWorkbook> ParseAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default) => ReadAsync(source, sourceName, cancellationToken);

    public async Task<CatalogueImportWorkbook> ReadAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return ReadCore(buffer, sourceName, cancellationToken);
    }

    private static CatalogueImportWorkbook ReadCore(Stream source, string? sourceName, CancellationToken cancellationToken)
    {
        var issues = new List<CatalogueImportIssue>();
        try
        {
            using var workbook = new XLWorkbook(source);
            cancellationToken.ThrowIfCancellationRequested();
            var requiredNames = new[] { "Products", "OptionGroups", "Options", CatalogueWorkbookSchema.MetadataSheetName };
            foreach (var name in requiredNames)
                if (!workbook.Worksheets.Any(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal)))
                    AddError(issues, "missing-sheet", $"Required worksheet '{name}' is missing.");
            foreach (var sheet in workbook.Worksheets)
                if (!requiredNames.Contains(sheet.Name, StringComparer.Ordinal))
                    AddError(issues, "unsupported-sheet", $"Unsupported worksheet '{sheet.Name}' is present.");

            var metadata = workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, CatalogueWorkbookSchema.MetadataSheetName, StringComparison.Ordinal));
            if (metadata is null)
                return Empty(sourceName, issues);
            var businessNames = new[] { "Products", "OptionGroups", "Options" };
            foreach (var name in businessNames)
            {
                var businessSheet = workbook.Worksheets.FirstOrDefault(sheet => string.Equals(sheet.Name, name, StringComparison.Ordinal));
                if (businessSheet is not null && businessSheet.Visibility != XLWorksheetVisibility.Visible)
                    AddError(issues, "business-sheet-visibility", $"Business worksheet '{name}' must be visible.", name);
            }
            var businessOrder = workbook.Worksheets.Where(sheet => businessNames.Contains(sheet.Name, StringComparer.Ordinal)).Select(sheet => sheet.Name).ToArray();
            if (!businessNames.SequenceEqual(businessOrder, StringComparer.Ordinal))
                AddError(issues, "sheet-order", "Business worksheets must appear in Products, OptionGroups, Options order.");
            if (metadata.Visibility != XLWorksheetVisibility.VeryHidden)
                AddError(issues, "metadata-visibility", "Technical metadata worksheet must be VeryHidden.", metadata.Name);
            var version = ReadText(metadata.Cell(2, 2), issues, metadata.Name, 2, "contract_version") ?? string.Empty;
            if (!string.Equals(version, CatalogueWorkbookSchema.ContractVersion, StringComparison.Ordinal))
                AddError(issues, "unsupported-contract-version", "Workbook contract version is unsupported.", metadata.Name, 2, "contract_version");
            var exportInstance = ReadText(metadata.Cell(3, 2), issues, metadata.Name, 3, "export_instance_id");
            if (!Guid.TryParse(exportInstance, out var exportId) || exportId == Guid.Empty)
                AddError(issues, "metadata-corrupt", "Export instance identity is malformed.", metadata.Name, 3, "export_instance_id");
            if (!string.Equals(ReadText(metadata.Cell(4, 2), issues, metadata.Name, 4, "visible_sheets"), "Products|OptionGroups|Options", StringComparison.Ordinal))
                AddError(issues, "metadata-corrupt", "Visible worksheet metadata is inconsistent.", metadata.Name, 4, "visible_sheets");
            ValidateDescriptors(metadata, issues);
            var manifest = ReadManifest(metadata, issues);

            var products = ReadProducts(workbook.Worksheets.FirstOrDefault(sheet => sheet.Name == "Products"), issues);
            var groups = ReadGroups(workbook.Worksheets.FirstOrDefault(sheet => sheet.Name == "OptionGroups"), issues);
            var options = ReadOptions(workbook.Worksheets.FirstOrDefault(sheet => sheet.Name == "Options"), issues);
            ValidateBindings(products, groups, options, manifest, issues);
            return new CatalogueImportWorkbook(version, products, groups, options, manifest.Values.OrderBy(value => value.RowKey, StringComparer.Ordinal).ToArray(), issues, sourceName);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            AddError(issues, "unreadable-workbook", $"Workbook could not be read: {exception.Message}");
            return Empty(sourceName, issues);
        }
    }

    private static CatalogueImportWorkbook Empty(string? sourceName, List<CatalogueImportIssue> issues) => new(string.Empty, [], [], [], [], issues, sourceName);

    private static void ValidateDescriptors(IXLWorksheet metadata, List<CatalogueImportIssue> issues)
    {
        var expectedHeader = new[] { "Worksheet", "FieldKey", "ColumnIndex", "BusinessVisible", "Editable", "Role", "Header" };
        for (var column = 0; column < expectedHeader.Length; column++)
            if (!string.Equals(ReadText(metadata.Cell(6, column + 1), issues, metadata.Name, 6, expectedHeader[column]), expectedHeader[column], StringComparison.Ordinal))
                AddError(issues, "descriptor-corrupt", "Metadata descriptor header is inconsistent.", metadata.Name, 6, expectedHeader[column]);
        var row = 7;
        foreach (var expected in CatalogueWorkbookSchema.Fields)
        {
            var actual = new CatalogueWorkbookFieldDescriptor(
                ReadText(metadata.Cell(row, 1), issues, metadata.Name, row, "worksheet") ?? string.Empty,
                ReadText(metadata.Cell(row, 2), issues, metadata.Name, row, "field_key") ?? string.Empty,
                ReadInt(metadata.Cell(row, 3), issues, metadata.Name, row, "column_index") ?? -1,
                ReadBool(metadata.Cell(row, 4), issues, metadata.Name, row, "business_visible") ?? false,
                ReadBool(metadata.Cell(row, 5), issues, metadata.Name, row, "editable") ?? false,
                ReadText(metadata.Cell(row, 6), issues, metadata.Name, row, "role") ?? string.Empty,
                ReadText(metadata.Cell(row, 7), issues, metadata.Name, row, "header") ?? string.Empty);
            if (actual.Worksheet != expected.Worksheet
                || actual.FieldKey != expected.FieldKey
                || actual.ColumnIndex != expected.ColumnIndex
                || actual.BusinessVisible != expected.BusinessVisible
                || actual.Editable != expected.Editable
                || actual.Role != expected.Role
                || string.IsNullOrWhiteSpace(actual.Header))
                AddError(issues, "descriptor-corrupt", "Metadata descriptor does not match the supported workbook schema.", metadata.Name, row, "descriptor");
            row++;
        }
    }

    private static Dictionary<string, CatalogueImportManifestEntry> ReadManifest(IXLWorksheet metadata, List<CatalogueImportIssue> issues)
    {
        var result = new Dictionary<string, CatalogueImportManifestEntry>(StringComparer.Ordinal);
        var headerRow = 0;
        var last = metadata.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 1; row <= last; row++)
            if (string.Equals(ReadText(metadata.Cell(row, 1), issues, metadata.Name, row, "manifest"), "EntityType", StringComparison.Ordinal)) { headerRow = row; break; }
        if (headerRow == 0)
        {
            AddError(issues, "manifest-corrupt", "Workbook manifest header is missing.", metadata.Name);
            return result;
        }
        var expected = new[] { "EntityType", "RowKey", "EntityId", "ParentRowKey", "Worksheet", "RowNumber", "BaselineFingerprint" };
        for (var column = 0; column < expected.Length; column++)
            if (!string.Equals(ReadText(metadata.Cell(headerRow, column + 1), issues, metadata.Name, headerRow, expected[column]), expected[column], StringComparison.Ordinal))
                AddError(issues, "manifest-corrupt", "Workbook manifest header is inconsistent.", metadata.Name, headerRow, expected[column]);
        var parentDisplayHeader = ReadText(metadata.Cell(headerRow, 8), issues, metadata.Name, headerRow, "ParentDisplayFingerprint");
        var hasParentDisplayFingerprint = string.Equals(parentDisplayHeader, "ParentDisplayFingerprint", StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(parentDisplayHeader) && !hasParentDisplayFingerprint)
            AddError(issues, "manifest-corrupt", "Manifest parent-display fingerprint header is inconsistent.", metadata.Name, headerRow, "ParentDisplayFingerprint");
        for (var row = headerRow + 1; row <= last; row++)
        {
            var values = Enumerable.Range(1, 7).Select(column => ReadText(metadata.Cell(row, column), issues, metadata.Name, row, expected[column - 1])).ToArray();
            var parentDisplayFingerprint = hasParentDisplayFingerprint ? ReadText(metadata.Cell(row, 8), issues, metadata.Name, row, "ParentDisplayFingerprint") : null;
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            if (values.Where((value, index) => index != 3).Any(value => value is null)) { AddError(issues, "manifest-corrupt", "Manifest row is incomplete.", metadata.Name, row); continue; }
            if (!Guid.TryParse(values[2], out var id) || id == Guid.Empty) { AddError(issues, "malformed-entity-id", "Manifest EntityId is malformed.", metadata.Name, row, "EntityId"); continue; }
            var manifestRowNumber = ReadInt(metadata.Cell(row, 6), issues, metadata.Name, row, "RowNumber") ?? 0;
            if (string.IsNullOrWhiteSpace(values[1]) || string.IsNullOrWhiteSpace(values[0]) || string.IsNullOrWhiteSpace(values[4]) || string.IsNullOrWhiteSpace(values[6]) || manifestRowNumber < 2 || !result.TryAdd(values[1]!, new CatalogueImportManifestEntry(values[0]!, values[1]!, id, string.IsNullOrWhiteSpace(values[3]) ? null : values[3], values[4]!, manifestRowNumber, values[6]!, string.IsNullOrWhiteSpace(parentDisplayFingerprint) ? null : parentDisplayFingerprint)))
                AddError(issues, "duplicate-manifest-key", "Manifest RowKey is missing or duplicated.", metadata.Name, row, "RowKey");
            else if (values[0] is not ("Product" or "OptionGroup" or "Option") || !new[] { "Products", "OptionGroups", "Options" }.Contains(values[4], StringComparer.Ordinal) || values[6]!.Length != 64 || !values[6]!.All(Uri.IsHexDigit) || (values[0] == "Product" && !string.IsNullOrWhiteSpace(parentDisplayFingerprint)) || (values[0] is "OptionGroup" or "Option") && hasParentDisplayFingerprint && (string.IsNullOrWhiteSpace(parentDisplayFingerprint) || parentDisplayFingerprint!.Length != 64 || !parentDisplayFingerprint.All(Uri.IsHexDigit)))
                AddError(issues, "manifest-corrupt", "Manifest entity type, worksheet or fingerprint is invalid.", metadata.Name, row, "manifest");
        }
        foreach (var entry in result.Values)
        {
            var expectedParentType = entry.EntityType switch { "Product" => null, "OptionGroup" => "Product", "Option" => "OptionGroup", _ => "invalid" };
            if (expectedParentType is null && entry.ParentRowKey is not null || expectedParentType is not null && (entry.ParentRowKey is null || !result.TryGetValue(entry.ParentRowKey, out var parent) || parent.EntityType != expectedParentType))
                AddError(issues, "manifest-corrupt", "Manifest parent relationship is inconsistent.", metadata.Name);
        }
        return result;
    }

    private static IReadOnlyList<CatalogueImportProductRow> ReadProducts(IXLWorksheet? sheet, List<CatalogueImportIssue> issues)
    {
        if (sheet is null) return [];
        ValidateHeaders(sheet, "Products", issues);
        var result = new List<CatalogueImportProductRow>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            if (IsBlankBusinessRow(sheet, row, 9)) { AddFormulaErrors(sheet, row, 1, 11, issues); if (HasTechnicalData(sheet, row, 10, 11)) AddError(issues, "misbound-identity", "A blank Product row contains technical binding data.", sheet.Name, row, "product_row_key"); continue; }
            result.Add(new(row,
                ReadText(sheet.Cell(row, 1), issues, sheet.Name, row, "product_code"), ReadText(sheet.Cell(row, 2), issues, sheet.Name, row, "product_name"),
                ReadText(sheet.Cell(row, 3), issues, sheet.Name, row, "category_name"), ReadText(sheet.Cell(row, 4), issues, sheet.Name, row, "category_short_code"),
                ReadMoney(sheet.Cell(row, 5), issues, sheet.Name, row, "price_ttc"), ReadDecimal(sheet.Cell(row, 6), issues, sheet.Name, row, "vat_rate"),
                ReadBool(sheet.Cell(row, 7), issues, sheet.Name, row, "is_active"), ReadBool(sheet.Cell(row, 8), issues, sheet.Name, row, "discount_eligible"), ReadBool(sheet.Cell(row, 9), issues, sheet.Name, row, "options_enabled"),
                ReadText(sheet.Cell(row, 10), issues, sheet.Name, row, "product_row_key"), ReadText(sheet.Cell(row, 11), issues, sheet.Name, row, "product_id")));
        }
        return result;
    }

    private static IReadOnlyList<CatalogueImportOptionGroupRow> ReadGroups(IXLWorksheet? sheet, List<CatalogueImportIssue> issues)
    {
        if (sheet is null) return [];
        ValidateHeaders(sheet, "OptionGroups", issues);
        var result = new List<CatalogueImportOptionGroupRow>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            if (IsBlankBusinessRow(sheet, row, 8)) { AddFormulaErrors(sheet, row, 1, 12, issues); if (HasTechnicalData(sheet, row, 9, 12)) AddError(issues, "misbound-identity", "A blank OptionGroup row contains technical binding data.", sheet.Name, row, "option_group_row_key"); continue; }
            result.Add(new(row,
                ReadText(sheet.Cell(row, 1), issues, sheet.Name, row, "product_code"), ReadText(sheet.Cell(row, 2), issues, sheet.Name, row, "product_name"), ReadText(sheet.Cell(row, 3), issues, sheet.Name, row, "group_name"),
                ReadText(sheet.Cell(row, 4), issues, sheet.Name, row, "selection_mode"), ReadBool(sheet.Cell(row, 5), issues, sheet.Name, row, "is_required"), ReadNullableInt(sheet.Cell(row, 6), issues, sheet.Name, row, "min_selections"), ReadNullableInt(sheet.Cell(row, 7), issues, sheet.Name, row, "max_selections"), ReadInt(sheet.Cell(row, 8), issues, sheet.Name, row, "display_order"),
                ReadText(sheet.Cell(row, 9), issues, sheet.Name, row, "product_row_key"), ReadText(sheet.Cell(row, 10), issues, sheet.Name, row, "option_group_row_key"), ReadText(sheet.Cell(row, 11), issues, sheet.Name, row, "product_id"), ReadText(sheet.Cell(row, 12), issues, sheet.Name, row, "option_group_id")));
        }
        return result;
    }

    private static IReadOnlyList<CatalogueImportOptionRow> ReadOptions(IXLWorksheet? sheet, List<CatalogueImportIssue> issues)
    {
        if (sheet is null) return [];
        ValidateHeaders(sheet, "Options", issues);
        var result = new List<CatalogueImportOptionRow>();
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            if (IsBlankBusinessRow(sheet, row, 7)) { AddFormulaErrors(sheet, row, 1, 12, issues); if (HasTechnicalData(sheet, row, 8, 12)) AddError(issues, "misbound-identity", "A blank Option row contains technical binding data.", sheet.Name, row, "option_row_key"); continue; }
            result.Add(new(row,
                ReadText(sheet.Cell(row, 1), issues, sheet.Name, row, "product_code"), ReadText(sheet.Cell(row, 2), issues, sheet.Name, row, "product_name"), ReadText(sheet.Cell(row, 3), issues, sheet.Name, row, "option_group_name"), ReadText(sheet.Cell(row, 4), issues, sheet.Name, row, "option_name"),
                ReadMoney(sheet.Cell(row, 5), issues, sheet.Name, row, "price_adjustment_ttc"), ReadBool(sheet.Cell(row, 6), issues, sheet.Name, row, "is_active"), ReadInt(sheet.Cell(row, 7), issues, sheet.Name, row, "display_order"),
                ReadText(sheet.Cell(row, 8), issues, sheet.Name, row, "product_row_key"), ReadText(sheet.Cell(row, 9), issues, sheet.Name, row, "option_group_row_key"), ReadText(sheet.Cell(row, 10), issues, sheet.Name, row, "option_row_key"), ReadText(sheet.Cell(row, 11), issues, sheet.Name, row, "option_id"), ReadText(sheet.Cell(row, 12), issues, sheet.Name, row, "option_group_id")));
        }
        return result;
    }

    private static void ValidateHeaders(IXLWorksheet sheet, string worksheet, List<CatalogueImportIssue> issues)
    {
        foreach (var descriptor in CatalogueWorkbookSchema.Fields.Where(value => value.Worksheet == worksheet && value.BusinessVisible))
        {
            var cell = sheet.Cell(1, descriptor.ColumnIndex);
            if (cell.HasFormula)
                AddError(issues, "formula-not-allowed", "Formula cells are not supported in the import contract.", worksheet, 1, descriptor.FieldKey);
            else if (cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.GetString()))
                AddError(issues, "missing-header", "Business worksheet header is missing.", worksheet, 1, descriptor.FieldKey);
        }
    }

    private static void ValidateBindings(IReadOnlyList<CatalogueImportProductRow> products, IReadOnlyList<CatalogueImportOptionGroupRow> groups, IReadOnlyList<CatalogueImportOptionRow> options, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, List<CatalogueImportIssue> issues)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in products)
            ValidateRowBinding("Product", row.ProductRowKey, row.ProductId, null, "Products", row.ExcelRow, manifest, seen, issues);
        foreach (var row in groups)
            ValidateRowBinding("OptionGroup", row.OptionGroupRowKey, row.OptionGroupId, row.ProductRowKey, "OptionGroups", row.ExcelRow, manifest, seen, issues, row.ProductId);
        foreach (var row in options)
        {
            ValidateRowBinding("Option", row.OptionRowKey, row.OptionId, row.OptionGroupRowKey, "Options", row.ExcelRow, manifest, seen, issues, row.OptionGroupId);
            ValidateOptionProductBinding(row, manifest, issues);
        }
    }

    private static void ValidateOptionProductBinding(CatalogueImportOptionRow row, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, List<CatalogueImportIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(row.ProductRowKey)) return;
        if (string.IsNullOrWhiteSpace(row.OptionGroupRowKey) || !manifest.TryGetValue(row.OptionGroupRowKey, out var groupEntry) || groupEntry.EntityType != "OptionGroup")
        {
            AddError(issues, "wrong-parent-binding", "Option Product row binding cannot be resolved through its OptionGroup manifest entry.", "Options", row.ExcelRow, "product_row_key");
            return;
        }

        if (!string.Equals(groupEntry.ParentRowKey, row.ProductRowKey, StringComparison.Ordinal))
            AddError(issues, "wrong-parent-binding", "Option Product row binding does not match the bound OptionGroup parent Product.", "Options", row.ExcelRow, "product_row_key");
    }

    private static void ValidateRowBinding(string type, string? rowKey, string? idText, string? parentKey, string worksheet, int row, IReadOnlyDictionary<string, CatalogueImportManifestEntry> manifest, HashSet<string> seen, List<CatalogueImportIssue> issues, string? parentIdText = null)
    {
        var hasKey = !string.IsNullOrWhiteSpace(rowKey);
        var hasId = !string.IsNullOrWhiteSpace(idText);
        if (hasKey != hasId) AddError(issues, "misbound-identity", "Existing identity and row binding must be supplied together.", worksheet, row, hasKey ? "entity-id" : "row-key");
        if (hasKey && !seen.Add(rowKey!)) AddError(issues, "duplicate-row-binding", "Visible row binding is duplicated.", worksheet, row, "row-key");
        if (hasId && (!Guid.TryParse(idText, out var id) || id == Guid.Empty)) AddError(issues, "malformed-entity-id", "Entity identity is malformed.", worksheet, row, "entity-id");
        if (!hasKey) return;
        if (!manifest.TryGetValue(rowKey!, out var entry)) { AddError(issues, "unknown-row-key", "Row binding is not present in the metadata manifest.", worksheet, row, "row-key"); return; }
        if (entry.EntityType != type || !Guid.TryParse(idText, out var parsed) || parsed != entry.EntityId)
            AddError(issues, "misbound-identity", "Visible row identity does not match its manifest binding.", worksheet, row, "entity-id");
        if (!string.Equals(entry.Worksheet, worksheet, StringComparison.Ordinal)) AddError(issues, "misbound-identity", "Manifest worksheet binding is incorrect.", worksheet, row, "row-key");
        if (parentKey is not null && !string.Equals(parentKey, entry.ParentRowKey, StringComparison.Ordinal)) AddError(issues, "wrong-parent-binding", "Visible parent helper does not match the manifest binding.", worksheet, row, "parent-row-key");
        if (parentIdText is not null && !string.IsNullOrWhiteSpace(parentIdText))
        {
            if (!Guid.TryParse(parentIdText, out var parentId) || entry.ParentRowKey is null || !manifest.TryGetValue(entry.ParentRowKey, out var parentEntry) || parentEntry.EntityId != parentId)
                AddError(issues, "wrong-parent-binding", "Visible parent identity is inconsistent with the manifest.", worksheet, row, "parent-entity-id");
        }
    }

    private static bool IsBlankBusinessRow(IXLWorksheet sheet, int row, int columns) => Enumerable.Range(1, columns).All(column => string.IsNullOrWhiteSpace(sheet.Cell(row, column).GetString()));
    private static bool HasTechnicalData(IXLWorksheet sheet, int row, int firstColumn, int lastColumn) => Enumerable.Range(firstColumn, lastColumn - firstColumn + 1).Any(column => sheet.Cell(row, column).HasFormula || !string.IsNullOrWhiteSpace(sheet.Cell(row, column).GetString()));
    private static void AddFormulaErrors(IXLWorksheet sheet, int row, int firstColumn, int lastColumn, List<CatalogueImportIssue> issues)
    {
        foreach (var column in Enumerable.Range(firstColumn, lastColumn - firstColumn + 1).Where(column => sheet.Cell(row, column).HasFormula))
        {
            var field = CatalogueWorkbookSchema.Fields.FirstOrDefault(value => value.Worksheet == sheet.Name && value.ColumnIndex == column)?.FieldKey ?? $"column_{column}";
            AddError(issues, "formula-not-allowed", "Formula cells are not supported in the import contract.", sheet.Name, row, field);
        }
    }
    private static string? ReadText(IXLCell cell, List<CatalogueImportIssue> issues, string worksheet, int row, string field)
    {
        if (cell.HasFormula) { AddError(issues, "formula-not-allowed", "Formula cells are not supported in the import contract.", worksheet, row, field); return null; }
        return cell.IsEmpty() ? null : cell.GetString();
    }
    private static decimal? ReadDecimal(IXLCell cell, List<CatalogueImportIssue> issues, string worksheet, int row, string field)
    {
        if (cell.HasFormula) { AddError(issues, "formula-not-allowed", "Formula cells are not supported in the import contract.", worksheet, row, field); return null; }
        try { return cell.IsEmpty() ? null : cell.GetValue<decimal>(); }
        catch { AddError(issues, "invalid-scalar", "Numeric value is invalid.", worksheet, row, field); return null; }
    }
    private static Money? ReadMoney(IXLCell cell, List<CatalogueImportIssue> issues, string worksheet, int row, string field)
    {
        var value = ReadDecimal(cell, issues, worksheet, row, field);
        return value is { } euros ? Money.FromEuros(euros) : null;
    }
    private static int? ReadInt(IXLCell cell, List<CatalogueImportIssue> issues, string worksheet, int row, string field)
    {
        var value = ReadDecimal(cell, issues, worksheet, row, field);
        if (value is null) return null;
        if (value.Value != decimal.Truncate(value.Value) || value.Value < int.MinValue || value.Value > int.MaxValue) { AddError(issues, "invalid-scalar", "Integer value is invalid.", worksheet, row, field); return null; }
        return (int)value.Value;
    }
    private static int? ReadNullableInt(IXLCell cell, List<CatalogueImportIssue> issues, string worksheet, int row, string field)
    {
        if (cell.HasFormula) { AddError(issues, "formula-not-allowed", "Formula cells are not supported in the import contract.", worksheet, row, field); return null; }
        return cell.IsEmpty() || string.IsNullOrWhiteSpace(cell.GetString()) ? null : ReadInt(cell, issues, worksheet, row, field);
    }
    private static bool? ReadBool(IXLCell cell, List<CatalogueImportIssue> issues, string worksheet, int row, string field)
    {
        if (cell.HasFormula) { AddError(issues, "formula-not-allowed", "Formula cells are not supported in the import contract.", worksheet, row, field); return null; }
        if (cell.IsEmpty()) return null;
        try { return cell.GetValue<bool>(); }
        catch { AddError(issues, "invalid-scalar", "Boolean value is invalid.", worksheet, row, field); return null; }
    }
    private static void AddError(List<CatalogueImportIssue> issues, string code, string message, string? worksheet = null, int? row = null, string? field = null) => issues.Add(new(CatalogueImportIssueSeverity.Error, code, message, worksheet, row, field));
}
