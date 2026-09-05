using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Domain.Tests;

[TestClass]
public sealed class OrderPricingTests
{
    private static readonly decimal[] DeliveryTaxRates = [10m, 20m];
    private static readonly decimal[] MixedTaxRates = [5.5m, 10m];
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
        Assert.AreEqual(0L, result.Lines.Sum(line => line.DiscountTtc.Cents), "Each 0.005 EUR discounted component rounds to 0.01 EUR; the discount amount is therefore zero cents.");
        Assert.AreEqual(2L, result.TotalTtc.Cents);
    }

    [TestMethod]
    public void C1RoundsTheDiscountedProductComponentBeforeAddingPositiveSurcharge()
    {
        var firstProduct = ProductWithOptions(Guid.NewGuid(), Money.FromCents(1200), 10m, true);
        var secondProduct = ProductWithOptions(Guid.NewGuid(), Money.FromCents(850), 10m, true);
        var first = new OrderLineDraft(Guid.Empty, firstProduct, [], [
            new(null, null, "Sans accompagnement", Money.FromCents(-100)),
            new(null, null, "Sauce premium", Money.FromCents(100))
        ]);
        var second = new OrderLineDraft(Guid.NewGuid(), secondProduct, [], [], 2);
        var result = OrderPricingService.Calculate(
            Draft(first, FulfilmentMode.Retrait) with { Lines = [first, second], PickupDiscountRequested = true },
            Settings(rate: 0.125m, minPickup: Money.Zero));

        Assert.IsTrue(result.IsValid, string.Join(";", result.ValidationErrors));
        Assert.AreEqual(2551L, result.TotalTtc.Cents);
        Assert.AreEqual(1063L, result.Lines[0].CalculatedLineTotalTtc.Cents);
        Assert.AreEqual(1488L, result.Lines[1].CalculatedLineTotalTtc.Cents);
        Assert.AreEqual(137L, result.Lines[0].DiscountTtc.Cents, "11.00 - roundHalfUp(11.00 x 87.5%) = 1.37; discount-amount-first would produce 1.38.");
        Assert.AreEqual(100L, result.Lines[0].PositiveAdjustmentComponentTtc.Cents);
        Assert.AreEqual(2451L, result.TaxBreakdown.Single(tax => tax.VatRate == 10m).TaxableTtc.Cents);
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
    public void OptionSelectionMatrixCoversEveryConfiguredBoundary()
    {
        var requiredSingle = ProductWithGroup(SelectionMode.Single, true, null, null, 2);
        var optionalSingle = ProductWithGroup(SelectionMode.Single, false, null, null, 2);
        var requiredMulti = ProductWithGroup(SelectionMode.Multi, true, 1, 2, 3);
        var optionalMulti = ProductWithGroup(SelectionMode.Multi, false, 0, 2, 3);

        var requiredSingleIds = requiredSingle.OptionsByGroup.Values.Single().Select(option => option.Id).ToArray();
        var optionalSingleIds = optionalSingle.OptionsByGroup.Values.Single().Select(option => option.Id).ToArray();
        var requiredMultiIds = requiredMulti.OptionsByGroup.Values.Single().Select(option => option.Id).ToArray();
        var optionalMultiIds = optionalMulti.OptionsByGroup.Values.Single().Select(option => option.Id).ToArray();

        Assert.IsFalse(Price(requiredSingle, [] ).IsValid);
        Assert.IsTrue(Price(requiredSingle, [requiredSingleIds[0]]).IsValid);
        Assert.IsFalse(Price(requiredSingle, requiredSingleIds).IsValid);
        Assert.IsTrue(Price(optionalSingle, []).IsValid);
        Assert.IsTrue(Price(optionalSingle, [optionalSingleIds[0]]).IsValid);
        Assert.IsFalse(Price(optionalSingle, optionalSingleIds).IsValid);
        Assert.IsFalse(Price(requiredMulti, []).IsValid);
        Assert.IsTrue(Price(requiredMulti, [requiredMultiIds[0]]).IsValid);
        Assert.IsTrue(Price(requiredMulti, requiredMultiIds[..2]).IsValid);
        Assert.IsFalse(Price(requiredMulti, requiredMultiIds).IsValid);
        Assert.IsTrue(Price(optionalMulti, []).IsValid);
        Assert.IsTrue(Price(optionalMulti, optionalMultiIds[..2]).IsValid);
        Assert.IsFalse(Price(optionalMulti, optionalMultiIds).IsValid);
    }

    [TestMethod]
    public void A1MultipliesPositiveAndNegativePredefinedAndCustomAdjustmentsPerUnit()
    {
        var negativeId = Guid.NewGuid();
        var positiveId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var aggregate = new ProductAggregate(
            new Product(Guid.NewGuid(), "P", "Plat", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, true, default, default),
            [new OptionGroup(groupId, Guid.Empty, "Options", SelectionMode.Multi, false, 0, 2, 0, default, default)],
            new Dictionary<Guid, IReadOnlyList<ProductOption>>
            {
                [groupId] = [
                    new ProductOption(negativeId, groupId, "Moins", Money.FromCents(-25), true, 0, default, default),
                    new ProductOption(positiveId, groupId, "Plus", Money.FromCents(40), true, 1, default, default)]
            });

        var result = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, aggregate, [negativeId, positiveId], [
            new(null, null, "Remise", Money.FromCents(-15)),
            new(null, null, "Service", Money.FromCents(20))
        ], 3), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));

        Assert.IsTrue(result.IsValid, string.Join(";", result.ValidationErrors));
        Assert.AreEqual(3000L, result.Lines[0].ExtendedBaseTtc.Cents);
        Assert.AreEqual(60L, result.Lines[0].ExtendedAdjustmentTtc.Cents);
        Assert.AreEqual(3060L, result.TotalTtc.Cents);
        CollectionAssert.AreEqual(new long[] { -25, 40, -15, 20 }, result.Lines[0].Adjustments.Select(adjustment => adjustment.AmountTtcPerUnit.Cents).ToArray());
    }

    [TestMethod]
    public void RetraitDiscountUsesEligibleLinesAndSignedComponentsAtEachThreshold()
    {
        var eligible = ProductWithOptions(Guid.NewGuid(), Money.FromCents(1000), 10m, true);
        var nonEligible = eligible with { Product = eligible.Product with { DiscountEligible = false, Code = "P2" } };
        var line = new OrderLineDraft(Guid.Empty, eligible, [], [new(null, null, "Moins", Money.FromCents(-100)), new(null, null, "Plus", Money.FromCents(200))], 1);
        var other = new OrderLineDraft(Guid.NewGuid(), nonEligible, [], [], 1);
        var draft = Draft(line, FulfilmentMode.Retrait) with { Lines = [line, other], PickupDiscountRequested = true };

        var below = OrderPricingService.Calculate(draft, Settings(rate: 0.1m, minPickup: Money.FromCents(2011)));
        var exact = OrderPricingService.Calculate(draft, Settings(rate: 0.1m, minPickup: Money.FromCents(2010)));
        var above = OrderPricingService.Calculate(draft, Settings(rate: 0.1m, minPickup: Money.FromCents(2009)));

        Assert.IsFalse(below.PickupDiscountApplied);
        Assert.AreEqual(2100L, below.TotalTtc.Cents);
        Assert.IsTrue(exact.PickupDiscountApplied);
        Assert.IsTrue(above.PickupDiscountApplied);
        Assert.AreEqual(90L, exact.Lines[0].DiscountTtc.Cents);
        Assert.AreEqual(200L, exact.Lines[0].PositiveAdjustmentComponentTtc.Cents);
        Assert.AreEqual(2010L, exact.TotalTtc.Cents);
        Assert.AreEqual(0L, exact.Lines[1].DiscountTtc.Cents);
    }

    [TestMethod]
    public void LivraisonBoundariesFeesAndSignedCommercialAmountAreExplicit()
    {
        var product = ProductWithOptions(Guid.NewGuid(), Money.FromCents(2999), 10m, true);
        var positive = new OrderLineAdjustmentDraft(null, null, "Plus", Money.FromCents(1));
        var negative = new OrderLineAdjustmentDraft(null, null, "Moins", Money.FromCents(-1));
        var line = new OrderLineDraft(Guid.Empty, product, [], [positive, negative], 1);
        var under = OrderPricingService.Calculate(Draft(line, FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000) });
        var at = OrderPricingService.Calculate(Draft(line, FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(2999), DeliveryFeeEnabled = true, DeliveryFeeAmountTtc = Money.FromCents(500) });
        var belowBoundary = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000) });
        var atBoundary = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product with { Product = product.Product with { PriceTtc = Money.FromCents(3000) } }, [], [], 1), FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000) });
        var disabledFee = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product with { Product = product.Product with { PriceTtc = Money.FromCents(3000) } }, [], [], 1), FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000), DeliveryFeeEnabled = false, DeliveryFeeAmountTtc = Money.FromCents(500) });
        var zeroFee = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product with { Product = product.Product with { PriceTtc = Money.FromCents(3000) } }, [], [], 1), FulfilmentMode.Livraison), Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000), DeliveryFeeEnabled = true, DeliveryFeeAmountTtc = Money.Zero });

        Assert.IsFalse(under.IsValid, "€29.99 merchandise must fail the €30.00 minimum.");
        Assert.IsTrue(at.IsValid);
        Assert.AreEqual(2999L, at.DeliveryCommercialAmountTtc.Cents);
        Assert.AreEqual(500L, at.DeliveryFeeTtc.Cents);
        Assert.IsFalse(belowBoundary.IsValid);
        Assert.IsTrue(atBoundary.IsValid);
        Assert.IsTrue(disabledFee.IsValid);
        Assert.AreEqual(0L, disabledFee.DeliveryFeeTtc.Cents);
        Assert.IsTrue(zeroFee.IsValid);
        Assert.AreEqual(0L, zeroFee.DeliveryFeeTtc.Cents);
    }

    [TestMethod]
    public void MixedVatAndIncludedVatHalfUpRoundingSurvivePricingVariants()
    {
        var product = ProductWithOptions(Guid.NewGuid(), Money.FromCents(6), 10m, true);
        var positive = new OrderLineAdjustmentDraft(null, null, "Service", Money.FromCents(10));
        var deliverySettings = Settings(minPickup: Money.Zero) with { DeliveryMinMerchandiseTotalTtc = Money.Zero, DeliveryFeeEnabled = true, DeliveryFeeAmountTtc = Money.FromCents(6) };
        var delivery = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [positive], 1), FulfilmentMode.Livraison), deliverySettings);
        var rounding = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Retrait), deliverySettings with { DeliveryFeeEnabled = false });
        var manualAbove = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Retrait) with { ManualTotalOverride = Money.FromCents(5000) }, deliverySettings);
        var manualBelow = OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, [], [], 1), FulfilmentMode.Retrait) with { ManualTotalOverride = Money.FromCents(1) }, deliverySettings);

        Assert.IsTrue(delivery.IsValid);
        CollectionAssert.AreEquivalent(MixedTaxRates, delivery.TaxBreakdown.Select(tax => tax.VatRate).ToArray());
        Assert.AreEqual(1L, rounding.TaxBreakdown.Single(tax => tax.VatRate == 10m && tax.TaxableTtc == Money.FromCents(6)).IncludedVatTtc.Cents, "€0.06 at 10% must round 0.545 cents half-up to €0.01.");
        Assert.AreEqual(5000L, manualAbove.TaxBreakdown.Single().TaxableTtc.Cents);
        Assert.AreEqual(1L, manualBelow.TaxBreakdown.Single().TaxableTtc.Cents);
        Assert.HasCount(1, manualAbove.TaxBreakdown);
        Assert.HasCount(1, manualBelow.TaxBreakdown);
        Assert.AreEqual(10m, manualAbove.TaxBreakdown[0].VatRate);
        Assert.AreEqual(10m, manualBelow.TaxBreakdown[0].VatRate);
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
    public void ExistingSnapshotRepricingUsesCurrentSettingsWithoutReadingCatalogue()
    {
        var item = new OrderItemSnapshot(
            Guid.NewGuid(), 0, Guid.NewGuid(), "P-HIST", "Plat historique", "Plats",
            Money.FromCents(3000), 10m, true, 1, Money.FromCents(3000), Money.FromCents(2700),
            [new(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Sauce", Money.FromCents(100), 5.5m)]);

        var current = OrderPricingService.CalculateSnapshots(
            [item], FulfilmentMode.Retrait, pickupDiscountRequested: true,
            Settings(rate: 0.20m, minPickup: Money.FromCents(2000)) with
            {
                DeliveryFeeEnabled = true,
                DeliveryFeeAmountTtc = Money.FromCents(500)
            });

        Assert.IsTrue(current.IsValid, string.Join(";", current.ValidationErrors));
        Assert.AreEqual(2500L, current.TotalTtc.Cents);
        Assert.AreEqual(0.20m, current.PickupDiscountRate);
        Assert.AreEqual(3000L, current.Items.Single().ExtendedBaseTtc.Cents);
        Assert.AreEqual(2500L, current.Items.Single().CalculatedLineTotalTtc.Cents);
        Assert.AreEqual(5.5m, current.TaxBreakdown.Single(tax => tax.VatRate == 5.5m).VatRate);
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
    private static OrderPricingResult Price(ProductAggregate product, IReadOnlyList<Guid> selected) =>
        OrderPricingService.Calculate(Draft(new OrderLineDraft(Guid.Empty, product, selected, [], 1), FulfilmentMode.Retrait), Settings(minPickup: Money.Zero));
}
