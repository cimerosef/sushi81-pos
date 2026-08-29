using System.Globalization;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

/// <summary>Presentation-only helpers for M03 validation and editor state.</summary>
public static class M03Presentation
{
    public static string FormatIssues(OperationResult result, IReadOnlyDictionary<string, string> localized)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(localized);
        return string.Join(Environment.NewLine, result.Issues.Select(issue =>
            $"{FieldLabel(issue.Field, localized)}: {Message(issue, localized)}"));
    }

    public static string Message(ValidationIssue issue, IReadOnlyDictionary<string, string> localized)
    {
        var key = issue.StableCode switch
        {
            ValidationCodes.CategoryDuplicate => "ValidationCategoryDuplicate",
            ValidationCodes.CategoryMissing => "ValidationCategoryMissing",
            ValidationCodes.ProductMissing => "ValidationProductMissing",
            ValidationCodes.ProductDuplicateCode => "ValidationProductDuplicateCode",
            ValidationCodes.PriceNegative => "ValidationPriceNegative",
            ValidationCodes.VatRange => "ValidationVatRange",
            ValidationCodes.RequiredChoices => "ValidationRequiredChoices",
            ValidationCodes.SettingsRange => "ValidationSettingsRange",
            ValidationCodes.InvalidNumber => "ValidationInvalidNumber",
            ValidationCodes.Busy => "ValidationBusy",
            ValidationCodes.GroupStructure => "ValidationGroupStructure",
            ValidationCodes.OptionStructure => "ValidationOptionStructure",
            ValidationCodes.Conflict => "ValidationConflict",
            _ => issue.Field switch
            {
                "code" or "name" or "product" or "groups" or "options" => "ValidationRequired",
                _ => "ValidationGeneric"
            }
        };
        return localized.TryGetValue(key, out var value) ? value : issue.Message;
    }

    public static string FieldLabel(string field, IReadOnlyDictionary<string, string> localized) => field switch
    {
        "code" => localized.GetValueOrDefault("Code", "Code"),
        "name" => localized.GetValueOrDefault("Name", "Name"),
        "category" => localized.GetValueOrDefault("Category", "Category"),
        "price" => localized.GetValueOrDefault("PriceTtc", "TTC price"),
        "vat" => localized.GetValueOrDefault("Vat", "VAT"),
        "groups" => localized.GetValueOrDefault("OptionGroups", "Option groups"),
        "options" => localized.GetValueOrDefault("Options", "Options"),
        "settings" => localized.GetValueOrDefault("Settings", "Settings"),
        "pickup-rate" => localized.GetValueOrDefault("PickupDiscount", "Pickup discount"),
        "pickup-minimum" => localized.GetValueOrDefault("PickupMinimum", "Pickup minimum"),
        "delivery-minimum" => localized.GetValueOrDefault("DeliveryMinimum", "Delivery minimum"),
        "delivery-fee" => localized.GetValueOrDefault("DeliveryFee", "Delivery fee"),
        _ => localized.GetValueOrDefault("ValidationField", "Field")
    };

    public static bool TryParseMoney(string? text, string field, out Money value, out ValidationIssue? issue)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var euros))
        {
            value = Money.Zero;
            issue = new ValidationIssue(field, "Enter a valid monetary amount.", ValidationCodes.InvalidNumber);
            return false;
        }

        try
        {
            value = Money.FromEuros(euros);
            issue = null;
            return true;
        }
        catch (OverflowException)
        {
            value = Money.Zero;
            issue = new ValidationIssue(field, "Enter a valid monetary amount.", ValidationCodes.InvalidNumber);
            return false;
        }
    }

    public static bool TryParseDecimal(string? text, string field, out decimal value, out ValidationIssue? issue)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            issue = new ValidationIssue(field, "Enter a valid number.", ValidationCodes.InvalidNumber);
            return false;
        }

        issue = null;
        return true;
    }

    public static bool TryParseInteger(string? text, string field, out int value, out ValidationIssue? issue)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            issue = new ValidationIssue(field, "Enter a valid whole number.", ValidationCodes.InvalidNumber);
            return false;
        }

        issue = null;
        return true;
    }
}

/// <summary>Pure category edit state so Save/Cancel semantics remain testable without WPF.</summary>
public sealed class CategoryEditBuffer
{
    private bool editing;
    public Guid? CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsEditing => editing;

    public void BeginCreate() { editing = true; CategoryId = null; Name = string.Empty; }
    public void BeginRename(Guid id, string name) { editing = true; CategoryId = id; Name = name; }
    public void SetName(string? name) => Name = name ?? string.Empty;
    public void Cancel() { editing = false; CategoryId = null; Name = string.Empty; }
}

/// <summary>Minimal state machine used by tests and dialogs to prevent silent dirty-edit loss.</summary>
public sealed class ProductEditSession<T>(T original)
{
    public T Original { get; } = original;
    public T Draft { get; private set; } = original;
    public bool IsDirty { get; private set; }
    public bool CanClose => !IsDirty;

    public void SetDraft(T draft) { Draft = draft; IsDirty = true; }
    public void Commit(T saved) { Draft = saved; IsDirty = false; }
    public void Cancel() { Draft = Original; IsDirty = false; }
}
