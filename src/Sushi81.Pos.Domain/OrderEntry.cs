namespace Sushi81.Pos.Domain;

public enum OrderStatus
{
    Open,
    Closed,
    Cancelled
}

public enum FulfilmentMode
{
    Retrait,
    Livraison
}

public enum OrderSourceType
{
    Pos,
    HiboutikPaste
}

public enum OrderAdjustmentKind
{
    PredefinedOption,
    CustomAdjustment
}

public enum PaymentBucket
{
    Card,
    Cash
}

/// <summary>A signed change to one cumulative payment bucket.</summary>
public sealed record PaymentAdjustment(
    Guid Id,
    Guid OrderId,
    PaymentBucket Bucket,
    Money Delta,
    DateTimeOffset EffectiveAt,
    DateTimeOffset RecordedAt)
{
    public DateOnly EffectiveBusinessDate => DateOnly.FromDateTime(EffectiveAt.Date);
}

public sealed record OrderPaymentState(Money Card, Money Cash, Money Total, Money Difference)
{
    public bool IsNonNegative => Card >= Money.Zero && Cash >= Money.Zero;
    public bool IsExactlyReconciled => Difference == Money.Zero;

    public static OrderPaymentState From(OrderSnapshot order) => From(order.TotalTtc, order.CardPaymentTtc, order.CashPaymentTtc);

    public static OrderPaymentState From(Money total, Money card, Money cash) =>
        new(card, cash, card + cash, total - card - cash);
}

public static class OrderReference
{
    public static string Format(DateOnly businessDate, int sequence) =>
        sequence <= 0 ? throw new ArgumentOutOfRangeException(nameof(sequence)) : $"{businessDate:yyyyMMdd}-{sequence:D3}";

