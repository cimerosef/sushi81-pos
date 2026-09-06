using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Domain.Tests;

[TestClass]
public sealed class ExistingOrderSnapshotPricingTests
{
    [TestMethod]
    public void ExistingSnapshotsApplyPickupDiscountOnlyToHistoricallyEligibleLines()
    {
        var scenarios = new[]
        {
            (FirstEligible: true, SecondEligible: true, ExpectedCents: 2610L),
            (FirstEligible: false, SecondEligible: true, ExpectedCents: 2730L),
            (FirstEligible: true, SecondEligible: false, ExpectedCents: 2780L),
            (FirstEligible: false, SecondEligible: false, ExpectedCents: 2900L)
        };

        foreach (var scenario in scenarios)
        {
            var priced = OrderPricingService.CalculateSnapshots(
                [Item("TST002", 1200, scenario.FirstEligible), Item("TST001A", 850, scenario.SecondEligible, quantity: 2)],
                FulfilmentMode.Retrait,
                pickupDiscountRequested: true,
                Settings());

            Assert.IsTrue(priced.IsValid, string.Join(";", priced.ValidationErrors));
            Assert.AreEqual(scenario.ExpectedCents, priced.TotalTtc.Cents);
            Assert.AreEqual(scenario.FirstEligible || scenario.SecondEligible, priced.PickupDiscountApplied);
        }
    }

    [TestMethod]
    public void ExistingSnapshotsPreserveSignedAdjustmentDiscountBoundaries()
    {
        var eligibleNegative = OrderPricingService.CalculateSnapshots(
            [Item("NEG", 1000, true, adjustments: [Adjustment("Sans sauce", -200)])], FulfilmentMode.Retrait, true, Settings());
        var eligiblePositive = OrderPricingService.CalculateSnapshots(
            [Item("POS", 1000, true, adjustments: [Adjustment("Supplément", 200)])], FulfilmentMode.Retrait, true, Settings());
        var nonEligibleSigned = OrderPricingService.CalculateSnapshots(
            [Item("NON", 1000, false, adjustments: [Adjustment("Réduction", -200), Adjustment("Supplément", 300)])], FulfilmentMode.Retrait, true, Settings());

        Assert.IsTrue(eligibleNegative.IsValid);
        Assert.IsTrue(eligiblePositive.IsValid);
        Assert.IsTrue(nonEligibleSigned.IsValid);
        Assert.AreEqual(720L, eligibleNegative.TotalTtc.Cents, "A negative adjustment belongs to the eligible product component before its 10% discount.");
        Assert.AreEqual(1100L, eligiblePositive.TotalTtc.Cents, "A positive surcharge remains outside the eligible product discount.");
        Assert.AreEqual(1100L, nonEligibleSigned.TotalTtc.Cents, "A non-eligible line, including signed adjustments, remains undiscounted.");
        Assert.IsFalse(nonEligibleSigned.PickupDiscountApplied);
    }

    [TestMethod]
    public void SyntheticTwentyFiveFortyNineRequiresAnAdditionalPersistedOrSettingsComponent()
    {
        var withPersistedNegativeAdjustment = OrderPricingService.CalculateSnapshots(
            [Item("TST002", 1200, true, adjustments: [Adjustment("Ajustement historique", -68)]), Item("TST001A", 850, true, quantity: 2)],
            FulfilmentMode.Retrait, true, Settings());
        var withDifferentCurrentRate = OrderPricingService.CalculateSnapshots(
            [Item("TST002", 1200, true), Item("TST001A", 850, true, quantity: 2)],
            FulfilmentMode.Retrait, true, Settings(rate: 0.121m));

        Assert.AreEqual(2549L, withPersistedNegativeAdjustment.TotalTtc.Cents);
        Assert.AreEqual(2549L, withDifferentCurrentRate.TotalTtc.Cents);
        Assert.AreEqual(-68L, withPersistedNegativeAdjustment.Items.Single(item => item.ProductCode == "TST002").Adjustments.Single().AdjustmentTtcPerUnit.Cents);
        Assert.AreEqual(0.121m, withDifferentCurrentRate.PickupDiscountRate);
    }

    [TestMethod]
    public void ExistingSnapshotsUseLineComponentFirstRoundingAtTwelvePointFivePercent()
    {
        var priced = OrderPricingService.CalculateSnapshots(
            [
                Item("TST002", 1200, true, adjustments: [Adjustment("Sans accompagnement", -100), Adjustment("Sauce premium", 100)]),
                Item("TST001A", 850, true, quantity: 2)
            ],
            FulfilmentMode.Retrait,
            pickupDiscountRequested: true,
            Settings(rate: 0.125m));

        Assert.IsTrue(priced.IsValid, string.Join(";", priced.ValidationErrors));
        Assert.AreEqual(2551L, priced.TotalTtc.Cents);
        Assert.AreEqual(1063L, priced.Items.Single(item => item.ProductCode == "TST002").CalculatedLineTotalTtc.Cents);
        Assert.AreEqual(1488L, priced.Items.Single(item => item.ProductCode == "TST001A").CalculatedLineTotalTtc.Cents);
        Assert.AreEqual(2451L, priced.TaxBreakdown.Single(tax => tax.VatRate == 10m).TaxableTtc.Cents);
    }

    private static OrderItemSnapshot Item(string code, long unitCents, bool eligible, int quantity = 1, IReadOnlyList<OrderLineAdjustmentSnapshot>? adjustments = null) => new(
        Guid.NewGuid(), 0, Guid.NewGuid(), code, code, "Synthetic", Money.FromCents(unitCents), 10m, eligible, quantity,
        Money.FromCents(unitCents * quantity), Money.FromCents(unitCents * quantity), adjustments ?? []);

    private static OrderLineAdjustmentSnapshot Adjustment(string label, long perUnitCents) => new(
        Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, label, Money.FromCents(perUnitCents), perUnitCents < 0 ? 10m : 5.5m);

    private static BusinessSettings Settings(decimal rate = 0.10m) =>
        BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountRate = rate, PickupDiscountMinTotalTtc = Money.Zero };
}
