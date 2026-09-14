using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
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
    public async Task OrdinaryModificationPreservesNonAuthoritativeSourceTotal()
    {
        var current = Snapshot(BusinessDate, total: 3231) with { SourceTotalTtc = Money.FromCents(3590) };
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow)));

        var result = await service.SaveModificationAsync(current with { SourceTotalTtc = null, Comment = "ordinary edit" });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(Money.FromCents(3590), store.Snapshot!.SourceTotalTtc);
        Assert.AreEqual("ordinary edit", store.Snapshot.Comment);
    }

    [TestMethod]
    public async Task PaymentModificationPreservesNonAuthoritativeSourceTotal()
    {
        var current = Snapshot(BusinessDate, total: 1000) with { SourceTotalTtc = Money.FromCents(3590) };
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow)));

        var result = await service.SaveModificationAsync(current with { CardPaymentTtc = Money.FromCents(500) }, BusinessDate);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(Money.FromCents(3590), store.Snapshot!.SourceTotalTtc);
        Assert.AreEqual(500L, store.Snapshot.CardPaymentTtc.Cents);
    }

    [TestMethod]
    public async Task PriceAffectingModificationPreservesNonAuthoritativeSourceTotal()
    {
        var current = Snapshot(BusinessDate, total: 1000) with { SourceTotalTtc = Money.FromCents(3590) };
        var item = current.Items.Single();
        var repricedItem = item with
        {
            Quantity = 2,
            ExtendedBaseTtc = Money.FromCents(2000),
            CalculatedLineTotalTtc = Money.FromCents(2000)
        };
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow)));

        var result = await service.SaveModificationAsync(current with { Items = [repricedItem] });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(Money.FromCents(3590), store.Snapshot!.SourceTotalTtc);
        Assert.AreEqual(2000L, store.Snapshot.TotalTtc.Cents);
    }

    [TestMethod]
    public async Task ClosePreservesNonAuthoritativeSourceTotal()
    {
        var current = Snapshot(BusinessDate, total: 1000) with
        {
            SourceTotalTtc = Money.FromCents(3590),
            CardPaymentTtc = Money.FromCents(1000)
        };
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock());

        var result = await service.CloseAsync(current.Id);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(OrderStatus.Closed, store.Snapshot!.Status);
        Assert.AreEqual(Money.FromCents(3590), store.Snapshot.SourceTotalTtc);
    }

    [TestMethod]
    public async Task CancelPreservesNonAuthoritativeSourceTotal()
    {
        var current = Snapshot(BusinessDate, total: 1000) with { SourceTotalTtc = Money.FromCents(3590) };
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock());

        var result = await service.CancelAsync(current.Id);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(OrderStatus.Cancelled, store.Snapshot!.Status);
        Assert.AreEqual(Money.FromCents(3590), store.Snapshot.SourceTotalTtc);
    }

    [TestMethod]
    public async Task NonAuthoritativeLifecycleMutationIsRejectedBeforePersistence()
    {
        var current = Snapshot(BusinessDate, total: 1000) with { CardPaymentTtc = Money.FromCents(1000) };
        var store = new LifecycleStore(current);
        using var service = new OrderLifecycleService(
            store, new DeterministicIds(), new FixedClock(),
            authorityGuard: new TestWriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly));

        var result = await service.CloseAsync(current.Id);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.AuthorityBlocked, result.Issues.Single().StableCode);
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task LifecycleCommitNotifiesWithNonCancellableTokenAfterCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var current = Snapshot(BusinessDate, total: 1000);
        var store = new LifecycleStore(current) { AfterSave = cancellation.Cancel };
        var notifier = new RecordingNotifier();
        using var service = new OrderLifecycleService(
            store,
            new DeterministicIds(),
            new FixedClock(),
            new TestWriteAuthorityGuard(WriteAuthorityState.Authoritative),
            notifier);

        var result = await service.SaveModificationAsync(current with { Comment = "committed change" }, cancellationToken: cancellation.Token);

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.AreEqual(1, notifier.Calls);
        Assert.IsFalse(notifier.LastToken.IsCancellationRequested);
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

    [TestMethod]
    public async Task ExistingQuantityChangeRepricesTheFullPricingMatrixFromHistoricalSnapshots()
    {
        var scenarios = new[]
        {
            (Name: "both eligible", FirstEligible: true, SecondEligible: true, FirstAdjustments: Array.Empty<OrderLineAdjustmentSnapshot>(), ExpectedCents: 2610L),
            (Name: "first non-eligible", FirstEligible: false, SecondEligible: true, FirstAdjustments: Array.Empty<OrderLineAdjustmentSnapshot>(), ExpectedCents: 2730L),
            (Name: "second non-eligible", FirstEligible: true, SecondEligible: false, FirstAdjustments: Array.Empty<OrderLineAdjustmentSnapshot>(), ExpectedCents: 2780L),
            (Name: "neither eligible", FirstEligible: false, SecondEligible: false, FirstAdjustments: Array.Empty<OrderLineAdjustmentSnapshot>(), ExpectedCents: 2900L),
            (Name: "eligible negative adjustment", FirstEligible: true, SecondEligible: true, FirstAdjustments: new[] { Adjustment("Reduction", -200) }, ExpectedCents: 2430L),
            (Name: "eligible positive surcharge", FirstEligible: true, SecondEligible: true, FirstAdjustments: new[] { Adjustment("Supplement", 200) }, ExpectedCents: 2810L),
            (Name: "non-eligible signed adjustments", FirstEligible: false, SecondEligible: true, FirstAdjustments: new[] { Adjustment("Reduction", -200), Adjustment("Supplement", 300) }, ExpectedCents: 2830L)
        };

        foreach (var scenario in scenarios)
        {
            var first = PricingItem("TST002", 1200, scenario.FirstEligible, adjustments: scenario.FirstAdjustments);
            var second = PricingItem("TST001A", 850, scenario.SecondEligible);
            var current = Snapshot(BusinessDate, total: 9999) with
            {
                Items = [first, second],
                PickupDiscountApplied = true,
                PickupDiscountRate = 0.10m,
                ManualTotalOverrideActive = true
            };
            var store = new LifecycleStore(current);
            var catalogue = new ThrowingCatalogueQueries();
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero });
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), catalogue, settings);

            var quantityChangedSecond = second with { Quantity = 2, ExtendedBaseTtc = Money.FromCents(1700), CalculatedLineTotalTtc = Money.FromCents(1700) };
            var result = await service.SaveModificationAsync(current with { Items = [first, quantityChangedSecond] });

            Assert.IsTrue(result.Succeeded, $"{scenario.Name}: {string.Join(";", result.Issues.Select(issue => issue.Message))}");
            Assert.AreEqual(scenario.ExpectedCents, store.Snapshot!.TotalTtc.Cents, scenario.Name);
            Assert.IsFalse(store.Snapshot.ManualTotalOverrideActive, scenario.Name);
            Assert.AreEqual(0, catalogue.Calls, "An existing historical quantity edit must not read current Catalogue data.");
            Assert.AreEqual(1200L, store.Snapshot.Items[0].ProductBasePriceTtc.Cents);
            Assert.AreEqual(scenario.FirstEligible, store.Snapshot.Items[0].ProductDiscountEligible);
            Assert.AreEqual(10m, store.Snapshot.Items[0].ProductVatRate);
            CollectionAssert.AreEqual(scenario.FirstAdjustments, store.Snapshot.Items[0].Adjustments.ToArray());
            Assert.AreEqual(850L, store.Snapshot.Items[1].ProductBasePriceTtc.Cents);
            Assert.AreEqual(scenario.SecondEligible, store.Snapshot.Items[1].ProductDiscountEligible);
            Assert.AreEqual(2, store.Snapshot.Items[1].Quantity);
        }
    }

    [TestMethod]
    public async Task ExistingOrderRepriceUsesLineComponentFirstRoundingAndKeepsHistoricalSnapshotAuthority()
    {
        var first = PricingItem("TST002", 1200, true, adjustments: [Adjustment("Sans accompagnement", -100), Adjustment("Sauce premium", 100)]) with
        {
            CalculatedLineTotalTtc = Money.FromCents(1062)
        };
        var second = PricingItem("TST001A", 850, true, quantity: 2) with
        {
            CalculatedLineTotalTtc = Money.FromCents(1487)
        };
        var current = Snapshot(BusinessDate, total: 2549) with
        {
            Items = [first, second],
            PickupDiscountApplied = true,
            PickupDiscountRate = 0.125m,
            ManualTotalOverrideActive = true
        };
        var store = new LifecycleStore(current);
        var catalogue = new ThrowingCatalogueQueries();
        var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with
        {
            PickupDiscountRate = 0.125m,
            PickupDiscountMinTotalTtc = Money.Zero
        });
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), catalogue, settings);

        var proposedSecond = second with { CalculatedLineTotalTtc = Money.FromCents(1700) };
        var result = await service.SaveModificationAsync(current with { Items = [first, proposedSecond] });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(2551L, store.Snapshot!.TotalTtc.Cents);
        Assert.AreEqual(1063L, store.Snapshot.Items.Single(item => item.ProductCode == "TST002").CalculatedLineTotalTtc.Cents);
        Assert.AreEqual(1488L, store.Snapshot.Items.Single(item => item.ProductCode == "TST001A").CalculatedLineTotalTtc.Cents);
        Assert.IsFalse(store.Snapshot.ManualTotalOverrideActive);
        Assert.AreEqual(0, catalogue.Calls);
        CollectionAssert.AreEqual(first.Adjustments.ToArray(), store.Snapshot.Items.Single(item => item.ProductCode == "TST002").Adjustments.ToArray());
    }

    private static OrderItemSnapshot PricingItem(string code, long unitCents, bool eligible, int quantity = 1, IReadOnlyList<OrderLineAdjustmentSnapshot>? adjustments = null) => new(
        Guid.NewGuid(), code == "TST002" ? 0 : 1, Guid.NewGuid(), code, code, "Synthetic", Money.FromCents(unitCents), 10m, eligible,
        quantity, Money.FromCents(unitCents * quantity), Money.FromCents(unitCents * quantity), adjustments ?? []);

    private static OrderLineAdjustmentSnapshot Adjustment(string label, long perUnitCents) => new(
        Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, label, Money.FromCents(perUnitCents), perUnitCents < 0 ? 10m : 5.5m);

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
        public Action? AfterSave { get; init; }
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { Snapshot = snapshot; SaveCalls++; AfterSave?.Invoke(); return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(Snapshot.Id == orderId ? Snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) { Snapshot = snapshot; Adjustments.AddRange(adjustments); SaveCalls++; AfterSave?.Invoke(); return Task.CompletedTask; }
        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
        public Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(new OrderOperationalSummary(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0));
    }

    private sealed class SettingsStore(BusinessSettings initial) : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(initial);
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class ThrowingCatalogueQueries : IOrderEntryCatalogueQueries
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Throw<IReadOnlyList<CategorySummary>>();
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default) => Throw<IReadOnlyList<ProductSummary>>();
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Throw<OrderEntryProduct?>();
        private Task<T> Throw<T>() { Calls++; throw new AssertFailedException("Historical quantity repricing must not consult current Catalogue data."); }
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

    private sealed class TestWriteAuthorityGuard(WriteAuthorityState initialState) : IWriteAuthorityGuard
    {
        public WriteAuthorityState State { get; } = initialState;
        public void RequireWriteAuthority()
        {
            if (State != WriteAuthorityState.Authoritative) throw new WriteAuthorityException(State);
        }
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