    public static bool IsValid(string? reference) =>
        reference is not null && System.Text.RegularExpressions.Regex.IsMatch(reference, "^[0-9]{8}-[0-9]{3,}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

/// <summary>A per-unit adjustment captured while an order is being composed.</summary>
public sealed record OrderLineAdjustmentDraft(
    Guid? OptionId,
    string? GroupName,
    string Label,
    Money AmountTtcPerUnit,
    OrderAdjustmentKind Kind = OrderAdjustmentKind.CustomAdjustment,
    int DisplayOrder = 0);

/// <summary>A mutable-in-the-UI order line represented as an immutable draft.</summary>
public sealed record OrderLineDraft(
    Guid LineId,
    ProductAggregate Product,
    IReadOnlyList<Guid> SelectedOptionIds,
    IReadOnlyList<OrderLineAdjustmentDraft> CustomAdjustments,
    int Quantity = 1,
    string CategoryName = "")
{
    public static OrderLineDraft Create(ProductAggregate product, int quantity = 1) =>
        new(Guid.Empty, product, [], [], quantity);
}

/// <summary>Input to the shared new-order pricing and confirmation workflow.</summary>
public sealed record NewOrderDraft(
    IReadOnlyList<OrderLineDraft> Lines,
    FulfilmentMode? Fulfilment,
    DateOnly? PlannedFulfilmentDate,
    TimeOnly? PlannedFulfilmentTime,
    string? Telephone,
    string? DeliveryAddress,
    string? Comment,
    bool PickupDiscountRequested,
    Money? ManualTotalOverride = null);

public sealed record ResolvedOrderAdjustment(
    Guid? OptionId,
    string? GroupName,
    string Label,
    Money AmountTtcPerUnit,
    decimal? VatRate,
    OrderAdjustmentKind Kind,
    int DisplayOrder);

public sealed record OrderLinePricing(
    OrderLineDraft Draft,
    IReadOnlyList<ResolvedOrderAdjustment> Adjustments,
    Money ExtendedBaseTtc,
    Money ExtendedAdjustmentTtc,
    Money CalculatedLineTotalTtc,
    Money ProductVatComponentTtc,
    Money PositiveAdjustmentComponentTtc,
    Money DiscountTtc);

public sealed record OrderTaxBreakdown(
    decimal VatRate,
    Money TaxableTtc,
    Money IncludedVatTtc,
    Guid Id = default);

public sealed record OrderPricingResult(
    bool IsValid,
    IReadOnlyList<string> ValidationErrors,
    IReadOnlyList<OrderLinePricing> Lines,
    Money TotalBeforeManualOverride,
    Money TotalTtc,
    bool ManualTotalOverrideActive,
    bool PickupDiscountRequested,
    bool PickupDiscountApplied,
    decimal? PickupDiscountRate,
    string? DiscountNotAppliedReason,
    Money DeliveryCommercialAmountTtc,
    Money DeliveryFeeTtc,
    IReadOnlyList<OrderTaxBreakdown> TaxBreakdown)
{
    public static OrderPricingResult Invalid(params string[] errors) => new(
        false, errors, [], Money.Zero, Money.Zero, false, false, false, null, null, Money.Zero, Money.Zero, []);
}

/// <summary>Immutable sale-time line snapshot. It has no current-catalogue authority.</summary>
public sealed record OrderItemSnapshot(
    Guid Id,
    int Position,
    Guid? SourceProductId,
    string ProductCode,
    string ProductName,
    string CategoryName,
    Money ProductBasePriceTtc,
    decimal ProductVatRate,
    bool ProductDiscountEligible,
    int Quantity,
    Money ExtendedBaseTtc,
    Money CalculatedLineTotalTtc,
    IReadOnlyList<OrderLineAdjustmentSnapshot> Adjustments);

public sealed record OrderLineAdjustmentSnapshot(
    Guid Id,
    int DisplayOrder,
    OrderAdjustmentKind Kind,
    Guid? SourceOptionId,
    string? GroupName,
    string Label,
    Money AdjustmentTtcPerUnit,
    decimal? VatRate);

/// <summary>Complete committed order representation reloaded from persisted snapshots.</summary>
public sealed record OrderSnapshot(
    Guid Id,
    OrderSourceType SourceType,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset? CancelledAt,
    FulfilmentMode Fulfilment,
    DateOnly PlannedFulfilmentDate,
    TimeOnly? PlannedFulfilmentTime,
    bool AdvanceOrderMarker,
    string? Telephone,
    string? DeliveryAddress,
    string? Comment,
    Money TotalTtc,
    bool ManualTotalOverrideActive,
    bool PickupDiscountApplied,
    decimal? PickupDiscountRate,
    Money DeliveryFeeTtc,
    IReadOnlyList<OrderItemSnapshot> Items,
    IReadOnlyList<OrderTaxBreakdown> TaxBreakdown)
{
    /// <summary>Immutable operator-facing reference allocated by SQLite at creation.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Current cumulative card amount. It is kept separately from signed adjustments for fast detail reads.</summary>
    public Money CardPaymentTtc { get; init; } = Money.Zero;

    /// <summary>Current cumulative cash amount. It is kept separately from signed adjustments for fast detail reads.</summary>
    public Money CashPaymentTtc { get; init; } = Money.Zero;

    public Money CbPaymentTtc { get => CardPaymentTtc; init => CardPaymentTtc = value; }
    public Money EspecePaymentTtc { get => CashPaymentTtc; init => CashPaymentTtc = value; }
}

/// <summary>Shared operator-facing representation for persisted planned times.</summary>
public static class OrderTimeFormatting
{
    public static string Format(TimeOnly time) => time.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
    public static string Format(TimeOnly? time) => time is { } value ? Format(value) : string.Empty;
}

public static class TelephoneNormalization
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        var digits = new string(trimmed.Where(char.IsDigit).ToArray());
        var hasOnlyPhoneSeparators = trimmed.All(character => char.IsDigit(character) || char.IsWhiteSpace(character) || character is '-' or '.' or '(' or ')');
        if (hasOnlyPhoneSeparators && digits.Length == 10 && digits[0] == '0')
        {
            return string.Join(" ", Enumerable.Range(0, 5).Select(index => digits.Substring(index * 2, 2)));
        }

        return trimmed;
    }
}

/// <summary>Pure M04 order pricing. All amounts are integer cents and rates are decimal.</summary>
public static class OrderPricingService
{
    public const decimal PositiveAdjustmentVatRate = 5.5m;
    public const decimal DeliveryFeeVatRate = 10m;

