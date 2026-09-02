using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class OrderLifecycleApplicationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 31);

    [TestMethod]
    public async Task OverdueOrderAllowsPaymentCorrectionAndPreservesHistoricalDate()
    {
        var current = Snapshot(BusinessDate.AddDays(-1), total: 1000);
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow)));

        var result = await service.SaveModificationAsync(current with { CardPaymentTtc = Money.FromCents(500) }, BusinessDate);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(BusinessDate.AddDays(-1), store.Snapshot!.PlannedFulfilmentDate);
        Assert.AreEqual(500L, store.Snapshot.CardPaymentTtc.Cents);
        Assert.HasCount(1, store.Adjustments);
        Assert.AreEqual(500L, store.Adjustments[0].Delta.Cents);
    }

    [TestMethod]
    public async Task ExplicitlyChangingAnOverdueDateRemainsRejected()
    {
        var current = Snapshot(BusinessDate.AddDays(-1), total: 1000);
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow)));

        var result = await service.SaveModificationAsync(current with { PlannedFulfilmentDate = BusinessDate.AddDays(-2) });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.PastPlannedDate, result.Issues.Single().StableCode);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task ExistingOrderWithNullPlannedTimeRemainsEditableWithoutInventingTime()
    {
        var current = Snapshot(BusinessDate, total: 1000) with { PlannedFulfilmentTime = null };
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow)));

        var result = await service.SaveModificationAsync(current with { Comment = "updated without a time" }, BusinessDate);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.IsNull(store.Snapshot.PlannedFulfilmentTime);
        Assert.AreEqual("updated without a time", store.Snapshot.Comment);
    }

    [TestMethod]
    public async Task ExistingQuantityChangeUsesCurrentSettingsAndHistoricalLineSnapshot()
    {
        var item = new OrderItemSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), "P-HIST", "Plat historique", "Plats", Money.FromCents(3000), 10m, true, 1, Money.FromCents(3000), Money.FromCents(2700), []);
        var current = Snapshot(BusinessDate, total: 2700) with { Items = [item], PickupDiscountApplied = true, PickupDiscountRate = 0.10m };
        var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountRate = 0.20m, PickupDiscountMinTotalTtc = Money.FromCents(2000) });
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);

        var changed = item with { Quantity = 2, ExtendedBaseTtc = Money.FromCents(6000), CalculatedLineTotalTtc = Money.FromCents(5400) };
        var result = await service.SaveModificationAsync(current with { Items = [changed] });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(4800L, store.Snapshot!.TotalTtc.Cents);
        Assert.AreEqual(3000L, store.Snapshot.Items.Single().ProductBasePriceTtc.Cents);
        Assert.AreEqual(6000L, store.Snapshot.Items.Single().ExtendedBaseTtc.Cents);
        Assert.AreEqual(0.20m, store.Snapshot.PickupDiscountRate);
        Assert.IsFalse(store.Snapshot.ManualTotalOverrideActive);
    }

    private static OrderSnapshot Snapshot(DateOnly plannedDate, long total) => new(
        Guid.NewGuid(), OrderSourceType.Pos, OrderStatus.Open,
        new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 31, 8, 0, 0, TimeSpan.Zero),
        null, null, FulfilmentMode.Retrait, plannedDate, new TimeOnly(11, 0), false,
        "06 12 34 56 78", null, "synthetic", Money.FromCents(total), false, false, null, Money.Zero,
        [new(Guid.NewGuid(), 0, Guid.NewGuid(), "P", "Plat", "Plats", Money.FromCents(total), 10m, true, 1, Money.FromCents(total), Money.FromCents(total), [])], [])
    { Reference = "20260831-001" };

    private sealed class LifecycleStore(OrderSnapshot initial) : IOrderStore, IOrderLifecycleStore
    {
        public OrderSnapshot Snapshot { get; private set; } = initial;
        public List<PaymentAdjustment> Adjustments { get; } = [];
        public int SaveCalls { get; private set; }
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { Snapshot = snapshot; SaveCalls++; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(Snapshot.Id == orderId ? Snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) { Snapshot = snapshot; Adjustments.AddRange(adjustments); SaveCalls++; return Task.CompletedTask; }
        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
        public Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(new OrderOperationalSummary(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0));
    }

    private sealed class SettingsStore(BusinessSettings initial) : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(initial);
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => OrderLifecycleApplicationTests.BusinessDate;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int counter;
        public Guid NewId() => Guid.Parse($"20000000-0000-0000-0000-{Interlocked.Increment(ref counter):D12}");
    }
}
