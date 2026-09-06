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
    public async Task PastPlannedDateIsRejectedBeforePersistenceAndDispatch()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with
        {
            PlannedFulfilmentDate = BusinessDate.AddDays(-1)
        });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.PastPlannedDate, result.Issues.Single().StableCode);
        Assert.AreEqual(0, store.SaveCalls);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task MissingPlannedTimeIsAcceptedAndPersistedAsNull()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with { PlannedFulfilmentTime = null });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.IsNull(result.CommittedOrder!.PlannedFulfilmentTime);
        Assert.IsNull((await service.GetOrderByIdAsync(result.CommittedOrder.Id))!.PlannedFulfilmentTime);
        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(1, dispatcher.Calls);
    }

    [TestMethod]
    public async Task ManualTotalAlsoAllowsMissingPlannedTime()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with
        {
            PlannedFulfilmentTime = null,
            ManualTotalOverride = Money.FromCents(2500)
        });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.IsNull(result.CommittedOrder!.PlannedFulfilmentTime);
        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(1, dispatcher.Calls);
    }

    [TestMethod]
    public async Task LivraisonWithoutPlannedTimeIsAcceptedWhenItsCommercialRulesAreSatisfied()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false, price: Money.FromCents(4000));
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        var settings = BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { DeliveryMinMerchandiseTotalTtc = Money.Zero };
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher, settings);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with
        {
            Fulfilment = FulfilmentMode.Livraison,
            PlannedFulfilmentTime = null
        });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(FulfilmentMode.Livraison, result.CommittedOrder!.Fulfilment);
        Assert.IsNull(result.CommittedOrder.PlannedFulfilmentTime);
        Assert.IsNull((await service.GetOrderByIdAsync(result.CommittedOrder.Id))!.PlannedFulfilmentTime);
        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(1, dispatcher.Calls);
    }

    [TestMethod]
    public async Task InvalidPlannedTimeIsRejectedBeforePersistenceAndDispatch()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with { PlannedFulfilmentTime = new TimeOnly(15, 1) });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.PlannedTimeInvalid, result.Issues.Single().StableCode);
        Assert.AreEqual(0, store.SaveCalls);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task ExactApprovedPlannedTimeSucceeds()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with { PlannedFulfilmentTime = new TimeOnly(11, 5) });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(new TimeOnly(11, 5), result.CommittedOrder!.PlannedFulfilmentTime);
        Assert.AreEqual(1, store.SaveCalls);
        Assert.AreEqual(1, dispatcher.Calls);
    }

    [TestMethod]
    public async Task PlannedTimeWithNonZeroSecondsIsRejectedBeforePersistenceAndDispatch()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product) with { PlannedFulfilmentTime = new TimeOnly(11, 5, 30) });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.PlannedTimeInvalid, result.Issues.Single().StableCode);
        Assert.AreEqual(0, store.SaveCalls);
        Assert.AreEqual(0, dispatcher.Calls);
    }

    [TestMethod]
    public async Task PlannedTimeWithNonZeroSubSecondTicksIsRejectedBeforePersistenceAndDispatch()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var invalidTime = TimeOnly.FromTimeSpan(new TimeSpan(11, 5, 0).Add(TimeSpan.FromTicks(1)));
        var result = await service.ConfirmNewOrderAsync(Draft(product) with { PlannedFulfilmentTime = invalidTime });

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.PlannedTimeInvalid, result.Issues.Single().StableCode);
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
        Assert.AreEqual(new TimeOnly(11, 0), result.CommittedOrder.PlannedFulfilmentTime);
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

    [TestMethod]
    public async Task ActiveCatalogueListingSupportsCodeNameAndCategoryFilters()
    {
        var categoryId = Guid.NewGuid();
        var catalogue = new FilterCatalogue(categoryId);
        using var service = CreateService(catalogue, new RecordingOrderStore(), new RecordingDispatcher());

        var byCode = await service.ListActiveProductsAsync("P-001", categoryId);
        var byName = await service.ListActiveProductsAsync("Sushi", categoryId);
        var byCategory = await service.ListActiveProductsAsync(null, categoryId);

        Assert.HasCount(1, byCode);
        Assert.AreEqual("P-001", byCode[0].Code);
        Assert.HasCount(1, byName);
        Assert.AreEqual("Sushi saumon", byName[0].Name);
        Assert.HasCount(1, byCategory);
        Assert.IsTrue(byCategory.All(product => product.IsActive));
        CollectionAssert.AreEqual(new string?[] { "P-001", "Sushi", null }, catalogue.Searches.ToArray());
        Assert.IsTrue(catalogue.CategoryIds.All(id => id == categoryId));
    }

    [TestMethod]
    public async Task OptionalTelephoneAndInitialLivraisonAddressAreAllowed()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false, price: Money.FromCents(3000));
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher, BusinessSettings.Defaults(DateTimeOffset.UtcNow));

        var result = await service.ConfirmNewOrderAsync(Draft(product) with { Fulfilment = FulfilmentMode.Livraison, Telephone = null, DeliveryAddress = null });

        Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
        Assert.IsNull(result.CommittedOrder!.Telephone);
        Assert.IsNull(result.CommittedOrder.DeliveryAddress);
        Assert.AreEqual(new TimeOnly(11, 0), result.CommittedOrder.PlannedFulfilmentTime);
    }

    [TestMethod]
    public async Task ConfirmationAllocatesStableOpenPosIdsAndAdvanceMarker()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var sameDay = await service.ConfirmNewOrderAsync(Draft(product));
        var future = await service.ConfirmNewOrderAsync(Draft(product) with { PlannedFulfilmentDate = BusinessDate.AddDays(1) });

        Assert.IsTrue(sameDay.Succeeded);
        Assert.IsFalse(sameDay.CommittedOrder!.AdvanceOrderMarker);
        Assert.AreNotEqual(Guid.Empty, sameDay.CommittedOrder.Id);
        Assert.AreEqual(OrderSourceType.Pos, sameDay.CommittedOrder.SourceType);
        Assert.AreEqual(OrderStatus.Open, sameDay.CommittedOrder.Status);
        Assert.IsTrue(future.Succeeded);
        Assert.IsTrue(future.CommittedOrder!.AdvanceOrderMarker);
        Assert.AreNotEqual(sameDay.CommittedOrder.Id, future.CommittedOrder.Id);
    }

    [TestMethod]
    public async Task DispatcherReceivesThePersistedCommittedSnapshot()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product));

        Assert.IsTrue(result.Succeeded);
        Assert.IsNotNull(store.Snapshot);
        Assert.IsNotNull(dispatcher.LastOrder);
        Assert.AreEqual(store.Snapshot, dispatcher.LastOrder);
        Assert.AreEqual(result.CommittedOrder, dispatcher.LastOrder);
    }

    [TestMethod]
    public async Task HistoricalSnapshotWithNullPlannedTimeRemainsReadable()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingDispatcher();
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, dispatcher);

        var result = await service.ConfirmNewOrderAsync(Draft(product));
        Assert.IsTrue(result.Succeeded);
        await store.SaveAsync(result.CommittedOrder! with { PlannedFulfilmentTime = null });

        var reloaded = await service.GetOrderByIdAsync(result.CommittedOrder.Id);

        Assert.IsNotNull(reloaded);
        Assert.IsNull(reloaded!.PlannedFulfilmentTime);
    }

    [TestMethod]
    public async Task PlannedDateBrowserDelegatesTheReadOnlyDateQuery()
    {
        var product = Product(Guid.NewGuid(), Guid.Empty, optionsEnabled: false);
        var store = new RecordingOrderStore
        {
            BrowserRows = [new(Guid.NewGuid(), BusinessDate, new TimeOnly(18, 25), FulfilmentMode.Retrait, OrderStatus.Closed, Money.FromCents(1250), "06 12 34 56 78")]
        };
        using var service = CreateService(new FakeCatalogue(Entry(product)), store, new RecordingDispatcher());

        var rows = await service.ListOrdersByPlannedDateAsync(BusinessDate);

        Assert.HasCount(1, rows);
        Assert.AreEqual(BusinessDate, store.BrowserDate);
        Assert.AreEqual("06 12 34 56 78", rows[0].Telephone);
        Assert.AreEqual(OrderStatus.Closed, rows[0].Status);
    }

    private static OrderEntryService CreateService(
        IOrderEntryCatalogueQueries catalogue,
        IOrderStore store,
        IOrderPrintDispatcher dispatcher,
        BusinessSettings? settings = null) =>
        new(catalogue, new FakeSettingsStore(settings ?? BusinessSettings.Defaults(DateTimeOffset.UtcNow)), store, dispatcher, new DeterministicIds(), new FixedClock());

    private static NewOrderDraft Draft(ProductAggregate product, IReadOnlyList<Guid>? selected = null) =>
        new([new OrderLineDraft(Guid.Empty, product, selected ?? [], [], 1, "Plats")], FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), null, null, null, false);

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

    private sealed class FilterCatalogue(Guid categoryId) : IOrderEntryCatalogueQueries
    {
        private readonly IReadOnlyList<ProductSummary> products =
        [
            new(Guid.NewGuid(), "P-001", "Sushi saumon", categoryId, "Plats", Money.FromCents(1000), 10m, true, true, false),
            new(Guid.NewGuid(), "P-002", "Sushi thon", categoryId, "Plats", Money.FromCents(1100), 10m, false, true, false)
        ];
        public List<string?> Searches { get; } = [];
        public List<Guid?> CategoryIds { get; } = [];
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([new(categoryId, "Plats")]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default)
        {
            Searches.Add(search); CategoryIds.Add(filterCategoryId);
            var query = products.Where(product => product.IsActive && (!filterCategoryId.HasValue || product.CategoryId == filterCategoryId.Value));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(product => product.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<IReadOnlyList<ProductSummary>>(query.ToArray());
        }
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(null);
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
        public IReadOnlyList<OrderBrowserRow> BrowserRows { get; init; } = [];
        public DateOnly? BrowserDate { get; private set; }
        public virtual Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { SaveCalls++; Snapshot = snapshot; return Task.CompletedTask; }
        public virtual Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnReload) throw new InvalidOperationException("synthetic reload failure");
            return Task.FromResult(Snapshot);
        }
        public virtual Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) { BrowserDate = plannedDate; return Task.FromResult(BrowserRows); }
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
        public OrderSnapshot? LastOrder { get; private set; }
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) { Calls++; LastOrder = committedOrder; return Task.CompletedTask; }
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
