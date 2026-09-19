using ClosedXML.Excel;
using Sushi81.Pos.Application.Catalogue;

namespace Sushi81.Pos.Infrastructure.Catalogue;

/// <summary>
/// ClosedXML-only workbook writer. The Application layer sees only
/// <see cref="ICatalogueWorkbookGateway"/> and its DTOs.
/// </summary>
public sealed class ClosedXmlCatalogueWorkbookGateway : ICatalogueWorkbookGateway, ICatalogueWorkbookImportGateway
{
    public Task<CatalogueImportWorkbook> ReadAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default) => new ClosedXmlCatalogueWorkbookImportGateway().ReadAsync(source, sourceName, cancellationToken);

    private const string MetadataSheetName = CatalogueWorkbookSchema.MetadataSheetName;
    private const string ContractVersion = CatalogueWorkbookSchema.ContractVersion;

    public Task WriteAsync(CatalogueWorkbookExport model, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();

        using var workbook = new XLWorkbook();
        var products = workbook.Worksheets.Add("Products");
        var groups = workbook.Worksheets.Add("OptionGroups");
        var options = workbook.Worksheets.Add("Options");
        var metadata = workbook.Worksheets.Add(MetadataSheetName);

        WriteProducts(products, model.Products);
        WriteGroups(groups, model.Products);
        WriteOptions(options, model.Products);
        WriteMetadata(metadata, model);

        metadata.Visibility = XLWorksheetVisibility.VeryHidden;
        Protect(products);
        Protect(groups);
        Protect(options);
        Protect(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(destination);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private static void WriteProducts(IXLWorksheet sheet, IReadOnlyList<CatalogueWorkbookProduct> products)
    {
        var headers = HeadersFor("Products");
        WriteHeader(sheet, headers);
        var row = 2;
        foreach (var product in products.OrderBy(value => value.Code, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.ProductId))
        {
            var key = ProductKey(product.ProductId);
            sheet.Cell(row, 1).Value = product.Code;
            sheet.Cell(row, 2).Value = product.Name;
            sheet.Cell(row, 3).Value = product.CategoryName;
            sheet.Cell(row, 4).Value = product.CategoryShortCode ?? string.Empty;
            sheet.Cell(row, 5).Value = product.PriceTtc.Euros;
            sheet.Cell(row, 6).Value = product.VatRate;
            sheet.Cell(row, 7).Value = product.IsActive;
            sheet.Cell(row, 8).Value = product.DiscountEligible;
            sheet.Cell(row, 9).Value = product.OptionsEnabled;
            sheet.Cell(row, 10).Value = key;
            sheet.Cell(row, 11).Value = product.ProductId.ToString("D");
            row++;
        }

        sheet.Column(5).Style.NumberFormat.Format = "0.00";
        sheet.Column(6).Style.NumberFormat.Format = "0.00";
        FinishVisibleSheet(sheet, row - 1, 9, 11);
    }

    private static void WriteGroups(IXLWorksheet sheet, IReadOnlyList<CatalogueWorkbookProduct> products)
    {
        var headers = HeadersFor("OptionGroups");
        WriteHeader(sheet, headers);
        var row = 2;
        foreach (var product in OrderedProducts(products))
        foreach (var group in product.OptionGroups.OrderBy(value => value.DisplayOrder).ThenBy(value => value.OptionGroupId))
        {
            sheet.Cell(row, 1).Value = group.ProductCode;
            sheet.Cell(row, 2).Value = group.ProductName;
            sheet.Cell(row, 3).Value = group.Name;
            sheet.Cell(row, 4).Value = group.SelectionMode.ToString().ToUpperInvariant();
            sheet.Cell(row, 5).Value = group.IsRequired;
            SetNullableInteger(sheet.Cell(row, 6), group.MinSelections);
            SetNullableInteger(sheet.Cell(row, 7), group.MaxSelections);
            sheet.Cell(row, 8).Value = group.DisplayOrder;
            sheet.Cell(row, 9).Value = ProductKey(group.ProductId);
            sheet.Cell(row, 10).Value = GroupKey(group.OptionGroupId);
            sheet.Cell(row, 11).Value = group.ProductId.ToString("D");
            sheet.Cell(row, 12).Value = group.OptionGroupId.ToString("D");
            row++;
        }

        FinishVisibleSheet(sheet, row - 1, 8, 12);
    }

    private static void WriteOptions(IXLWorksheet sheet, IReadOnlyList<CatalogueWorkbookProduct> products)
    {
        var headers = HeadersFor("Options");
        WriteHeader(sheet, headers);
        var row = 2;
        foreach (var product in OrderedProducts(products))
        foreach (var group in product.OptionGroups.OrderBy(value => value.DisplayOrder).ThenBy(value => value.OptionGroupId))
        foreach (var option in group.Options.OrderBy(value => value.DisplayOrder).ThenBy(value => value.OptionId))
        {
            sheet.Cell(row, 1).Value = option.ProductCode;
            sheet.Cell(row, 2).Value = option.ProductName;
            sheet.Cell(row, 3).Value = option.OptionGroupName;
            sheet.Cell(row, 4).Value = option.Name;
            sheet.Cell(row, 5).Value = option.PriceAdjustmentTtc.Euros;
            sheet.Cell(row, 6).Value = option.IsActive;
            sheet.Cell(row, 7).Value = option.DisplayOrder;
            sheet.Cell(row, 8).Value = ProductKey(product.ProductId);
            sheet.Cell(row, 9).Value = GroupKey(option.OptionGroupId);
            sheet.Cell(row, 10).Value = OptionKey(option.OptionId);
            sheet.Cell(row, 11).Value = option.OptionId.ToString("D");
            sheet.Cell(row, 12).Value = option.OptionGroupId.ToString("D");
            row++;
        }

        sheet.Column(5).Style.NumberFormat.Format = "0.00";
        FinishVisibleSheet(sheet, row - 1, 7, 12);
    }

    private static void WriteMetadata(IXLWorksheet sheet, CatalogueWorkbookExport export)
    {
        sheet.Cell(1, 1).Value = "Key";
        sheet.Cell(1, 2).Value = "Value";
        sheet.Cell(2, 1).Value = "ContractVersion";
        sheet.Cell(2, 2).Value = ContractVersion;
        sheet.Cell(3, 1).Value = "ExportInstanceId";
        sheet.Cell(3, 2).Value = export.ExportInstanceId.ToString("D");
        sheet.Cell(4, 1).Value = "VisibleSheets";
        sheet.Cell(4, 2).Value = "Products|OptionGroups|Options";
        var descriptorHeaders = new[] { "Worksheet", "FieldKey", "ColumnIndex", "BusinessVisible", "Editable", "Role", "Header" };
        WriteMetadataHeader(sheet, 6, descriptorHeaders);
        var row = 7;
        foreach (var descriptor in CatalogueWorkbookSchema.Fields)
        {
            sheet.Cell(row, 1).Value = descriptor.Worksheet;
            sheet.Cell(row, 2).Value = descriptor.FieldKey;
            sheet.Cell(row, 3).Value = descriptor.ColumnIndex;
            sheet.Cell(row, 4).Value = descriptor.BusinessVisible;
            sheet.Cell(row, 5).Value = descriptor.Editable;
            sheet.Cell(row, 6).Value = descriptor.Role;
            sheet.Cell(row, 7).Value = descriptor.Header;
            row++;
        }

        row++;
        var manifestHeaderRow = row++;
        WriteMetadataHeader(sheet, manifestHeaderRow, ["EntityType", "RowKey", "EntityId", "ParentRowKey", "Worksheet", "RowNumber", "BaselineFingerprint", "ParentDisplayFingerprint"]);
        var productWorksheetRow = 2;
        var groupWorksheetRow = 2;
        var optionWorksheetRow = 2;
        foreach (var product in OrderedProducts(export.Products))
        {
            AddManifest(sheet, ref row, "Product", ProductKey(product.ProductId), product.ProductId, null, "Products", productWorksheetRow++, CatalogueWorkbookFingerprint.Product(product), null);
            foreach (var group in product.OptionGroups.OrderBy(value => value.DisplayOrder).ThenBy(value => value.OptionGroupId))
            {
                AddManifest(sheet, ref row, "OptionGroup", GroupKey(group.OptionGroupId), group.OptionGroupId, ProductKey(product.ProductId), "OptionGroups", groupWorksheetRow++, CatalogueWorkbookFingerprint.OptionGroup(group), CatalogueWorkbookFingerprint.ParentProduct(product.Code, product.Name));
                foreach (var option in group.Options.OrderBy(value => value.DisplayOrder).ThenBy(value => value.OptionId))
                    AddManifest(sheet, ref row, "Option", OptionKey(option.OptionId), option.OptionId, GroupKey(group.OptionGroupId), "Options", optionWorksheetRow++, CatalogueWorkbookFingerprint.Option(option), CatalogueWorkbookFingerprint.ParentOptionGroup(product.Code, product.Name, group.Name));
            }
        }

        sheet.Columns().AdjustToContents();
    }

    private static string[] HeadersFor(string worksheet) =>
        CatalogueWorkbookSchema.Fields
            .Where(field => string.Equals(field.Worksheet, worksheet, StringComparison.Ordinal))
            .OrderBy(field => field.ColumnIndex)
            .Select(field => field.Header)
            .ToArray();

    private static void WriteMetadataHeader(IXLWorksheet sheet, int row, string[] headers)
    {
        for (var column = 0; column < headers.Length; column++) sheet.Cell(row, column + 1).Value = headers[column];
        sheet.Row(row).Style.Font.Bold = true;
        sheet.Row(row).Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    private static void AddManifest(IXLWorksheet sheet, ref int row, string entityType, string key, Guid entityId, string? parentKey, string worksheet, int rowNumber, string fingerprint, string? parentDisplayFingerprint)
    {
        sheet.Cell(row, 1).Value = entityType;
        sheet.Cell(row, 2).Value = key;
        sheet.Cell(row, 3).Value = entityId.ToString("D");
        sheet.Cell(row, 4).Value = parentKey ?? string.Empty;
        sheet.Cell(row, 5).Value = worksheet;
        sheet.Cell(row, 6).Value = rowNumber;
        sheet.Cell(row, 7).Value = fingerprint;
        sheet.Cell(row, 8).Value = parentDisplayFingerprint ?? string.Empty;
        row++;
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    private static void FinishVisibleSheet(IXLWorksheet sheet, int lastRow, int businessColumns, int technicalColumns)
    {
        var effectiveLastRow = Math.Max(1, lastRow);
        var newRowTemplate = Math.Max(2, lastRow + 1);

        // Excel refuses to sort a protected range when any cell in that range is
        // locked.  Row-local helpers therefore use the same unlocked cell state as
        // business cells, while remaining hidden and excluded from FormatColumns /
        // unhide permissions.  The VeryHidden manifest remains authoritative and
        // future import must validate every helper against it.
        // Excel's protected-sheet sort checks the complete AutoFilter range,
        // including its header row. Keep that range unlocked so header/filter
        // sorting works in real Excel; importer header validation remains the
        // trust boundary for any external header edit.
        sheet.Range(1, 1, newRowTemplate, technicalColumns).Style.Protection.Locked = false;
        if (effectiveLastRow >= 1) sheet.Range(1, 1, effectiveLastRow, technicalColumns).SetAutoFilter();
        for (var column = businessColumns + 1; column <= technicalColumns; column++)
        {
            sheet.Column(column).Hide();
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, businessColumns).AdjustToContents();
    }

    private static void Protect(IXLWorksheet sheet) => sheet.Protect(
        XLSheetProtectionElements.SelectLockedCells
        | XLSheetProtectionElements.SelectUnlockedCells
        | XLSheetProtectionElements.AutoFilter
        | XLSheetProtectionElements.Sort
        | XLSheetProtectionElements.InsertRows);

    private static void SetNullableInteger(IXLCell cell, int? value) => cell.Value = value is null ? string.Empty : value.Value;

    private static IEnumerable<CatalogueWorkbookProduct> OrderedProducts(IReadOnlyList<CatalogueWorkbookProduct> products) =>
        products.OrderBy(value => value.Code, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.ProductId);

    private static string ProductKey(Guid id) => $"product:{id:N}";
    private static string GroupKey(Guid id) => $"group:{id:N}";
    private static string OptionKey(Guid id) => $"option:{id:N}";

}
