using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class OrderEntryApplicationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 31);

    [TestMethod]
    public async Task StaleOptionalOptionIsRejectedBeforeSaveAndDispatch()
    {
        var productId = Guid.NewGuid();
        var staleOptionId = Guid.NewGuid();
        var draftProduct = Product(productId, staleOptionId, optionsEnabled: true);
        var currentProduct = Product(productId, Guid.NewGuid(), optionsEnabled: true);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(currentProduct)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(draftProduct, [staleOptionId]));

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.DispatchSucceeded);
        Assert.AreEqual(0, store.SaveCalls);
        Assert.AreEqual(0, dispatcher.Calls);
        StringAssert.Contains(string.Join(";", result.Issues.Select(issue => issue.Message)), "no longer available");
    }

    [TestMethod]
    public async Task UnknownWrongAndInactiveProductsCannotBeConfirmed()
    {
        var productId = Guid.NewGuid();
        var validDraft = Draft(Product(productId, Guid.NewGuid(), optionsEnabled: false));
        var inactive = Product(productId, Guid.Empty, optionsEnabled: false) with
        {
            Product = Product(productId, Guid.Empty, optionsEnabled: false).Product with { IsActive = false }
        };
        var wrongProduct = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        foreach (var current in new OrderEntryProduct?[] { null, Entry(inactive), Entry(wrongProduct) })
        {
            var store = new RecordingOrderStore();
            var dispatcher = new RecordingDispatcher();
            using var service = CreateService(new FakeCatalogue(current), store, dispatcher);

            var result = await service.ConfirmNewOrderAsync(validDraft);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(0, store.SaveCalls);
            Assert.AreEqual(0, dispatcher.Calls);
        }
    }

    [TestMethod]
    public async Task EmptyProductIdIsRejectedWithoutCatalogueBypass()
    {
        var emptyProduct = Product(Guid.Empty, Guid.Empty, optionsEnabled: false);
        var catalogue = new FakeCatalogue(Entry(Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false)));
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(catalogue, store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(emptyProduct));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, catalogue.GetCalls);
        Assert.AreEqual(0, store.SaveCalls);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task FulfilmentAndDateAreRequiredBeforePersistence()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var missingFulfilment = await service.ConfirmNewOrderAsync(Draft(product) with { Fulfilment = null });
        var missingDate = await service.ConfirmNewOrderAsync(Draft(product) with { PlannedFulfilmentDate = null });

        Assert.IsFalse(missingFulfilment.Succeeded);
        Assert.IsFalse(missingDate.Succeeded);
        Assert.AreEqual(0, store.SaveCalls);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task ConfirmationUsesCurrentSettingsAndNormalizesOptionalFields()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false, price: Money.FromCents(3000));
        var settings = BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountRate = 0.2m, PickupDiscountMinTotalTtc = Money.FromCents(2000) };
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher, settings);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with
        {
            Telephone = "0612345678",
            DeliveryAddress = "  12 rue des Tests ",
            Comment = "  note  ",
            PickupDiscountRequested = true
        });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.IsNotNull(result.CommittedOrder);
        Assert.AreEqual("06 12 34 56 78", result.CommittedOrder!.Telephone);
        Assert.AreEqual("12 rue des Tests", result.CommittedOrder.DeliveryAddress);
        Assert.AreEqual("note", result.CommittedOrder.Comment);
        Assert.AreEqual(2400L, result.CommittedOrder.TotalTtc.Cents);
        Assert.AreEqual(OrderSourceType.Pos, result.CommittedOrder.SourceType);
        Assert.AreEqual(1, dispatcher.Calls);
    }

    [TestMethod]
    public async Task CommitReloadFailureIsReportedWithoutRetryingDispatch()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore { ThrowOnReload = true };
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product));

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.HasOutputFailure);
        Assert.IsNotNull(result.PersistedOrderId);
        Assert.IsNull(result.CommittedOrder);
        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task BusyConfirmationDoesNotDispatchOrWriteTwice()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new BlockingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var first = service.ConfirmNewOrderAsync(Draft(product));
        await store.SaveEntered.Task;
        var second = await service.ConfirmNewOrderAsync(Draft(product));
        store.Release.TrySetResult(true);
        var firstResult = await first;

        Assert.AreEqual(ValidationCodes.Busy, second.Issues.Single().StableCode);
        Assert.IsTrue(firstResult.Succeeded);
        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(1, dispatcher.Calls);
    }

    private static OrderEntryService CreateService(
        IOrderEntryCatalogueQueries catalogue,
        IOrderStore store,
        IOrderPrintDispatcher dispatcher,
        BusinessSettings? settings = null) =>
        new(catalogue, new FakeSettingsStore(settings ?? BusinessSettings.Defaults(DateTimeOffset.UtcNow)), store, dispatcher, new DeterministicIds(), new FixedClock());

    private static NewOrderDraft Draft(ProductAggregate product, IReadOnlyList<Guid>? selected = null) =>
        new([new OrderLineDraft(Guid.Empty, product, selected ?? [], [], 1, "Plats")], FulfilmentMode.Retrait, BusinessDate, null, null, null, null, false);

    private static OrderEntryProduct? Entry(ProductAggregate? product) => product is null ? null : new OrderEntryProduct(product, "Plats");

    private static ProductAggregate Product(Guid productId, Guid optionId, bool optionsEnabled, Money? price = null)
    {
        var groupId = Guid.NewGuid();
        var product = new Product(productId, "P1", "Plat", Guid.NewGuid(), price ?? Money.FromCents(1000), 10m, true, true, optionsEnabled, default, default);
        if (!optionsEnabled) return ProductAggregate.Empty(product);
        return new ProductAggregate(product,
            [new OptionGroup(groupId, productId, "Options", SelectionMode.Single, false, null, null, 0, default, default)],
            new Dictionary<Guid, IReadOnlyList<ProductOption>>
            {
                [groupId] = [new ProductOption(optionId, groupId, "Choix", Money.FromCents(50), true, 0, default, default)]
            });
    }

    private sealed class FakeCatalogue(OrderEntryProduct? current) : IOrderEntryCatalogueQueries
    {
        public int GetCalls { get; private set; }
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) { GetCalls++; return Task.FromResult(current); }
    }

    private sealed class FakeSettingsStore(BusinessSettings settings) : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(settings);
        public Task<OperationResult> UpdateAsync(BusinessSettings updated, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private class RecordingOrderStore : IOrderStore
    {
        public int SaveCalls { get; private set; }
        public bool ThrowOnReload { get; init; }
        public OrderSnapshot? Snapshot { get; private set; }
        public virtual Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { SaveCalls++; Snapshot = snapshot; return Task.CompletedTask; }
        public virtual Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnReload) throw new InvalidOperationException("synthetic reload failure");
            return Task.FromResult(Snapshot);
        }
    }

    private sealed class BlockingOrderStore : RecordingOrderStore
    {
        public TaskCompletionSource<bool> SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            SaveEntered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            await base.SaveAsync(snapshot, cancellationToken);
        }
    }

    private sealed class RecordingDispatcher : IOrderPrintDispatcher
    {
        public int Calls { get; private set; }
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => BusinessDateValue;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
        private static DateOnly BusinessDateValue => OrderEntryApplicationTests.BusinessDate;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }
}
