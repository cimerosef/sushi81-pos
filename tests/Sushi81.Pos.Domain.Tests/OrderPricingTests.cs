using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Domain.Tests;

[TestClass]
public sealed class OrderPricingTests
{
    private static readonly decimal[] DeliveryTaxRates = [10m, 20m];
    [TestMethod]
    public void A1QuantityMultipliesBasePredefinedAndCustomAdjustments()
    {
        var optionId = Guid.NewGuid();
        var product = ProductWithOptions(optionId, Money.FromCents(1000), 10m, true);
        var line = new OrderLineDraft(Guid.Empty, product, [optionId], [new(null, null, "Préparation", Money.FromCents(50))], 2, "Plats");
        var result = OrderPricingService.Calculate(Draft(line, FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));

        Assert.IsTrue(result.IsValid, string.Join(";", result.ValidationErrors));
        Assert.AreEqual(2300L, result.TotalTtc.Cents);
        Assert.AreEqual(2000L, result.Lines[0].ExtendedBaseTtc.Cents);
        Assert.AreEqual(300L, result.Lines[0].ExtendedAdjustmentTtc.Cents);
        Assert.AreEqual(50L, result.Lines[0].Adjustments[1].AmountTtcPerUnit.Cents);
    }

    [TestMethod]
    public void B1RetraitDiscountStartsOffAndC1RoundsPerLine()
    {
        var product = ProductWithOptions(Guid.NewGuid(), Money.FromCents(1), 10m, true);
        var off = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));
        Assert.IsFalse(off.PickupDiscountApplied);
        Assert.AreEqual(1L, off.TotalTtc.Cents);

        var oneLine = Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Retrait) with { PickupDiscountRequested = true };
        var result = OrderPricingService.Calculate(oneLine with { Lines = [oneLine.Lines[0], oneLine.Lines[0] with { LineId = Guid.NewGuid() }] }, Settings(rate: 0.5m, minPickup: Money.Zero));
        Assert.IsTrue(result.PickupDiscountApplied);
        Assert.AreEqual(2L, result.Lines.Sum(line => line.DiscountTtc.Cents));
        Assert.AreEqual(0L, result.TotalTtc.Cents);
    }

    [TestMethod]
    public void InactiveSelectedOptionAndMalformedSelectionCannotBePriced()
    {
        var inactiveId = Guid.NewGuid();
        var product = ProductWithOptions(inactiveId, Money.FromCents(1000), 10m, false);
        var result = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [inactiveId], [], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));
        Assert.IsFalse(result.IsValid);
        StringAssert.Contains(string.Join(";", result.ValidationErrors), "inactive");
    }

    [TestMethod]
    public void SignBasedVatAndTelephoneNormalizationAreDeterministic()
    {
        var negativeId = Guid.NewGuid();
        var positiveId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var aggregate = new ProductAggregate(
            new Product(Guid.NewGuid(), "P", "Plat", Guid.NewGuid(), Money.FromCents(1000), 10m, true, false, true, default, default),
            [new OptionGroup(groupId, Guid.Empty, "Options", SelectionMode.Multi, false, 0, 3, 0, default, default)],
            new Dictionary<Guid, IReadOnlyList<ProductOption>>
            {
                [groupId] = [new ProductOption(negativeId, groupId, "Réduction", Money.FromCents(-100), true, 0, default, default), new ProductOption(positiveId, groupId, "Supplément", Money.FromCents(200), true, 1, default, default)]
            });
        var result = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, aggregate, [negativeId, positiveId], [], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));
        Assert.IsTrue(result.IsValid);
        Assert.AreEqual(10m, result.TaxBreakdown.Single(tax => tax.VatRate == 10m).VatRate);
        Assert.AreEqual(5.5m, result.TaxBreakdown.Single(tax => tax.VatRate == 5.5m).VatRate);
        Assert.AreEqual("06 12 34 56 78", TelephoneNormalization.Normalize("0612345678"));
        Assert.AreEqual("contact 0612345678", TelephoneNormalization.Normalize("contact 0612345678"));
    }

    [TestMethod]
    public void LivraisonMinimumExcludesFeeAndManualTotalUsesOneTenPercentBucket()
    {
        var product = ProductWithOptions(Guid.NewGuid(), Money.FromCents(3000), 10m, true);
        var settings = Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000), DeliveryFeeEnabled = true, DeliveryFeeAmountTtc = Money.FromCents(1000) };
        var delivery = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Livraison), settings);
        Assert.IsTrue(delivery.IsValid);
        Assert.AreEqual(3000L, delivery.DeliveryCommercialAmountTtc.Cents);
        Assert.AreEqual(1000L, delivery.DeliveryFeeTtc.Cents);
        Assert.AreEqual(4000L, delivery.TotalTtc.Cents);

        var manual = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Retrait) with { ManualTotalOverride = Money.FromCents(1234) }, settings);
        Assert.IsTrue(manual.IsValid);
        Assert.IsTrue(manual.ManualTotalOverrideActive);
        Assert.HasCount(1, manual.TaxBreakdown);
        Assert.AreEqual(10m, manual.TaxBreakdown[0].VatRate);
        Assert.AreEqual(1234L, manual.TaxBreakdown[0].TaxableTtc.Cents);
    }

    [TestMethod]
    public void UnknownDeletedAndInactiveSelectionsNeverResolveSilently()
    {
        var activeId = Guid.NewGuid();
        var product = ProductWithOptions(activeId, Money.FromCents(1000), 10m, true);
        var unknown = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [Guid.NewGuid()], [], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));
        var deleted = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [activeId, Guid.NewGuid()], [], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));
        var inactiveId = Guid.NewGuid();
        var inactiveProduct = ProductWithOptions(inactiveId, Money.FromCents(1000), 10m, false);
        var inactive = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, inactiveProduct, [inactiveId], [], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));

        Assert.IsFalse(unknown.IsValid);
        Assert.IsFalse(deleted.IsValid);
        Assert.IsFalse(inactive.IsValid);
        StringAssert.Contains(string.Join(";", unknown.ValidationErrors), "no longer available");
        StringAssert.Contains(string.Join(";", deleted.ValidationErrors), "no longer available");
        StringAssert.Contains(string.Join(";", inactive.ValidationErrors), "inactive");
        Assert.IsEmpty(unknown.Lines[0].Adjustments);
    }

    [TestMethod]
    public void OptionBoundsRespectRequiredAndOptionalSingleAndMultiGroups()
    {
        var requiredSingle = ProductWithGroup(SelectionMode.Single, true, null, null, 2);
        var optionalSingle = ProductWithGroup(SelectionMode.Single, false, null, null, 2);
        var requiredMulti = ProductWithGroup(SelectionMode.Multi, true, 1, 2, 3);
        var optionalMulti = ProductWithGroup(SelectionMode.Multi, false, 0, 2, 3);

        Assert.IsFalse(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, requiredSingle, [], [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
        Assert.IsFalse(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, requiredSingle, [requiredSingle.OptionsByGroup.Values.Single()[0].Id, requiredSingle.OptionsByGroup.Values.Single()[1].Id], [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
        Assert.IsTrue(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, optionalSingle, [], [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
        Assert.IsFalse(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, optionalSingle, optionalSingle.OptionsByGroup.Values.Single().Select(option => option.Id).ToArray(), [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
        Assert.IsFalse(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, requiredMulti, [], [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
        Assert.IsTrue(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, requiredMulti, [requiredMulti.OptionsByGroup.Values.Single()[0].Id], [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
        Assert.IsFalse(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, requiredMulti, requiredMulti.OptionsByGroup.Values.Single().Select(option => option.Id).ToArray(), [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
        Assert.IsTrue(OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, optionalMulti, [], [], 1), FulfilmentMode.Retrait), Settings()).IsValid);
    }

    [TestMethod]
    public void DisabledOptionsIgnoreDormantGroupsButKeepCustomAdjustments()
    {
        var groupId = Guid.NewGuid();
        var product = new ProductAggregate(
            new Product(Guid.NewGuid(), "P", "Plat", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, false, default, default),
            [new OptionGroup(groupId, Guid.Empty, "Dormant", SelectionMode.Single, true, null, null, 0, default, default)],
            new Dictionary<Guid, IReadOnlyList<ProductOption>> { [groupId] = [] });
        var result = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [new(null, null, "Sauce", Money.FromCents(25))], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));

        Assert.IsTrue(result.IsValid, string.Join(";", result.ValidationErrors));
        Assert.AreEqual(1025L, result.TotalTtc.Cents);
    }

    [TestMethod]
    public void CustomAdjustmentsValidateBlankAndPreservePositiveNegativeAndZeroAmounts()
    {
        var product = ProductWithOptions(Guid.NewGuid(), Money.FromCents(1000), 10m, true);
        var valid = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [
            new(null, null, "Plus", Money.FromCents(25)),
            new(null, null, "Moins", Money.FromCents(-10)),
            new(null, null, "Neutre", Money.Zero)
        ], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));
        var invalid = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [new(null, null, " ", Money.FromCents(1))], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));

        Assert.IsTrue(valid.IsValid, string.Join(";", valid.ValidationErrors));
        Assert.AreEqual(1015L, valid.TotalTtc.Cents);
        Assert.IsFalse(invalid.IsValid);
        StringAssert.Contains(string.Join(";", invalid.ValidationErrors), "label is required");
    }

    [TestMethod]
    public void PickupAndDeliveryThresholdsUseAuthoritativeCurrentSettings()
    {
        var eligible = ProductWithOptions(Guid.NewGuid(), Money.FromCents(3000), 10m, true);
        var pickup = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, eligible, [], [], 1), FulfilmentMode.Retrait) with { PickupDiscountRequested = true }, Settings(rate: 0.1m, minPickup: Money.FromCents(2700)));
        Assert.IsTrue(pickup.PickupDiscountApplied);
        Assert.AreEqual(2700L, pickup.TotalTtc.Cents);

        var below = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, eligible, [], [], 1), FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3001) });
        var at = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, eligible, [], [], 1), FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000), DeliveryFeeEnabled = true, DeliveryFeeAmountTtc = Money.FromCents(299) });

        Assert.IsFalse(below.IsValid);
        Assert.IsTrue(at.IsValid);
        Assert.AreEqual(299L, at.DeliveryFeeTtc.Cents);
        Assert.AreEqual(3299L, at.TotalTtc.Cents);
    }

    [TestMethod]
    public void OptionsDisabledAndDeliveryFeeDoNotCreateUnexpectedTaxBuckets()
    {
        var product = ProductWithOptions(Guid.NewGuid(), Money.FromCents(2999), 20m, true);
        var settings = Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(2999), DeliveryFeeEnabled = true, DeliveryFeeAmountTtc = Money.FromCents(100) };
        var result = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Livraison), settings);

        Assert.IsTrue(result.IsValid);
        CollectionAssert.AreEquivalent(DeliveryTaxRates, result.TaxBreakdown.Select(tax => tax.VatRate).ToArray());
        Assert.AreEqual(9L, result.TaxBreakdown.Single(tax => tax.VatRate == 10m).IncludedVatTtc.Cents);
    }

    private static ProductAggregate ProductWithOptions(Guid optionId, Money price, decimal vat, bool activeOption)
    {
        var groupId = Guid.NewGuid();
        return new ProductAggregate(
            new Product(Guid.NewGuid(), "P", "Plat", Guid.NewGuid(), price, vat, true, true, true, default, default),
            [new OptionGroup(groupId, Guid.Empty, "Options", SelectionMode.Single, false, null, null, 0, default, default)],
            new Dictionary<Guid, IReadOnlyList<ProductOption>>
            {
                [groupId] = [new ProductOption(optionId, groupId, "Option", Money.FromCents(100), activeOption, 0, default, default)]
            });
    }

    private static ProductAggregate ProductWithGroup(SelectionMode mode, bool required, int? min, int? max, int optionCount)
    {
        var groupId = Guid.NewGuid();
        var options = Enumerable.Range(0, optionCount)
            .Select(index => new ProductOption(Guid.NewGuid(), groupId, $"Option {index}", Money.Zero, true, index, default, default))
            .ToArray();
        return new ProductAggregate(
            new Product(Guid.NewGuid(), "P", "Plat", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, true, default, default),
            [new OptionGroup(groupId, Guid.Empty, "Options", mode, required, min, max, 0, default, default)],
            new Dictionary<Guid, IReadOnlyList<ProductOption>> { [groupId] = options });
    }

    private static NewOrderDraft Draft(OrderLineDraft line, FulfilmentMode mode) => new([line], mode, new DateOnly(2026, 8, 31), null, null, null, null, false);
    private static BusinessSettings Settings(decimal rate = 0.10m, Money? minPickup = null) => new(rate, minPickup ?? Money.FromCents(1500), Money.FromCents(3000), false, Money.Zero, default);
}
