namespace Sushi81.Pos.Application.Catalogue;

/// <summary>Invariant workbook field descriptors shared by the exporter and future importer.</summary>
public sealed record CatalogueWorkbookFieldDescriptor(
    string Worksheet,
    string FieldKey,
    int ColumnIndex,
    bool BusinessVisible,
    bool Editable,
    string Role,
    string Header);

public static class CatalogueWorkbookSchema
{
    public const string ContractVersion = "M10-CATALOGUE-1";
    public const string MetadataSheetName = "__Sushi81Meta";

    public static IReadOnlyList<CatalogueWorkbookFieldDescriptor> Fields { get; } =
    [
        new("Products", "product_code", 1, true, true, "product-code", "Product Code"),
        new("Products", "product_name", 2, true, true, "product-name", "Product Name"),
        new("Products", "category_name", 3, true, true, "category-name", "Category Name"),
        new("Products", "category_short_code", 4, true, true, "category-short-code", "Category Short Code"),
        new("Products", "price_ttc", 5, true, true, "price-ttc", "Price TTC"),
        new("Products", "vat_rate", 6, true, true, "vat-rate", "VAT Rate"),
        new("Products", "is_active", 7, true, true, "active", "Active"),
        new("Products", "discount_eligible", 8, true, true, "discount-eligible", "Discount Eligible"),
        new("Products", "options_enabled", 9, true, true, "options-enabled", "Options Enabled"),
        new("Products", "product_row_key", 10, false, false, "row-key", "Product Row Key"),
        new("Products", "product_id", 11, false, false, "entity-id", "Product ID"),

        new("OptionGroups", "product_code", 1, true, true, "parent-product-code", "Product Code"),
        new("OptionGroups", "product_name", 2, true, true, "parent-product-name", "Product Name"),
        new("OptionGroups", "group_name", 3, true, true, "group-name", "Group Name"),
        new("OptionGroups", "selection_mode", 4, true, true, "selection-mode", "Selection Mode"),
        new("OptionGroups", "is_required", 5, true, true, "required", "Required"),
        new("OptionGroups", "min_selections", 6, true, true, "minimum-selections", "Min Selections"),
        new("OptionGroups", "max_selections", 7, true, true, "maximum-selections", "Max Selections"),
        new("OptionGroups", "display_order", 8, true, true, "display-order", "Display Order"),
        new("OptionGroups", "product_row_key", 9, false, false, "parent-row-key", "Product Row Key"),
        new("OptionGroups", "option_group_row_key", 10, false, false, "row-key", "OptionGroup Row Key"),
        new("OptionGroups", "product_id", 11, false, false, "parent-entity-id", "Product ID"),
        new("OptionGroups", "option_group_id", 12, false, false, "entity-id", "OptionGroup ID"),

        new("Options", "product_code", 1, true, true, "parent-product-code", "Product Code"),
        new("Options", "product_name", 2, true, true, "parent-product-name", "Product Name"),
        new("Options", "option_group_name", 3, true, true, "parent-group-name", "Option Group Name"),
        new("Options", "option_name", 4, true, true, "option-name", "Option Name"),
        new("Options", "price_adjustment_ttc", 5, true, true, "price-adjustment-ttc", "Price Adjustment TTC"),
        new("Options", "is_active", 6, true, true, "active", "Active"),
        new("Options", "display_order", 7, true, true, "display-order", "Display Order"),
        new("Options", "product_row_key", 8, false, false, "parent-row-key", "Product Row Key"),
        new("Options", "option_group_row_key", 9, false, false, "parent-row-key", "OptionGroup Row Key"),
        new("Options", "option_row_key", 10, false, false, "row-key", "Option Row Key"),
        new("Options", "option_id", 11, false, false, "entity-id", "Option ID"),
        new("Options", "option_group_id", 12, false, false, "parent-entity-id", "OptionGroup ID")
    ];
}
