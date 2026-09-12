using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Authority;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M07PresentationRefreshTests
{
    [TestMethod]
    public void DatabaseReplacementRefreshesAllBusinessSurfacesBeforeReenablingWritesOnSta()
    {
        RunOnSta(() =>
        {
            var store = new MutablePresentationStore();
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var notifier = new NoOpNotifier();
            var catalogue = new CatalogueService(store, guard, notifier);
            var settings = new BusinessSettingsService(store, guard, notifier);
            var entryCatalogue = new OrderEntryCatalogueService(store);
            var clock = new FixedClock();
            using var entryService = new OrderEntryService(
                entryCatalogue, store, store, new NoOpPrinter(), new DeterministicIds(), clock, guard, notifier);
            using var lifecycleService = new OrderLifecycleService(
                store, new DeterministicIds(), clock, guard, notifier, entryCatalogue, store);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true, catalogue, settings, entryService, lifecycleService,
                guard, WriteAuthorityState.Authoritative);

            shell.Admin!.Categories.Add(store.OldCategory);
            shell.Admin.CategoryFilters.Add(store.OldCategory);
            shell.Admin.Products.Add(store.OldProduct);
            shell.Admin.SetSettingsValidationMessage(string.Empty);
            shell.Admin.DeliveryMinText = "1.00";
            shell.Entry!.Categories.Add(store.OldCategory);
            shell.Entry.Products.Add(store.OldProduct);
            shell.Lifecycle!.Orders.Add(new OrderManagementRowViewModel(store.OldRow));

            var refresh = shell.RefreshBusinessPresentationAfterDatabaseReplacementAsync();
            PumpUntil(store.CatalogueReadStarted.Task);
            Assert.IsFalse(shell.CanWrite, "The shell must remain fail-closed while the replacement database is being read.");
            Assert.IsFalse(shell.Admin.CanWrite);
            Assert.IsFalse(shell.Entry.CanWrite);
            Assert.IsFalse(shell.Lifecycle.CanWrite);

            store.ReleaseCatalogueRead.TrySetResult(null);
            PumpUntil(refresh);
            refresh.GetAwaiter().GetResult();

            Assert.IsTrue(shell.CanWrite);
            Assert.IsTrue(shell.Admin.CanWrite);
            Assert.IsTrue(shell.Entry.CanWrite);
            Assert.IsTrue(shell.Lifecycle.CanWrite);
            Assert.AreEqual(store.NewCategory.Id, shell.Admin.Categories.Single().Id);
            Assert.AreEqual(store.NewProduct.Id, shell.Admin.Products.Single().Id);
            Assert.AreEqual(store.NewProduct.Id, shell.Entry.Products.Single().Id);
            Assert.AreEqual(store.NewRow.Id, shell.Lifecycle.Orders.Single().Id);
            Assert.AreEqual("42.00", shell.Admin.DeliveryMinText);
            Assert.AreEqual(store.NewSummary.TurnoverTtc.Euros.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture), shell.Lifecycle.DashboardTurnoverText);
        });
    }

    [TestMethod]
    public void DatabaseReplacementRefreshFailureLeavesWritableSurfacesBlockedOnSta()
    {
        RunOnSta(() =>
        {
            var store = new MutablePresentationStore { ThrowOnCatalogueRead = true };
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var notifier = new NoOpNotifier();
            var catalogue = new CatalogueService(store, guard, notifier);
            var settings = new BusinessSettingsService(store, guard, notifier);
            var entryCatalogue = new OrderEntryCatalogueService(store);
            var clock = new FixedClock();
            using var entryService = new OrderEntryService(
                entryCatalogue, store, store, new NoOpPrinter(), new DeterministicIds(), clock, guard, notifier);
            using var lifecycleService = new OrderLifecycleService(
                store, new DeterministicIds(), clock, guard, notifier, entryCatalogue, store);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true, catalogue, settings, entryService, lifecycleService,
                guard, WriteAuthorityState.Authoritative);

            var refresh = shell.RefreshBusinessPresentationAfterDatabaseReplacementAsync();
            PumpUntil(refresh);
            try
            {
                refresh.GetAwaiter().GetResult();
                Assert.Fail("The replacement refresh should report the synthetic read failure.");
            }
            catch (IOException)
            {
            }
            Assert.IsFalse(shell.CanWrite);
            Assert.IsFalse(shell.Admin!.CanWrite);
            Assert.IsFalse(shell.Entry!.CanWrite);
            Assert.IsFalse(shell.Lifecycle!.CanWrite);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntil(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        Assert.IsTrue(task.IsCompleted, "The refresh did not complete within the bounded STA test window.");
    }

    private sealed class MutablePresentationStore : ICatalogueStore, IBusinessSettingsStore, IOrderLifecycleStore, IOrderStore
    {
        private readonly TaskCompletionSource<object?> releaseCatalogue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Guid newCategoryId = Guid.NewGuid();
        private int catalogueReads;

        public CategorySummary OldCategory { get; } = new(Guid.NewGuid(), "Ancienne catégorie");
        public CategorySummary NewCategory { get; } = new(Guid.Empty, "Nouvelle catégorie");
        public ProductSummary OldProduct { get; } = new(Guid.NewGuid(), "OLD", "Ancien produit", Guid.NewGuid(), "Ancienne catégorie", Money.FromEuros(1), 10m, true, false, false);
        public ProductSummary NewProduct { get; } = new(Guid.NewGuid(), "NEW", "Nouveau produit", Guid.Empty, "Nouvelle catégorie", Money.FromEuros(2), 10m, true, false, false);
        public OrderBrowserRow OldRow { get; } = new(Guid.NewGuid(), new DateOnly(2026, 9, 10), new TimeOnly(12, 0), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromEuros(1), null);
        public OrderBrowserRow NewRow { get; } = new(Guid.NewGuid(), new DateOnly(2026, 9, 10), new TimeOnly(13, 0), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromEuros(2), null);
        public TaskCompletionSource<object?> CatalogueReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ReleaseCatalogueRead => releaseCatalogue;
        public bool ThrowOnCatalogueRead { get; init; }
        public BusinessSettings NewSettings { get; } = BusinessSettings.Defaults(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)) with { DeliveryMinMerchandiseTotalTtc = Money.FromEuros(42) };
        public OrderOperationalSummary NewSummary { get; } = new(Money.FromEuros(123), Money.FromEuros(100), Money.FromEuros(60), Money.FromEuros(40), 1, 2, 3);

        public MutablePresentationStore()
        {
            NewCategory = new(newCategoryId, "Nouvelle catégorie");
            NewProduct = NewProduct with { CategoryId = NewCategory.Id };
        }

        public async Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref catalogueReads) == 1)
            {
                CatalogueReadStarted.TrySetResult(null);
                if (ThrowOnCatalogueRead) throw new IOException("synthetic replacement refresh failure");
                await releaseCatalogue.Task.WaitAsync(cancellationToken);
            }
            return [NewCategory];
        }

        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>([NewProduct]);

        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductDraft?>(new(NewProduct.Id, NewProduct.Code, NewProduct.Name, NewProduct.CategoryId, NewProduct.PriceTtc, NewProduct.VatRate, NewProduct.IsActive, NewProduct.DiscountEligible, NewProduct.OptionsEnabled, []));

        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(NewCategory));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(NewCategory));
        public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(NewCategory));
        public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid categoryId, string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(NewCategory));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(NewProduct.Id));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 0)));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());

        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(NewSettings);
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([NewRow]);
        public Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(NewSummary);
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([NewRow]);
    }

    private sealed class NoOpNotifier : IDurableChangeNotifier
    {
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpPrinter : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 10);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }
}
