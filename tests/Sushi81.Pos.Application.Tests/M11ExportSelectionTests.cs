using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class M11ExportSelectionTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DateOnly FulfilmentDate = new(2026, 9, 20);

    [TestMethod]
    public void SelectionUsesExplicitPosAndClosedGateAndInclusiveFulfilmentFilter()
    {
        var create = Source(Guid.Parse("11000000-0000-0000-0000-000000000001"), OrderStatus.Closed, OrderSourceType.Pos, 1000);
        var open = Source(Guid.Parse("11000000-0000-0000-0000-000000000002"), OrderStatus.Open, OrderSourceType.Pos, 1000);
        var hiboutik = Source(Guid.Parse("11000000-0000-0000-0000-000000000003"), OrderStatus.Closed, OrderSourceType.HiboutikPaste, 1000);
        var outside = Source(Guid.Parse("11000000-0000-0000-0000-000000000004"), OrderStatus.Closed, OrderSourceType.Pos, 1000) with
        {
            Snapshot = Source(Guid.Parse("11000000-0000-0000-0000-000000000004"), OrderStatus.Closed, OrderSourceType.Pos, 1000, new DateOnly(2026, 9, 19)).Snapshot
        };

        var result = ExportSelectionRules.Select(
            [create, open, hiboutik, outside],
            [],
            new ExportSelectionOptions(FulfilmentDate, FulfilmentDate),
            Utc);

        Assert.IsFalse(result.IsBlocked);
        Assert.HasCount(1, result.Actions);
        Assert.AreEqual(create.Snapshot.Id, result.Actions[0].OrderId);
        Assert.AreEqual(ExportAction.Create, result.Actions[0].Action);
    }

    [TestMethod]
    public void SettlementUsesEffectiveBusinessDateAndGroupsZeroNetReclassification()
    {
        var orderId = Guid.Parse("12000000-0000-0000-0000-000000000001");
        var source = Source(orderId, OrderStatus.Closed, OrderSourceType.Pos, 1000) with
        {
            PaymentAdjustments =
            [
                Adjustment(orderId, 600, PaymentBucket.Card, new DateOnly(2026, 9, 10), new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero)),
                Adjustment(orderId, 400, PaymentBucket.Cash, new DateOnly(2026, 9, 9), new DateTimeOffset(2026, 9, 20, 8, 1, 0, TimeSpan.Zero)),
                Adjustment(orderId, 100, PaymentBucket.Card, new DateOnly(2026, 9, 11), new DateTimeOffset(2026, 9, 20, 8, 2, 0, TimeSpan.Zero)),
                Adjustment(orderId, -100, PaymentBucket.Cash, new DateOnly(2026, 9, 11), new DateTimeOffset(2026, 9, 20, 8, 3, 0, TimeSpan.Zero))
            ]
        };

        var result = ExportSelectionRules.Select([source], [], new ExportSelectionOptions(), Utc);

        Assert.IsFalse(result.IsBlocked);
        Assert.HasCount(1, result.Actions);
        Assert.AreEqual(new DateOnly(2026, 9, 10), result.Actions[0].SettlementDate);
    }

    [TestMethod]
    public void CorrectionsWaitForClosedAndCancellationAnchorsToLastPositiveSnapshot()
    {
        var orderId = Guid.Parse("13000000-0000-0000-0000-000000000001");
        var original = Source(orderId, OrderStatus.Closed, OrderSourceType.Pos, 1000);
        Assert.IsTrue(ExportSelectionRules.TryBuildPositivePayload(original, Utc, out var emittedPositive, out _));
        var hash = ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(emittedPositive));
        var emission = new ExportEmissionRecord(Guid.Parse("13000000-0000-0000-0000-000000000099"), orderId, ExportAction.Create, hash, emittedPositive, FulfilmentDate, emittedPositive.SettlementDate, DateTimeOffset.UtcNow);

        var changedClosed = original with
        {
            Snapshot = original.Snapshot with { Comment = "changed after export", UpdatedAt = original.Snapshot.UpdatedAt.AddMinutes(1) }
        };
        var update = ExportSelectionRules.Select([changedClosed], [emission], new ExportSelectionOptions(), Utc);
        Assert.HasCount(1, update.Actions);
        Assert.AreEqual(ExportAction.Update, update.Actions[0].Action);

        var changedOpen = changedClosed with { Snapshot = changedClosed.Snapshot with { Status = OrderStatus.Open } };
        Assert.IsEmpty(ExportSelectionRules.Select([changedOpen], [emission], new ExportSelectionOptions(), Utc).Actions);

        var cancelled = changedClosed with { Snapshot = changedClosed.Snapshot with { Status = OrderStatus.Cancelled, CancelledAt = DateTimeOffset.UtcNow } };
        var cancel = ExportSelectionRules.Select([cancelled], [emission], new ExportSelectionOptions(), Utc);
        Assert.HasCount(1, cancel.Actions);
        Assert.AreEqual(ExportAction.Cancel, cancel.Actions[0].Action);
        Assert.AreEqual(emittedPositive.Comment, cancel.Actions[0].Comment);
        Assert.AreEqual("CANCELLED", cancel.Actions[0].OrderStatus);
    }

    [TestMethod]
    public void PositiveClosedOrderWithoutConsistentPaymentLedgerFailsClosed()
    {
        var source = Source(Guid.Parse("14000000-0000-0000-0000-000000000001"), OrderStatus.Closed, OrderSourceType.Pos, 1000) with
        {
            PaymentAdjustments = []
        };
        var result = ExportSelectionRules.Select([source], [], new ExportSelectionOptions(), Utc);

        Assert.IsTrue(result.IsBlocked);
        Assert.HasCount(1, result.Diagnostics);
        Assert.AreEqual("SETTLEMENT_DATE_UNAVAILABLE", result.Diagnostics[0].Code);
        Assert.IsEmpty(result.Actions);
    }

    private static ExportOrderSourceRecord Source(Guid id, OrderStatus status, OrderSourceType source, long total, DateOnly? fulfilmentDate = null)
    {
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        var snapshot = new OrderSnapshot(
            id,
            source,
            status,
            now,
            now,
            status == OrderStatus.Closed ? now : null,
            status == OrderStatus.Cancelled ? now : null,
            FulfilmentMode.Retrait,
            fulfilmentDate ?? FulfilmentDate,
            new TimeOnly(12, 0),
            false,
            null,
            null,
            "test order",
            Money.FromCents(total),
            false,
            false,
            null,
            Money.Zero,
            [new(Guid.Parse("15000000-0000-0000-0000-000000000001"), 0, Guid.NewGuid(), "P-1", "Produit", "Plats", Money.FromCents(total), 10m, true, 1, Money.FromCents(total), Money.FromCents(total), [])],
            [new(10m, Money.FromCents(total), Money.Zero, Guid.Parse("15000000-0000-0000-0000-000000000002"))])
        {
            CardPaymentTtc = Money.FromCents(total),
            CashPaymentTtc = Money.Zero
        };
        return new ExportOrderSourceRecord(snapshot, status == OrderStatus.Closed && total > 0
            ? [Adjustment(id, total, PaymentBucket.Card, fulfilmentDate ?? FulfilmentDate, now)]
            : []);
    }

    private static PaymentAdjustment Adjustment(Guid orderId, long cents, PaymentBucket bucket, DateOnly effectiveDate, DateTimeOffset recordedAt) =>
        new(Guid.NewGuid(), orderId, bucket, Money.FromCents(cents), new DateTimeOffset(effectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), recordedAt);
}