    public static OrderPricingResult Calculate(NewOrderDraft draft, BusinessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(settings);

        var errors = new List<string>();
        if (draft.Fulfilment is null) errors.Add("A fulfilment mode is required.");
        if (draft.PlannedFulfilmentDate is null) errors.Add("A planned fulfilment date is required.");
        if (draft.Lines is null || draft.Lines.Count == 0) errors.Add("At least one order line is required.");
        if (CatalogueValidation.ValidateSettings(settings) is { } settingsError) errors.Add(settingsError);

        var lines = new List<OrderLinePricing>();
        foreach (var line in draft.Lines ?? [])
        {
            if (line is null)
            {
                errors.Add("An order line is invalid.");
                continue;
            }

            if (line.Quantity <= 0) errors.Add($"Quantity for '{line.Product.Product.Name}' must be positive.");
            if (!line.Product.Product.IsActive) errors.Add($"Product '{line.Product.Product.Name}' is no longer active.");
            var resolved = ResolveAdjustments(line, errors);
            var baseTtc = line.Product.Product.PriceTtc * line.Quantity;
            var adjustmentTtc = resolved.Aggregate(Money.Zero, (total, adjustment) => total + adjustment.AmountTtcPerUnit * line.Quantity);
            var productComponent = baseTtc + resolved.Where(adjustment => adjustment.AmountTtcPerUnit < Money.Zero).Aggregate(Money.Zero, (total, adjustment) => total + adjustment.AmountTtcPerUnit * line.Quantity);
            var positiveComponent = resolved.Where(adjustment => adjustment.AmountTtcPerUnit > Money.Zero).Aggregate(Money.Zero, (total, adjustment) => total + adjustment.AmountTtcPerUnit * line.Quantity);
            lines.Add(new(line, resolved, baseTtc, adjustmentTtc, productComponent + positiveComponent, productComponent, positiveComponent, Money.Zero));
        }

        if (errors.Count > 0)
        {
            return new(false, errors, lines, Money.Zero, Money.Zero, false, draft.PickupDiscountRequested, false, null, null, Money.Zero, Money.Zero, []);
        }

        var normalTotal = lines.Aggregate(Money.Zero, (total, line) => total + line.CalculatedLineTotalTtc);
        var commercialAmount = normalTotal;
        var pickupApplied = false;
        decimal? pickupRate = null;
        string? discountReason = null;
        var deliveryFee = Money.Zero;

        if (draft.Fulfilment == FulfilmentMode.Retrait && draft.PickupDiscountRequested)
        {
            var eligibleLines = lines.Where(line => line.Draft.Product.Product.DiscountEligible).ToArray();
            if (eligibleLines.Length == 0)
            {
                discountReason = "No eligible product line is present.";
            }
            else
            {
                var candidateLines = lines.Select(line =>
                {
                    if (!line.Draft.Product.Product.DiscountEligible) return line;
                    var discount = BusinessRounding.ToCents(line.ProductVatComponentTtc.Euros * settings.PickupDiscountRate);
                    return line with { DiscountTtc = Money.FromCents(discount), CalculatedLineTotalTtc = line.CalculatedLineTotalTtc - Money.FromCents(discount) };
                }).ToArray();
                var candidateTotal = candidateLines.Aggregate(Money.Zero, (total, line) => total + line.CalculatedLineTotalTtc);
                if (candidateTotal < settings.PickupDiscountMinTotalTtc)
                {
                    discountReason = "The discounted total is below the pickup minimum.";
                }
                else
                {
                    lines = candidateLines.ToList();
                    normalTotal = candidateTotal;
                    pickupApplied = settings.PickupDiscountRate != 0m;
                    pickupRate = pickupApplied ? settings.PickupDiscountRate : null;
                    if (!pickupApplied) discountReason = "The configured pickup discount rate is zero.";
                }
            }
        }

        if (draft.Fulfilment == FulfilmentMode.Livraison)
        {
            if (commercialAmount < settings.DeliveryMinMerchandiseTotalTtc)
            {
                errors.Add("The delivery merchandise total is below the configured minimum.");
            }
            else if (settings.DeliveryFeeEnabled)
            {
                deliveryFee = settings.DeliveryFeeAmountTtc;
            }
        }

        var calculatedTotal = normalTotal + deliveryFee;
        var total = draft.ManualTotalOverride ?? calculatedTotal;
        var manual = draft.ManualTotalOverride is not null;
        var taxes = manual
            ? [new OrderTaxBreakdown(DeliveryFeeVatRate, total, IncludedVat(total, DeliveryFeeVatRate))]
            : BuildTaxes(lines, deliveryFee);

        if (errors.Count > 0)
        {
            return new(false, errors, lines, calculatedTotal, total, manual, draft.PickupDiscountRequested, pickupApplied, pickupRate, discountReason, commercialAmount, deliveryFee, taxes);
        }

        return new(true, [], lines, calculatedTotal, total, manual, draft.PickupDiscountRequested, pickupApplied, pickupRate, discountReason, commercialAmount, deliveryFee, taxes);
    }

