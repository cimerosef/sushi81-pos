using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M05DesktopTests
{
    [TestMethod]
    public void CommandesEditingUsesHistoricalDatePaymentDateAndLivePaymentFeedbackOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 30));
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero });
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new EmptyCatalogueStore()),
                new BusinessSettingsService(settings),
                orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();
                Assert.IsTrue(lifecycle.IsEditing);
                Assert.AreEqual(FulfilmentMode.Retrait, lifecycle.EditFulfilment);
                Assert.IsTrue(lifecycle.IsEditPickupDiscountEnabled);
                var commandes = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Commandes"]);
                commandes.IsSelected = true;
                window.UpdateLayout();
                Assert.IsTrue(commandes.IsEnabled, $"window={window.IsEnabled}, tab={commandes.IsEnabled}, selected={commandes.IsSelected}");
                Assert.AreEqual(FulfilmentMode.Retrait, lifecycle.EditFulfilment);

                var planned = (DatePicker)typeof(MainWindow).GetField("lifecycleEditPlannedDatePicker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var effective = (DatePicker)typeof(MainWindow).GetField("lifecycleEffectivePaymentDatePicker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var card = (TextBox)typeof(MainWindow).GetField("lifecycleEditCardBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var cash = (TextBox)typeof(MainWindow).GetField("lifecycleEditCashBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                Assert.IsTrue(card.IsEnabled, $"tab={commandes.IsEnabled}, selected={commandes.IsSelected}");

                Assert.AreEqual(order.PlannedFulfilmentDate.ToDateTime(TimeOnly.MinValue), planned.SelectedDate);
                Assert.AreEqual(order.PlannedFulfilmentDate, DateOnly.FromDateTime(planned.DisplayDateStart!.Value));
                Assert.AreEqual(new DateTime(2026, 8, 31), effective.SelectedDate);
                var discount = VisualDescendants<CheckBox>(window).Single(check => check.Content?.ToString() == shell.Localized["PickupDiscountRequest"]);
                Assert.IsTrue(discount.IsEnabled, $"vm={lifecycle.IsEditPickupDiscountEnabled}; data={discount.DataContext?.GetType().Name}; binding={discount.GetBindingExpression(CheckBox.IsEnabledProperty)?.Status}");

                effective.SelectedDate = new DateTime(2026, 8, 29);
                effective.GetBindingExpression(DatePicker.SelectedDateProperty)!.UpdateSource();
                lifecycle.EditTelephone = "06 12 34 56 78";
                Assert.AreEqual(new DateTime(2026, 8, 29), lifecycle.EffectivePaymentDate);
                card.Text = "4"; card.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.AreEqual("4", lifecycle.EditCard, $"card enabled={card.IsEnabled}");
                cash.Text = "6"; cash.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.AreEqual("6", lifecycle.EditCash, $"cash enabled={cash.IsEnabled}");
                window.UpdateLayout();
                Assert.AreEqual(10m.ToString("0.00", CultureInfo.CurrentCulture), lifecycle.EditPaidText);
                Assert.AreEqual(0m.ToString("0.00", CultureInfo.CurrentCulture), lifecycle.EditDifferenceText);
                StringAssert.Contains(lifecycle.EditCloseEligibilityText, shell.Localized["OrderCloseEligible"]);

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                Assert.AreEqual(new DateTime(2026, 8, 29), lifecycle.EffectivePaymentDate);
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                Assert.AreEqual(new DateTime(2026, 8, 29), lifecycle.EffectivePaymentDate);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void MainWindowUsesRealCommandesSelectionAbandonReuseAndDashboardRoutesOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 9, 2));
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            var ids = new DeterministicIds();
            using var lifecycleService = new OrderLifecycleService(store, ids, new FixedClock(), settings: settings);
            using var entryService = new OrderEntryService(new OrderEntryCatalogueService(new EmptyCatalogueStore()), settings, store, new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new EmptyCatalogueStore()),
                new BusinessSettingsService(settings), entryService, lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                var entry = shell.Entry!;
                var mainTabs = Field<TabControl>(window, "mainTabs");
                var commandes = mainTabs.Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                var caisse = mainTabs.Items.OfType<TabItem>().Single(item => item.DataContext is OrderEntryShellViewModel);
                commandes.IsSelected = true;
                window.UpdateLayout();

                var grid = Field<DataGrid>(window, "commandesGrid");
                Assert.HasCount(1, grid.Items);
                grid.SelectedIndex = 0;
                grid.GetBindingExpression(DataGrid.SelectedItemProperty)?.UpdateSource();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert.AreEqual(order.Id, lifecycle.SelectedOrder!.Id);

                var modify = VisualDescendants<Button>(window).Single(button => Equals(button.Content, shell.Localized["OrderModify"]));
                modify.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(lifecycle.IsEditing);
                lifecycle.EditTelephone = "0699999999";
                var abandon = VisualDescendants<Button>(window).Single(button => Equals(button.Content, shell.Localized["OrderAbandon"]));
                abandon.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsFalse(lifecycle.IsEditing);
                Assert.AreEqual(order.Telephone, lifecycle.EditTelephone);

                var reuse = VisualDescendants<Button>(window).Single(button => Equals(button.Content, shell.Localized["OrderNewFromDetails"]));
                reuse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsTrue(caisse.IsSelected);
                Assert.AreEqual(order.Telephone, entry.Telephone);
                Assert.AreEqual(order.DeliveryAddress, entry.DeliveryAddress);
                Assert.AreEqual(order.Comment, entry.Comment);
                Assert.IsEmpty(entry.Cart);
                Assert.IsNull(entry.SelectedFulfilment);
                Assert.IsNull(entry.PlannedTime);

                entry.Comment = "unrelated draft survives tab navigation";
                commandes.IsSelected = true;
                caisse.IsSelected = true;
                Assert.AreEqual("unrelated draft survives tab navigation", entry.Comment);

                var futureButton = Field<Button>(window, "dashboardFutureButton");
                futureButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                Assert.IsTrue(commandes.IsSelected);
                Assert.AreEqual(new DateTime(2026, 9, 1), lifecycle.BrowseDate);
                Assert.HasCount(1, lifecycle.Orders);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public async Task LiveSearchAndDateRefreshCannotLetAnOlderLifecycleQueryOverwriteTheLatest()
    {
        var oldRow = Row(Guid.Parse("61000000-0000-0000-0000-000000000001"), "old-result", new DateOnly(2026, 8, 30));
        var newRow = Row(Guid.Parse("61000000-0000-0000-0000-000000000002"), "new-result", new DateOnly(2026, 9, 1));
        var store = new BlockingLifecycleStore(oldRow, newRow);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock());
        using var viewModel = new OrderLifecycleShellViewModel(service);

        viewModel.SearchText = "old";
        var stale = viewModel.RefreshAsync();
        await store.OldSearchStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        viewModel.SearchText = "new";
        var latest = viewModel.RefreshAsync();
        await latest;
        store.ReleaseOldSearch();
        await stale;

        Assert.HasCount(1, viewModel.Orders);
        Assert.AreEqual(newRow.Reference, viewModel.Orders[0].ReferenceText);
        Assert.AreEqual("new", viewModel.SearchText);
    }

    [TestMethod]
    public void LifecycleOperationsDoNotOwnAutomaticPrintingAndCloseEligibilityIsExactOnSta()
    {
        Assert.IsFalse(typeof(OrderLifecycleService).GetConstructors().Single().GetParameters().Any(parameter => parameter.ParameterType == typeof(IOrderPrintDispatcher)));
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 30));
            var store = new LifecycleStore(order);
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow))), orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                Assert.IsFalse(lifecycle.CanClose);
                lifecycle.BeginModification();
                lifecycle.EditCard = "10";
                lifecycle.EditCash = "0";
                lifecycle.EditTotal = "10";
                Assert.IsTrue(lifecycle.EditCloseEligibilityText.Contains(shell.Localized["OrderCloseEligible"], StringComparison.Ordinal));
                Assert.IsTrue(lifecycle.CanSave);
                lifecycle.AbandonModification();
                Assert.IsFalse(lifecycle.CanClose);
            }
            finally { window.Close(); }
        });
    }

    private static OrderSnapshot Snapshot(DateOnly plannedDate) => new(
        Guid.NewGuid(), OrderSourceType.Pos, OrderStatus.Open,
        new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero),
        null, null, FulfilmentMode.Retrait, plannedDate, new TimeOnly(11, 0), false,
        "06 00 00 00 00", "12 rue des Tests", "synthetic", Money.FromCents(1000), false, false, null, Money.Zero,
        [new(Guid.NewGuid(), 0, Guid.NewGuid(), "P", "Plat", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])], [])
    { Reference = "20260830-001" };

    private static OrderBrowserRow Row(Guid id, string reference, DateOnly plannedDate) => new(id, plannedDate, new TimeOnly(11, 0), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromCents(1000), "06 00 00 00 00") { Reference = reference };

    private static T Field<T>(MainWindow window, string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        var count = 0;
        try { count = VisualTreeHelper.GetChildrenCount(root); } catch (InvalidOperationException) { yield break; }
        for (var index = 0; index < count; index++)
            foreach (var child in VisualDescendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class LifecycleStore(OrderSnapshot initial) : IOrderStore, IOrderLifecycleStore
    {
        public OrderSnapshot Snapshot { get; private set; } = initial;
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { Snapshot = snapshot; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(Snapshot.Id == orderId ? Snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([Row(Snapshot)]);
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) { Snapshot = snapshot; return Task.CompletedTask; }
        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([Row(Snapshot)]);
        public Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(new OrderOperationalSummary(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0));
        private static OrderBrowserRow Row(OrderSnapshot value) => new(value.Id, value.PlannedFulfilmentDate, value.PlannedFulfilmentTime, value.Fulfilment, value.Status, value.TotalTtc, value.Telephone) { Reference = value.Reference };
    }

    private sealed class BlockingLifecycleStore(OrderBrowserRow oldRow, OrderBrowserRow newRow) : IOrderStore, IOrderLifecycleStore
    {
        private readonly TaskCompletionSource<IReadOnlyList<OrderBrowserRow>> oldSearch = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> OldSearchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default)
        {
            if (string.Equals(query, "old", StringComparison.Ordinal))
            {
                OldSearchStarted.TrySetResult(true);
                return oldSearch.Task;
            }
            return Task.FromResult<IReadOnlyList<OrderBrowserRow>>([newRow]);
        }
        public Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(new OrderOperationalSummary(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0));
        public void ReleaseOldSearch() => oldSearch.TrySetResult([oldRow]);
    }

    private sealed class NoopDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SettingsStore(BusinessSettings current) : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(current);
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class EmptyCatalogueStore : ICatalogueStore
    {
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name, shortCode)));
        public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid categoryId, string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name, shortCode)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(draft.Id == Guid.Empty ? Guid.NewGuid() : draft.Id));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 0)));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 8, 31);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int counter;
        public Guid NewId() => Guid.Parse($"30000000-0000-0000-0000-{Interlocked.Increment(ref counter):D12}");
    }
}
