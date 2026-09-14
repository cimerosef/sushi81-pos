using System.Globalization;
using System.Text;

namespace Sushi81.Pos.Domain;

/// <summary>Selection semantics for a product option group.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720", Justification = "The approved selection modes are named Single and Multi.")]
public enum SelectionMode
{
    Single,
    Multi
}

public static class CatalogueNormalization
{
    public static string Display(string? value) => (value ?? string.Empty).Trim();

    public static string Key(string? value) => Display(value).Normalize(NormalizationForm.FormC).ToUpperInvariant();
}

public sealed record Category(
    Guid Id,
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const int MaxShortCodeLength = 12;

    /// <summary>Optional operator-facing navigation code. It is independent from Name.</summary>
    public string? ShortCode { get; init; }

    public string NormalizedName => CatalogueNormalization.Key(Name);
    public string? NormalizedShortCode => string.IsNullOrWhiteSpace(ShortCode) ? null : CatalogueNormalization.Key(ShortCode);

    public static bool TryCreate(Guid id, string? name, DateTimeOffset now, out Category category, out string? error)
    {
        var display = CatalogueNormalization.Display(name);
        if (display.Length == 0)
        {
            category = null!;
            error = "Category name is required.";
            return false;
        }

        category = new Category(id, display, now, now);
        error = null;
        return true;
    }

    public Category Rename(string? name, DateTimeOffset now)
    {
        var display = CatalogueNormalization.Display(name);
        if (display.Length == 0) throw new ArgumentException("Category name is required.", nameof(name));
        return this with { Name = display, UpdatedAt = now };
    }
}

public sealed record Product(
    Guid Id,
    string Code,
    string Name,
    Guid CategoryId,
    Money PriceTtc,
    decimal VatRate,
    bool IsActive,
    bool DiscountEligible,
    bool OptionsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string NormalizedCode => CatalogueNormalization.Key(Code);
}

public sealed record OptionGroup(
    Guid Id,
    Guid ProductId,
    string Name,
    SelectionMode SelectionMode,
    bool IsRequired,
    int? MinSelections,
    int? MaxSelections,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsSingle => SelectionMode == SelectionMode.Single;
}

public sealed record ProductOption(
    Guid Id,
    Guid OptionGroupId,
    string Name,
    Money PriceAdjustmentTtc,
    bool IsActive,
    int DisplayOrder,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ProductAggregate(Product Product, IReadOnlyList<OptionGroup> Groups, IReadOnlyDictionary<Guid, IReadOnlyList<ProductOption>> OptionsByGroup)
{
    public static ProductAggregate Empty(Product product) => new(product, [], new Dictionary<Guid, IReadOnlyList<ProductOption>>());
}

public sealed record BusinessSettings(
    decimal PickupDiscountRate,
    Money PickupDiscountMinTotalTtc,
    Money DeliveryMinMerchandiseTotalTtc,
    bool DeliveryFeeEnabled,
    Money DeliveryFeeAmountTtc,
    DateTimeOffset UpdatedAt)
{
    public ReceiptIdentity ReceiptIdentity { get; init; } = ReceiptIdentity.Default;

    public static BusinessSettings Defaults(DateTimeOffset now) => new(0.10m, Money.FromCents(1500), Money.FromCents(3000), false, Money.Zero, now);
}

/// <summary>Authoritative business identity printed on customer receipts.</summary>
public sealed record ReceiptIdentity(
    string BusinessName,
    string AddressLine1,
    string AddressLine2,
    string Siret,
    string VatNumber,
    string ActivityCode)
{
    public static ReceiptIdentity Default { get; } = new(
        "Sushi 81",
        "12 Rue Gaston Darley",
        "77140 Nemours - FRA",
        "90805211100014",
        "FR03908052111",
        "5610C");

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(BusinessName)
        && !string.IsNullOrWhiteSpace(AddressLine1)
        && !string.IsNullOrWhiteSpace(AddressLine2)
        && !string.IsNullOrWhiteSpace(Siret)
        && !string.IsNullOrWhiteSpace(VatNumber)
        && !string.IsNullOrWhiteSpace(ActivityCode);
}

public static class CatalogueValidation
{
    public static string? ValidateCategoryShortCode(string? shortCode)
    {
        var display = CatalogueNormalization.Display(shortCode);
        return display.Length > Category.MaxShortCodeLength
            ? $"Category short code cannot exceed {Category.MaxShortCodeLength} characters."
            : null;
    }

    public static string? ValidateProduct(string? code, string? name, Guid categoryId, Money price, decimal vatRate)
    {
        if (CatalogueNormalization.Display(code).Length == 0) return "Product code is required.";
        if (CatalogueNormalization.Display(name).Length == 0) return "Product name is required.";
        if (categoryId == Guid.Empty) return "A category is required.";
        if (price < Money.Zero) return "Product price cannot be negative.";
        if (vatRate is < 0m or > 100m) return "VAT rate must be between 0 and 100 percent.";
        return null;
    }

    public static string? ValidateGroup(OptionGroup group)
    {
        if (CatalogueNormalization.Display(group.Name).Length == 0) return "Option group name is required.";
        if (group.DisplayOrder < 0) return "Option group order cannot be negative.";
        if (group.SelectionMode == SelectionMode.Single)
        {
            if (group.MinSelections is not null || group.MaxSelections is not null) return "Single-select groups cannot have minimum or maximum selections.";
            return null;
        }

        if (group.MinSelections is null || group.MaxSelections is null) return "Multi-select groups require minimum and maximum selections.";
        if (group.MinSelections < 0 || group.MaxSelections < 0) return "Selection limits cannot be negative.";
        if (group.MaxSelections < 1 || group.MinSelections > group.MaxSelections) return "Selection limits are invalid.";
        if (group.IsRequired && group.MinSelections < 1) return "A required multi-select group must require at least one choice.";
        return null;
    }

    public static string? ValidateOption(ProductOption option) =>
        CatalogueNormalization.Display(option.Name).Length == 0
            ? "Option name is required."
            : option.DisplayOrder < 0 ? "Option order cannot be negative." : null;

    public static string? ValidateSettings(BusinessSettings settings)
    {
        if (settings.PickupDiscountRate is < 0m or > 1m) return "Pickup discount rate must be between 0 and 100 percent.";
        if (settings.PickupDiscountMinTotalTtc < Money.Zero || settings.DeliveryMinMerchandiseTotalTtc < Money.Zero || settings.DeliveryFeeAmountTtc < Money.Zero)
            return "Settings money values cannot be negative.";
        if (!settings.ReceiptIdentity.IsComplete) return "Customer receipt identity is incomplete.";
        return null;
    }

    public static string? ValidateRequiredChoices(ProductAggregate aggregate)
    {
        if (!aggregate.Product.OptionsEnabled) return null;
        foreach (var group in aggregate.Groups)
        {
            var options = aggregate.OptionsByGroup.TryGetValue(group.Id, out var values) ? values : [];
            var active = options.Count(option => option.IsActive);
            var minimum = group.SelectionMode == SelectionMode.Single ? (group.IsRequired ? 1 : 0) : group.MinSelections ?? 0;
            var maximum = group.SelectionMode == SelectionMode.Single ? (group.IsRequired ? 1 : 1) : group.MaxSelections ?? 0;
            if (active < minimum || active < 1 && maximum > 0 && group.IsRequired)
                return $"Option group '{group.Name}' does not have enough active choices.";
        }
        return null;
    }
}
