using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class M07Wp9GuardIntegrationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 8);
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task EveryNonWritableM07PhaseRejectsAllApplicationMutationFamiliesBeforePersistence()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var catalogueStore = new RecordingCatalogueStore(categoryId, productId);
        var settingsStore = new RecordingSettingsStore();
        var aggregate = Product(productId, categoryId);
        var orderCatalogue = new RecordingOrderCatalogue(aggregate, "Plats");
        var orderStore = new RecordingOrderStore(Snapshot(productId));
        var notifier = new RecordingNotifier();
        var guard = new ApplicationWriteAuthorityGuard();
        using var entry = new OrderEntryService(
            orderCatalogue, settingsStore, orderStore, new NoOpPrintDispatcher(), new DeterministicIds(), new FixedClock(), guard, notifier);
        using var lifecycle = new OrderLifecycleService(
            orderStore, new DeterministicIds(), new FixedClock(), guard, notifier, orderCatalogue, settingsStore);
        var catalogue = new CatalogueService(catalogueStore, guard, notifier);
        var settings = new BusinessSettingsService(settingsStore, guard, notifier);

        var blockedPhases = new[]
        {
            AuthorityPhase.Uninitialized,
            AuthorityPhase.PairedUninitializedReadOnly,
            AuthorityPhase.TransferPreparing,
            AuthorityPhase.RelinquishedPendingGrant,
            AuthorityPhase.ReleasedNonAuthoritative,
            AuthorityPhase.TargetAcquisitionPending,
            AuthorityPhase.NonAuthoritativeReadOnly,
            AuthorityPhase.StaleGeneration,
            AuthorityPhase.DisasterRecoveryPreparing,
            AuthorityPhase.DisasterRecoveryPending,
            AuthorityPhase.RecoveryRequired
        };

        foreach (var phase in blockedPhases)
        {
            guard.SetState(WriteStateFor(phase));

            AssertAuthorityBlocked(await catalogue.CreateCategoryAsync("Plats"), phase);
            AssertAuthorityBlocked(await catalogue.RenameCategoryAsync(categoryId, "Entrées"), phase);
            AssertAuthorityBlocked(await catalogue.CreateCategoryWithCodeAsync("Desserts", "D"), phase);
            AssertAuthorityBlocked(await catalogue.RenameCategoryWithCodeAsync(categoryId, "Boissons", "B"), phase);
            AssertAuthorityBlocked(await catalogue.CreateProductAsync(ProductDraft(productId, categoryId, "P2")), phase);
            AssertAuthorityBlocked(await catalogue.UpdateProductAsync(productId, ProductDraft(productId, categoryId, "P2")), phase);
            AssertAuthorityBlocked(await catalogue.SetProductActiveAsync(productId, false), phase);
            AssertAuthorityBlocked(await catalogue.BulkSetProductsActiveAsync(
                new BulkProductActiveStateRequest(false, [new BulkProductActiveStateItem(productId, true)])), phase);
            AssertAuthorityBlocked(await catalogue.DeleteProductAsync(productId), phase);
            AssertAuthorityBlocked(await settings.UpdateAsync(BusinessSettings.Defaults(Now) with { PickupDiscountRate = 0.125m }), phase);
            Assert.IsFalse((await entry.ConfirmNewOrderAsync(NewOrder(aggregate))).Succeeded, phase.ToString());
            Assert.IsFalse((await lifecycle.SaveModificationAsync(orderStore.Snapshot! with { Comment = "changed" })).Succeeded, phase.ToString());
            Assert.IsFalse((await lifecycle.CloseAsync(orderStore.Snapshot!.Id)).Succeeded, phase.ToString());
            Assert.IsFalse((await lifecycle.CancelAsync(orderStore.Snapshot!.Id)).Succeeded, phase.ToString());

            Assert.AreEqual(0, catalogueStore.MutationCount, phase.ToString());
            Assert.AreEqual(0, settingsStore.MutationCount, phase.ToString());
            Assert.AreEqual(0, orderStore.MutationCount, phase.ToString());
            Assert.AreEqual(0, notifier.Calls, phase.ToString());
        }
    }

    [TestMethod]
    public async Task AuthoritativeGuardAllowsEachApplicationMutationFamilyAndNotifiesAfterCommit()
    {
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var catalogueStore = new RecordingCatalogueStore(categoryId, productId);
        var settingsStore = new RecordingSettingsStore();
        var aggregate = Product(productId, categoryId);
        var orderCatalogue = new RecordingOrderCatalogue(aggregate, "Plats");
        var orderStore = new RecordingOrderStore(Snapshot(productId));
        var notifier = new RecordingNotifier();
        var guard = new ApplicationWriteAuthorityGuard(WriteAuthorityState.Authoritative);
        using var entry = new OrderEntryService(
            orderCatalogue, settingsStore, orderStore, new NoOpPrintDispatcher(), new DeterministicIds(), new FixedClock(), guard, notifier);
        using var lifecycle = new OrderLifecycleService(
            orderStore, new DeterministicIds(), new FixedClock(), guard, notifier, orderCatalogue, settingsStore);
        var catalogue = new CatalogueService(catalogueStore, guard, notifier);
        var settings = new BusinessSettingsService(settingsStore, guard, notifier);

        Assert.IsTrue((await catalogue.CreateCategoryAsync("Plats")).Succeeded);
        Assert.IsTrue((await settings.UpdateAsync(BusinessSettings.Defaults(Now) with { PickupDiscountRate = 0.125m })).Succeeded);
        Assert.IsTrue((await entry.ConfirmNewOrderAsync(NewOrder(aggregate))).Succeeded);
        Assert.IsTrue((await lifecycle.CancelAsync(orderStore.Snapshot!.Id)).Succeeded);

        Assert.IsGreaterThan(0, catalogueStore.MutationCount);
        Assert.AreEqual(1, settingsStore.MutationCount);
        Assert.IsGreaterThan(0, orderStore.MutationCount);
        Assert.IsGreaterThan(0, notifier.Calls);
    }

    private static void AssertAuthorityBlocked(OperationResult result, AuthorityPhase phase)
    {
        Assert.IsFalse(result.Succeeded, phase.ToString());
        Assert.AreEqual(ValidationCodes.AuthorityBlocked, result.Issues.Single().StableCode, phase.ToString());
    }

    private static WriteAuthorityState WriteStateFor(AuthorityPhase phase) => phase switch
    {
        AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority => WriteAuthorityState.Authoritative,
        AuthorityPhase.TransferPreparing or AuthorityPhase.RelinquishedPendingGrant
            or AuthorityPhase.TargetAcquisitionPending or AuthorityPhase.DisasterRecoveryPreparing
            or AuthorityPhase.DisasterRecoveryPending => WriteAuthorityState.Transitioning,
        AuthorityPhase.Uninitialized or AuthorityPhase.RecoveryRequired => WriteAuthorityState.RecoveryRequired,
        _ => WriteAuthorityState.NonAuthoritativeReadOnly
    };

    private static ProductAggregate Product(Guid productId, Guid categoryId) => new(
        new Product(productId, "P1", "Saumon", categoryId, Money.FromCents(1200), 10m, true, true, false, default, default),
        [], new Dictionary<Guid, IReadOnlyList<ProductOption>>());

    private static ProductDraft ProductDraft(Guid productId, Guid categoryId, string code) =>
        new(productId, code, "Saumon", categoryId, Money.FromCents(1200), 10m, true, true, false, []);

    private static NewOrderDraft NewOrder(ProductAggregate product) =>
        new([new OrderLineDraft(Guid.NewGuid(), product, [], [], 1, "Plats")], FulfilmentMode.Retrait, BusinessDate, new TimeOnly(12, 0), null, null, null, false);

    private static OrderSnapshot Snapshot(Guid productId) => new(
        Guid.NewGuid(), OrderSourceType.Pos, OrderStatus.Open, Now, Now, null, null, FulfilmentMode.Retrait,
        BusinessDate, new TimeOnly(12, 0), false, null, null, null, Money.FromCents(1000), false, false, null, Money.Zero,
        [new(Guid.NewGuid(), 0, productId, "P1", "Saumon", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])], [])
    { Reference = "S81-TEST" };

    private sealed class RecordingCatalogueStore(Guid categoryId, Guid productId) : ICatalogueStore
    {
        public int MutationCount { get; private set; }

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([new(categoryId, "Plats", "P")]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(ProductDraft(productId, categoryId, "P1"));
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(new(id, name)));
        public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(new(categoryId, name, shortCode)));
        public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid id, string name, string? shortCode, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(new(id, name, shortCode)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Mutate(OperationResult<Guid>.Success(productId));
        public Task<OperationResult> UpdateProductAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken = default) => Mutate(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken = default) => Mutate(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Mutate(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 1)));
        public Task<OperationResult> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Mutate(OperationResult.Success());
        private Task<T> Mutate<T>(T value) { MutationCount++; return Task.FromResult(value); }
    }

    private sealed class RecordingSettingsStore : IBusinessSettingsStore
    {
        private BusinessSettings current = BusinessSettings.Defaults(Now);
        public int MutationCount { get; private set; }
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(current);
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) { current = settings; MutationCount++; return Task.FromResult(OperationResult.Success()); }
    }

    private sealed class RecordingOrderCatalogue(ProductAggregate product, string categoryName) : IOrderEntryCatalogueQueries
    {
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(new(product, categoryName));
    }

    private sealed class RecordingOrderStore(OrderSnapshot initial) : IOrderStore, IOrderLifecycleStore
    {
        public OrderSnapshot? Snapshot { get; private set; } = initial;
        public int MutationCount { get; private set; }
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { Snapshot = snapshot; MutationCount++; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot?.Id == orderId ? Snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) { Snapshot = snapshot; MutationCount++; return Task.CompletedTask; }
        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
        public Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(new OrderOperationalSummary(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0, Money.Zero, Money.Zero));
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class NoOpPrintDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => Now;
        public DateOnly BusinessDate => M07Wp9GuardIntegrationTests.BusinessDate;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int counter;
        public Guid NewId() => Guid.Parse($"50000000-0000-0000-0000-{Interlocked.Increment(ref counter):D12}");
    }

    private sealed class ApplicationWriteAuthorityGuard(WriteAuthorityState initialState = WriteAuthorityState.Uninitialized) : IWriteAuthorityGuard
    {
        public WriteAuthorityState State { get; private set; } = initialState;
        public void SetState(WriteAuthorityState state) => State = state;
        public void RequireWriteAuthority()
        {
            if (State != WriteAuthorityState.Authoritative) throw new WriteAuthorityException(State);
        }
    }
}