    private static List<ResolvedOrderAdjustment> ResolveAdjustments(OrderLineDraft line, List<string> errors)
    {
        var result = new List<ResolvedOrderAdjustment>();
        var selectedOptionIds = (line.SelectedOptionIds ?? []).ToArray();
        var selected = selectedOptionIds.ToHashSet();
        if (selected.Count != selectedOptionIds.Length) errors.Add($"Product '{line.Product.Product.Name}' contains duplicate option selections.");
        if (!line.Product.Product.OptionsEnabled && selected.Count > 0) errors.Add($"Product '{line.Product.Product.Name}' does not accept options.");

        var knownOptions = new Dictionary<Guid, (OptionGroup Group, ProductOption Option)>();
        if (line.Product.Product.OptionsEnabled)
        {
            foreach (var group in line.Product.Groups.OrderBy(group => group.DisplayOrder))
            {
                var options = line.Product.OptionsByGroup.TryGetValue(group.Id, out var values) ? values : [];
                foreach (var option in options)
                {
                    if (!knownOptions.TryAdd(option.Id, (group, option))) errors.Add($"Option '{option.Name}' has a duplicate identity in the product.");
                }
            }

            foreach (var selectedId in selected)
            {
                if (!knownOptions.ContainsKey(selectedId)) errors.Add("An option selection is no longer available.");
            }

            foreach (var group in line.Product.Groups.OrderBy(group => group.DisplayOrder))
            {
                var options = line.Product.OptionsByGroup.TryGetValue(group.Id, out var values) ? values : [];
                var groupSelected = selected.Where(id => knownOptions.TryGetValue(id, out var value) && value.Group.Id == group.Id).ToArray();
                var activeSelected = groupSelected.Where(id => knownOptions[id].Option.IsActive).ToArray();
                if (groupSelected.Length != activeSelected.Length) errors.Add($"Option group '{group.Name}' contains an inactive option.");
                var min = group.SelectionMode == SelectionMode.Single ? (group.IsRequired ? 1 : 0) : group.MinSelections ?? 0;
                var max = group.SelectionMode == SelectionMode.Single ? 1 : group.MaxSelections ?? 0;
                if (groupSelected.Length < min || groupSelected.Length > max || (group.IsRequired && groupSelected.Length == 0))
                    errors.Add($"Option group '{group.Name}' has an invalid selection.");
                if (options.Count(option => option.IsActive) < min) errors.Add($"Option group '{group.Name}' does not have enough active choices.");
            }
        }

        var orderedSelectedIds = line.Product.Groups
            .OrderBy(group => group.DisplayOrder)
            .SelectMany(group => line.Product.OptionsByGroup.TryGetValue(group.Id, out var options)
                ? options.OrderBy(option => option.DisplayOrder).Select(option => option.Id)
                : [])
            .Where(selected.Contains)
            .ToArray();
        foreach (var id in orderedSelectedIds)
        {
            if (!knownOptions.TryGetValue(id, out var selectedOption) || !selectedOption.Option.IsActive) continue;

            var option = selectedOption.Option;
            result.Add(new(id, selectedOption.Group.Name, option.Name, option.PriceAdjustmentTtc, AdjustmentVat(option.PriceAdjustmentTtc, line.Product.Product.VatRate), OrderAdjustmentKind.PredefinedOption, result.Count));
        }

        foreach (var custom in line.CustomAdjustments ?? [])
        {
            var label = (custom.Label ?? string.Empty).Trim();
            if (label.Length == 0) errors.Add("A custom adjustment label is required.");
            result.Add(new(custom.OptionId, string.IsNullOrWhiteSpace(custom.GroupName) ? null : custom.GroupName.Trim(), label, custom.AmountTtcPerUnit, AdjustmentVat(custom.AmountTtcPerUnit, line.Product.Product.VatRate), OrderAdjustmentKind.CustomAdjustment, result.Count));
        }

        return result;
    }

    private static decimal? AdjustmentVat(Money amount, decimal productRate) => amount.Cents switch
    {
        > 0 => PositiveAdjustmentVatRate,
        < 0 => productRate,
        _ => null
    };

    private static OrderTaxBreakdown[] BuildTaxes(IReadOnlyList<OrderLinePricing> lines, Money deliveryFee)
    {
        var buckets = new Dictionary<decimal, Money>();
        foreach (var line in lines)
        {
            Add(buckets, line.Draft.Product.Product.VatRate, line.ProductVatComponentTtc - line.DiscountTtc);
            Add(buckets, PositiveAdjustmentVatRate, line.PositiveAdjustmentComponentTtc);
        }
        Add(buckets, DeliveryFeeVatRate, deliveryFee);
        return buckets.Where(pair => pair.Value != Money.Zero)
            .OrderBy(pair => pair.Key)
            .Select(pair => new OrderTaxBreakdown(pair.Key, pair.Value, IncludedVat(pair.Value, pair.Key)))
            .ToArray();
    }

    private static void Add(Dictionary<decimal, Money> buckets, decimal rate, Money value)
    {
        if (value == Money.Zero) return;
        buckets[rate] = buckets.TryGetValue(rate, out var current) ? current + value : value;
    }

    private static Money IncludedVat(Money taxable, decimal rate) => rate == 0m
        ? Money.Zero
        : Money.FromCents(BusinessRounding.ToCents(taxable.Euros * rate / (100m + rate)));
}
