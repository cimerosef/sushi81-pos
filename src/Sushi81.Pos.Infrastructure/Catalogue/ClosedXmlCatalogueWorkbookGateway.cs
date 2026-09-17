using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Sushi81.Pos.Application.Catalogue;

namespace Sushi81.Pos.Infrastructure.Catalogue;

/// <summary>
/// ClosedXML-only workbook writer. The Application layer sees only
/// <see cref="ICatalogueWorkbookGateway"/> and its DTOs.
/// </summary>
public sealed class ClosedXmlCatalogueWorkbookGateway : ICatalogueWorkbookGateway
{
    private const string MetadataSheetName = "__Sushi81Meta";
    private const string ContractVersion = "M10-CATALOGUE-1";

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
        var headers = new[]
        {
            "Product Code", "Product Name", "Category Name", "Category Short Code", "Price TTC",
            "VAT Rate", "Active", "Discount Eligible", "Options Enabled", "Product Row Key", "Product ID"
        };
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
        sheet.Column(6).Style.NumberFormat.Format = "0.##";
        UnlockBusinessCells(sheet, row - 1, 9);
        FinishVisibleSheet(sheet, row - 1, 9, 11);
    }

    private static void WriteGroups(IXLWorksheet sheet, IReadOnlyList<CatalogueWorkbookProduct> products)
    {
        var headers = new[]
        {
            "Product Code", "Product Name", "Group Name", "Selection Mode", "Required",
            "Min Selections", "Max Selections", "Display Order", "Product Row Key", "OptionGroup Row Key", "Product ID", "OptionGroup ID"
        };
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

        UnlockBusinessCells(sheet, row - 1, 8);
        FinishVisibleSheet(sheet, row - 1, 8, 12);
    }

    private static void WriteOptions(IXLWorksheet sheet, IReadOnlyList<CatalogueWorkbookProduct> products)
    {
        var headers = new[]
        {
            "Product Code", "Product Name", "Option Group Name", "Option Name", "Price Adjustment TTC",
            "Active", "Display Order", "Product Row Key", "OptionGroup Row Key", "Option Row Key", "Option ID", "OptionGroup ID"
        };
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
        UnlockBusinessCells(sheet, row - 1, 7);
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
        sheet.Cell(6, 1).Value = "EntityType";
        sheet.Cell(6, 2).Value = "RowKey";
        sheet.Cell(6, 3).Value = "EntityId";
        sheet.Cell(6, 4).Value = "ParentRowKey";
        sheet.Cell(6, 5).Value = "Worksheet";
        sheet.Cell(6, 6).Value = "RowNumber";
        sheet.Cell(6, 7).Value = "BaselineFingerprint";

        var row = 7;
        var productWorksheetRow = 2;
        var groupWorksheetRow = 2;
        var optionWorksheetRow = 2;
        foreach (var product in OrderedProducts(export.Products))
        {
            AddManifest(sheet, ref row, "Product", ProductKey(product.ProductId), product.ProductId, null, "Products", productWorksheetRow++, ProductFingerprint(product));
            foreach (var group in product.OptionGroups.OrderBy(value => value.DisplayOrder).ThenBy(value => value.OptionGroupId))
            {
                AddManifest(sheet, ref row, "OptionGroup", GroupKey(group.OptionGroupId), group.OptionGroupId, ProductKey(product.ProductId), "OptionGroups", groupWorksheetRow++, GroupFingerprint(group));
                foreach (var option in group.Options.OrderBy(value => value.DisplayOrder).ThenBy(value => value.OptionId))
                    AddManifest(sheet, ref row, "Option", OptionKey(option.OptionId), option.OptionId, GroupKey(group.OptionGroupId), "Options", optionWorksheetRow++, OptionFingerprint(option));
            }
        }

        sheet.Columns().AdjustToContents();
    }

    private static void AddManifest(IXLWorksheet sheet, ref int row, string entityType, string key, Guid entityId, string? parentKey, string worksheet, int rowNumber, string fingerprint)
    {
        sheet.Cell(row, 1).Value = entityType;
        sheet.Cell(row, 2).Value = key;
        sheet.Cell(row, 3).Value = entityId.ToString("D");
        sheet.Cell(row, 4).Value = parentKey ?? string.Empty;
        sheet.Cell(row, 5).Value = worksheet;
        sheet.Cell(row, 6).Value = rowNumber;
        sheet.Cell(row, 7).Value = fingerprint;
        row++;
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
    }

    private static void UnlockBusinessCells(IXLWorksheet sheet, int lastRow, int businessColumns)
    {
        if (lastRow >= 2) sheet.Range(2, 1, lastRow, businessColumns).Style.Protection.Locked = false;
    }

    private static void FinishVisibleSheet(IXLWorksheet sheet, int lastRow, int businessColumns, int technicalColumns)
    {
        if (lastRow >= 1) sheet.Range(1, 1, Math.Max(1, lastRow), businessColumns).SetAutoFilter();
        for (var column = businessColumns + 1; column <= technicalColumns; column++)
        {
            sheet.Column(column).Hide();
            sheet.Column(column).Style.Protection.Locked = true;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns(1, businessColumns).AdjustToContents();
    }

    private static void Protect(IXLWorksheet sheet) => sheet.Protect(
        XLSheetProtectionElements.SelectLockedCells
        | XLSheetProtectionElements.SelectUnlockedCells
        | XLSheetProtectionElements.AutoFilter
        | XLSheetProtectionElements.Sort);

    private static void SetNullableInteger(IXLCell cell, int? value) => cell.Value = value is null ? string.Empty : value.Value;

    private static IEnumerable<CatalogueWorkbookProduct> OrderedProducts(IReadOnlyList<CatalogueWorkbookProduct> products) =>
        products.OrderBy(value => value.Code, StringComparer.OrdinalIgnoreCase).ThenBy(value => value.ProductId);

    private static string ProductKey(Guid id) => $"product:{id:N}";
    private static string GroupKey(Guid id) => $"group:{id:N}";
    private static string OptionKey(Guid id) => $"option:{id:N}";

    private static string ProductFingerprint(CatalogueWorkbookProduct product) => Fingerprint(string.Join("|", product.Code, product.Name, product.CategoryName, product.CategoryShortCode, product.PriceTtc.Cents.ToString(CultureInfo.InvariantCulture), product.VatRate.ToString(CultureInfo.InvariantCulture), product.IsActive, product.DiscountEligible, product.OptionsEnabled));
    private static string GroupFingerprint(CatalogueWorkbookOptionGroup group) => Fingerprint(string.Join("|", group.Name, group.SelectionMode, group.IsRequired, group.MinSelections, group.MaxSelections, group.DisplayOrder));
    private static string OptionFingerprint(CatalogueWorkbookOption option) => Fingerprint(string.Join("|", option.Name, option.PriceAdjustmentTtc.Cents.ToString(CultureInfo.InvariantCulture), option.IsActive, option.DisplayOrder));
    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
