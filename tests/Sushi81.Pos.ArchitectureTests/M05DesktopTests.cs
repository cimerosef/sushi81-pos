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
    public async Task ApplicationSearchFallbackUsesTheSamePartialPhoneAndCommentSemantics()
    {
        var row = new OrderBrowserRow(Guid.NewGuid(), new DateOnly(2026, 8, 31), new TimeOnly(11, 0), FulfilmentMode.Retrait, OrderStatus.Cancelled, Money.FromCents(1000), "06 12 34 56 78")
        {
            Reference = "20260831-123",
            Comment = "customer requested call-back",
            DeliveryAddress = "12 rue des Tests"
        };
        using var service = new OrderLifecycleService(new FallbackStore(row), new DeterministicIds(), new FixedClock());

        Assert.AreEqual(row.Id, (await service.SearchLiveAsync("31-123")).Single().Id);
        Assert.AreEqual(row.Id, (await service.SearchLiveAsync("6123")).Single().Id);
        Assert.AreEqual(row.Id, (await service.SearchLiveAsync("+33 6 12")).Single().Id);
        Assert.AreEqual(row.Id, (await service.SearchLiveAsync("call-back")).Single().Id);
    }

    [TestMethod]
    public void MainWindowCommandesUsesActualTopListBottomDetailLayoutAndCaisseKeepsOnlyOperationalEntryOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 9, 2));
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var lifecycleService = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var entryService = new OrderEntryService(new OrderEntryCatalogueService(new EmptyCatalogueStore()), settings, store, new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), entryService, lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var mainTabs = Field<TabControl>(window, "mainTabs");
                var commandesTab = mainTabs.Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                var caisseTab = mainTabs.Items.OfType<TabItem>().Single(item => item.DataContext is OrderEntryShellViewModel);
                commandesTab.IsSelected = true;
                window.UpdateLayout();

                var grid = Field<DataGrid>(window, "commandesGrid");
                Assert.HasCount(9, grid.Columns);
                CollectionAssert.AreEqual(
                    new[] { shell.Localized["OrderReference"], shell.Localized["PlannedDate"], shell.Localized["PlannedTime"], shell.Localized["OrderStatus"], shell.Localized["TotalTtc"], shell.Localized["Telephone"], shell.Localized["Fulfilment"], shell.Localized["Comment"], shell.Localized["DeliveryAddress"] },
                    grid.Columns.Select(column => column.Header?.ToString()).ToArray());
                Assert.AreEqual(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(grid));
                Assert.AreEqual(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(grid));
                Assert.IsTrue(grid.ActualWidth > 0 && grid.ActualHeight > 0);
                Assert.IsFalse(grid.Columns.Any(column => column.Header?.ToString() is "CB" or "Espèce" or "Paid" or "Difference"));

                var row = (OrderManagementRowViewModel)grid.Items[0];
                Assert.AreEqual(order.Comment, row.CommentText);
                Assert.AreEqual(order.DeliveryAddress, row.AddressText);
                Assert.AreEqual(order.Fulfilment, row.Row.Fulfilment);
                Assert.IsTrue(VisualDescendants<ScrollViewer>(window).Any(viewer => viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && viewer.Content is StackPanel), "The lower selected-order detail must have its own vertical scroll path.");

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                Assert.AreEqual("备注", grid.Columns[7].Header?.ToString());
                Assert.AreEqual("地址", grid.Columns[8].Header?.ToString());
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();

                window.Width = 760;
                window.Height = 520;
                window.UpdateLayout();
                Assert.IsTrue(grid.ActualWidth > 0 && grid.ActualHeight > 0, "The Commandes list must remain usable at the supported minimum window size.");
                window.Width = 1280;
                window.Height = 900;
                window.UpdateLayout();
                Assert.IsTrue(grid.ActualWidth > 0 && grid.ActualHeight > 0, "The Commandes list must remain usable at a larger window size.");

                var cartProduct = new OrderEntryProduct(new ProductAggregate(
                    new Product(Guid.NewGuid(), "P-CART", "Panier test", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
                for (var index = 0; index < 12; index++) shell.Entry!.AddConfiguredLine(cartProduct, [], [], 1);
                caisseTab.IsSelected = true;
                window.UpdateLayout();
                var cart = Field<ListBox>(window, "orderCartList");
                Assert.IsGreaterThan(230D, cart.ActualHeight, "The Caisse cart must be materially taller than the removed compact browser area.");
                Assert.AreEqual(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(cart));
                Assert.IsTrue(VisualDescendants<ScrollViewer>(cart).Any(viewer => viewer.ScrollableHeight > 0), "Long carts must scroll inside the cart control.");
                Assert.IsGreaterThan(0D, Field<TextBox>(window, "orderDeliveryAddressBox").ActualWidth);
                Assert.IsGreaterThan(0D, Field<DatePicker>(window, "orderPlannedDatePicker").ActualWidth);
                Assert.IsGreaterThan(0D, Field<Button>(window, "orderAddButton").ActualWidth);
                Assert.IsNotNull(Field<Button>(window, "dashboardFutureButton"));
                Assert.IsFalse(VisualDescendants<FrameworkElement>(window).Any(element => element.Name is "orderSavedGroup" or "orderReloadIdBox" or "orderReloadedDisplay" or "orderBrowserGroup" or "orderBrowserGrid" or "orderBrowserDatePicker"), "Caisse must not retain the duplicated saved-order/reload browser controls.");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void MainWindowCaisseLanguageRefreshAndOptionalPlannedTimeRemainCorrectOnSta()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(Guid.NewGuid(), "P-OPTIONAL", "Plat optionnel", categoryId, Money.FromCents(1000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { DeliveryMinMerchandiseTotalTtc = Money.Zero });
            var store = new LifecycleStore(Snapshot(new DateOnly(2026, 8, 31)));
            using var entryService = new OrderEntryService(new SingleEntryCatalogue(product), settings, store, new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new EmptyCatalogueStore()),
                new BusinessSettingsService(settings), entryService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 680 };
            window.Show();
            try
            {
                var entry = shell.Entry!;
                var caisse = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderEntryShellViewModel);
                caisse.IsSelected = true;
                entry.AddConfiguredLine(product, [], [], 2);
                entry.Telephone = "06 12 34 56 78";
                entry.DeliveryAddress = "12 rue de la Paix";
                entry.Comment = "sans traduction";
                entry.RepriceAsync(clearManualOverride: true).GetAwaiter().GetResult();
                window.UpdateLayout();

                Assert.IsNull(entry.SelectedFulfilment);
                Assert.IsNull(entry.PlannedTime);
                StringAssert.Contains(entry.ValidationMessage, shell.Localized["ValidationFulfilmentRequired"]);
                Assert.IsTrue(VisualDescendants<TextBlock>(window).Any(text => text.Text == entry.ValidationMessage && text.Foreground == System.Windows.Media.Brushes.Firebrick));

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.AreEqual(shell.Localized["ValidationFulfilmentRequired"], entry.ValidationMessage);
                Assert.IsFalse(entry.ValidationMessage.Contains("Le mode de commande", StringComparison.Ordinal));
                Assert.AreEqual(2, entry.Cart.Single().Quantity);
                Assert.AreEqual("06 12 34 56 78", entry.Telephone);
                Assert.AreEqual("12 rue de la Paix", entry.DeliveryAddress);
                Assert.AreEqual("sans traduction", entry.Comment);
                Assert.IsNull(entry.PlannedTime);

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                Assert.AreEqual(shell.Localized["ValidationFulfilmentRequired"], entry.ValidationMessage);

                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                entry.RepriceAsync(clearManualOverride: true).GetAwaiter().GetResult();
                Assert.IsTrue(entry.CanConfirm, "A valid date and fulfilment mode must be enough when the planned time is fully unset.");
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                Assert.IsNull(entry.PlannedTime, "Language changes must not silently select a planned time.");

                entry.SelectedPlannedHour = 18;
                Assert.IsFalse(entry.PlannedTimeValid, "A half-selected planned time must remain invalid.");
                Assert.IsFalse(entry.CanConfirm);
                var partial = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsFalse(partial!.Succeeded);
                Assert.AreEqual(ValidationCodes.PlannedTimeInvalid, partial.Issues.Single().StableCode);
                entry.SelectedPlannedHour = null;
                Assert.IsNull(entry.SelectedPlannedMinute);
                Assert.IsTrue(entry.CanConfirm, "A fully unset planned time must remain valid after a language change.");

                var withoutTime = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(withoutTime!.Succeeded, string.Join(";", withoutTime.Issues.Select(issue => issue.Message)));
                Assert.IsNull(withoutTime.CommittedOrder!.PlannedFulfilmentTime);
                Assert.IsNull(store.Snapshot.PlannedFulfilmentTime);

                entry.StartNewOrder();
                entry.AddConfiguredLine(product, [], [], 1);
                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                entry.SelectedPlannedHour = 18;
                entry.SelectedPlannedMinute = 25;
                entry.RepriceAsync(clearManualOverride: true).GetAwaiter().GetResult();
                var withTime = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(withTime!.Succeeded, string.Join(";", withTime.Issues.Select(issue => issue.Message)));
                Assert.AreEqual(new TimeOnly(18, 25), withTime.CommittedOrder!.PlannedFulfilmentTime);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void MainWindowCommandesUsesLongTextWidthWithoutAddingColumnsOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 31)) with
            {
                Comment = new string('c', 80),
                DeliveryAddress = new string('a', 80)
            };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var lifecycleService = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new EmptyCatalogueStore()),
                new BusinessSettingsService(settings), orderLifecycleService: lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 680 };
            window.Show();
            try
            {
                var commandes = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                commandes.IsSelected = true;
                var grid = Field<DataGrid>(window, "commandesGrid");
                window.UpdateLayout();
                Assert.HasCount(9, grid.Columns);
                Assert.AreEqual(DataGridLengthUnitType.Star, grid.Columns[7].Width.UnitType);
                Assert.AreEqual(DataGridLengthUnitType.Star, grid.Columns[8].Width.UnitType);
                Assert.IsGreaterThanOrEqualTo(180D, grid.Columns[7].MinWidth);
                Assert.IsGreaterThanOrEqualTo(180D, grid.Columns[8].MinWidth);

                window.Width = 1280;
                window.Height = 900;
                window.UpdateLayout();
                var wideScroll = VisualDescendants<ScrollViewer>(grid).First(viewer => viewer.ViewportWidth > 0);
                if (window.ActualWidth >= 1100D)
                {
                    Assert.IsGreaterThan(180D, grid.Columns[7].ActualWidth, "Commentaire must expand when a wide viewport is available.");
                    Assert.IsGreaterThan(180D, grid.Columns[8].ActualWidth, "Adresse must expand when a wide viewport is available.");
                    Assert.IsLessThanOrEqualTo(1D, Math.Max(0D, wideScroll.ViewportWidth - wideScroll.ExtentWidth), "Wide layout must not leave a filler region after Adresse.");
                }
                else
                {
                    Assert.IsGreaterThan(0D, wideScroll.ScrollableWidth, "A host-limited wide window must preserve horizontal scrolling.");
                    Assert.IsGreaterThanOrEqualTo(180D, grid.Columns[7].ActualWidth);
                    Assert.IsGreaterThanOrEqualTo(180D, grid.Columns[8].ActualWidth);
                }

                window.Width = 760;
                window.Height = 520;
                window.UpdateLayout();
                var narrowScroll = VisualDescendants<ScrollViewer>(grid).First(viewer => viewer.ViewportWidth > 0);
                Assert.IsGreaterThan(0D, narrowScroll.ScrollableWidth, "Narrow supported sizes must keep horizontal scrolling.");
                Assert.IsGreaterThanOrEqualTo(180D, grid.Columns[7].ActualWidth);
                Assert.IsGreaterThanOrEqualTo(180D, grid.Columns[8].ActualWidth);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void ExistingOrderQuantitySaveKeepsTheVisibleHistoricalPriceBreakdownInParityWithPersistenceOnSta()
    {
        RunOnSta(() =>
        {
            var historicalAdjustment = new OrderLineAdjustmentSnapshot(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Ajustement historique", Money.FromCents(-68), 10m);
            var first = new OrderItemSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), "TST002", "Produit 12", "Tests", Money.FromCents(1200), 10m, true, 1, Money.FromCents(1200), Money.FromCents(1019), [historicalAdjustment]);
            var second = new OrderItemSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), "TST001A", "Produit 8.50", "Tests", Money.FromCents(850), 10m, true, 1, Money.FromCents(850), Money.FromCents(765), []);
            var order = Snapshot(new DateOnly(2026, 8, 31)) with
            {
                Items = [first, second],
                TotalTtc = Money.FromCents(1784),
                PickupDiscountApplied = true,
                PickupDiscountRate = 0.10m
            };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero });
            using var lifecycleService = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel).IsSelected = true;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                var detailLines = Field<ListBox>(window, "lifecycleDetailLinesList");
                window.UpdateLayout();

                Assert.HasCount(2, detailLines.Items, "A selected saved order must expose its historical lines before modification begins.");
                StringAssert.Contains(lifecycle.DetailLines[0].PriceBreakdownText, "× 1 =");
                StringAssert.Contains(lifecycle.DetailLines[0].PriceBreakdownText, Money.FromCents(1200).Euros.ToString("0.00", CultureInfo.CurrentCulture));
                StringAssert.Contains(lifecycle.DetailLines[0].OptionsText, Money.FromCents(-68).Euros.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) + " €/unité");
                StringAssert.Contains(lifecycle.PickupDiscountText, "10%");

                lifecycle.BeginModification();
                lifecycle.DetailLines.Single(line => line.Item.ProductCode == "TST001A").Quantity = 2;
                lifecycle.SaveModificationAsync().GetAwaiter().GetResult();
                window.UpdateLayout();

                Assert.IsFalse(lifecycle.IsEditing);
                Assert.AreEqual(2549L, store.Snapshot.TotalTtc.Cents);
                Assert.AreEqual(store.Snapshot.TotalTtc, lifecycle.SelectedOrder!.TotalTtc);
                Assert.AreEqual(Money.FromCents(2549).Euros.ToString("0.00", CultureInfo.CurrentCulture), lifecycle.TotalText);
                Assert.AreEqual(Money.FromCents(2549).Euros.ToString("0.00", CultureInfo.CurrentCulture), Field<TextBox>(window, "lifecycleEditTotalBox").Text);
                Assert.HasCount(2, detailLines.Items, "The saved selected-order detail must continue to expose the persisted line components.");
                StringAssert.Contains(lifecycle.DetailLines.Single(line => line.Item.ProductCode == "TST002").OptionsText, Money.FromCents(-68).Euros.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture) + " €/unité");
                CollectionAssert.Contains(VisualDescendants<TextBlock>(detailLines).Select(text => text.Text).ToArray(), lifecycle.DetailLines.Single(line => line.Item.ProductCode == "TST002").OptionsText);

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                StringAssert.Contains(lifecycle.DetailLines.Single(line => line.Item.ProductCode == "TST002").OptionsText, "/件");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void ExistingLineQuantityUsesItsPersistedUnitPriceRatherThanAPreviouslyDerivedExtendedValue()
    {
        var historical = new OrderItemSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), "HIST", "Historique", "Tests", Money.FromCents(1200), 10m, true, 2, Money.FromCents(2399), Money.FromCents(2399), []);
        var line = new OrderDetailLineViewModel(historical) { Quantity = 3 };

        var snapshot = line.ToSnapshot();

        Assert.AreEqual(1200L, snapshot.ProductBasePriceTtc.Cents);
        Assert.AreEqual(3600L, snapshot.ExtendedBaseTtc.Cents);
        Assert.AreEqual(3600L, snapshot.CalculatedLineTotalTtc.Cents);
    }

    [TestMethod]
    public void ExistingOrderHalfCentRepriceKeepsVisibleAndPersistedTotalsOnSta()
    {
        RunOnSta(() =>
        {
            var firstAdjustments = new[]
            {
                new OrderLineAdjustmentSnapshot(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Sans accompagnement", Money.FromCents(-100), 10m),
                new OrderLineAdjustmentSnapshot(Guid.NewGuid(), 1, OrderAdjustmentKind.CustomAdjustment, null, null, "Sauce premium", Money.FromCents(100), 5.5m)
            };
            var first = new OrderItemSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), "TST002", "Produit 12", "Tests", Money.FromCents(1200), 10m, true, 1, Money.FromCents(1200), Money.FromCents(1062), firstAdjustments);
            var second = new OrderItemSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), "TST001A", "Produit 8.50", "Tests", Money.FromCents(850), 10m, true, 2, Money.FromCents(1700), Money.FromCents(1487), []);
            var order = Snapshot(new DateOnly(2026, 8, 31)) with
            {
                Items = [first, second],
                TotalTtc = Money.FromCents(2549),
                ManualTotalOverrideActive = true,
                PickupDiscountApplied = true,
                PickupDiscountRate = 0.125m
            };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountRate = 0.125m, PickupDiscountMinTotalTtc = Money.Zero });
            using var lifecycleService = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();
                lifecycle.DetailLines.Single(line => line.Item.ProductCode == "TST001A").Quantity = 1;
                lifecycle.DetailLines.Single(line => line.Item.ProductCode == "TST001A").Quantity = 2;
                lifecycle.SaveModificationAsync().GetAwaiter().GetResult();
                window.UpdateLayout();

                Assert.AreEqual(2551L, store.Snapshot.TotalTtc.Cents);
                Assert.AreEqual(Money.FromCents(2551).Euros.ToString("0.00", CultureInfo.CurrentCulture), lifecycle.TotalText);
                Assert.AreEqual(Money.FromCents(2551).Euros.ToString("0.00", CultureInfo.CurrentCulture), Field<TextBox>(window, "lifecycleEditTotalBox").Text);
                Assert.IsFalse(store.Snapshot.ManualTotalOverrideActive);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CommandesSelectedOrderLineActionsShareOneRightAlignedColumnOnSta()
    {
        RunOnSta(() =>
        {
            var shortLine = new OrderItemSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), "SHORT", "Court", "Tests", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), []);
            var longLine = new OrderItemSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), "LONG", new string('L', 80), "Tests", Money.FromCents(1200), 10m, true, 1, Money.FromCents(1200), Money.FromCents(1200), [
                new(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, new string('O', 120), Money.FromCents(-100), 10m)
            ]);
            var order = Snapshot(new DateOnly(2026, 8, 31)) with { Items = [shortLine, longLine], TotalTtc = Money.FromCents(2200) };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var lifecycleService = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                var detail = Field<ListBox>(window, "lifecycleDetailLinesList");
                var commandes = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                commandes.IsSelected = true;
                AssertAligned("normal");

                window.Width = 760;
                window.Height = 520;
                AssertAligned("small");
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                AssertAligned("zh-CN");
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                AssertAligned("fr-FR");
                Assert.HasCount(2, lifecycle.DetailLines, "Language refresh must preserve selected-order lines.");

                void AssertAligned(string size)
                {
                    window.UpdateLayout();
                    var containers = detail.Items.Cast<object>().Select(item => detail.ItemContainerGenerator.ContainerFromItem(item)).OfType<ListBoxItem>().ToArray();
                    Assert.HasCount(2, containers, $"{size}: both selected-order rows must be realized.");
                    var quantities = containers.Select(container => VisualDescendants<TextBox>(container).ToArray()).ToArray();
                    var buttons = containers.Select(container => VisualDescendants<Button>(container).ToArray()).ToArray();
                    var leftPanels = containers.Select(container => VisualDescendants<StackPanel>(container).First()).ToArray();
                    Assert.IsTrue(quantities.All(row => row.Length == 0), $"{size}: selected-order rows must not contain an inline quantity TextBox.");
                    Assert.IsTrue(buttons.All(row => row.Length == 2), $"{size}: each selected-order row must contain exactly edit and delete buttons.");
                    var editLefts = buttons.Select(row => LeftEdge(row[0], window)).ToArray();
                    var deleteLefts = buttons.Select(row => LeftEdge(row[1], window)).ToArray();
                    var deleteRights = buttons.Select(row => RightEdge(row[1], window)).ToArray();
                    var detailRight = RightEdge(detail, window);
                    Assert.IsLessThanOrEqualTo(1D, editLefts.Max() - editLefts.Min(), $"{size}: edit buttons must align vertically.");
                    Assert.IsLessThanOrEqualTo(1D, deleteLefts.Max() - deleteLefts.Min(), $"{size}: delete buttons must align vertically.");
                    Assert.IsLessThanOrEqualTo(1D, deleteRights.Max() - deleteRights.Min(), $"{size}: actions must share the same right edge.");
                    Assert.IsTrue(deleteRights.All(edge => edge <= detailRight + 1D), $"{size}: action area must stay inside detail width.");
                    Assert.IsTrue(leftPanels.All(panel => RightEdge(panel, window) <= editLefts.Min() + 1D), $"{size}: long text must not overlap the action area.");
                    StringAssert.Contains(lifecycle.DetailLines[1].PriceBreakdownText, "× 1 =");
                }

                static double LeftEdge(FrameworkElement element, Window window) => element.TransformToAncestor(window).Transform(new Point(0, 0)).X;
                static double RightEdge(FrameworkElement element, Window window) => element.TransformToAncestor(window).Transform(new Point(element.ActualWidth, 0)).X;
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void ExistingOrderPencilReplacementPathPersistsQuantityThroughLifecycleServiceOnSta()
    {
        RunOnSta(() =>
        {
            var productId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "EDIT", "Produit éditable", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, false, default, default),
                [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Tests");
            var item = new OrderItemSnapshot(Guid.NewGuid(), 0, productId, "EDIT", "Produit éditable", "Tests", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), []);
            var order = Snapshot(new DateOnly(2026, 8, 31)) with { Items = [item], TotalTtc = Money.FromCents(1000) };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            var catalogue = new SingleEntryCatalogue(product);
            using var lifecycleService = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), catalogue, settings);
            using var entryService = new OrderEntryService(catalogue, settings, store, new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), entryService, lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();
                var line = lifecycle.DetailLines.Single();
                var currentProduct = shell.Entry!.GetActiveProductForEditAsync(productId).GetAwaiter().GetResult()!;
                lifecycle.ReplaceLineAsync(line, new OrderLineDraft(line.Item.Id, currentProduct.Aggregate, [], [], 3, currentProduct.CategoryName)).GetAwaiter().GetResult();
                Assert.AreEqual(3, lifecycle.DetailLines.Single().Quantity);
                Assert.IsTrue(lifecycle.CanSave);
                lifecycle.SaveModificationAsync().GetAwaiter().GetResult();

                Assert.AreEqual(3, store.Snapshot.Items.Single().Quantity);
                Assert.AreEqual(3000L, store.Snapshot.TotalTtc.Cents);
                StringAssert.Contains(lifecycle.DetailLines.Single().PriceBreakdownText, "× 3 =");
            }
            finally { window.Close(); }
        });
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

    [TestMethod]
    public async Task CaisseCommittedFeedbackUsesHumanReferenceEvenWhenOutputFails()
    {
        var categoryId = Guid.NewGuid();
        var product = new OrderEntryProduct(new ProductAggregate(
            new Product(Guid.NewGuid(), "P", "Plat", categoryId, Money.FromCents(1000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
        var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero });
        var store = new ReferenceOrderStore("20260831-001");
        using var service = new OrderEntryService(new SingleEntryCatalogue(product), settings, store, new ThrowingDispatcher(), new DeterministicIds(), new FixedClock());
        using var viewModel = new OrderEntryShellViewModel(service);
        viewModel.AddConfiguredLine(product, [], [], 1);
        viewModel.SelectedFulfilment = FulfilmentMode.Retrait;
        viewModel.SelectedPlannedHour = 11;
        viewModel.SelectedPlannedMinute = 0;
        await viewModel.RepriceAsync(clearManualOverride: true);

        var result = await viewModel.ConfirmAsync();

        Assert.IsTrue(result!.Succeeded);
        Assert.IsTrue(result.HasOutputFailure);
        Assert.IsTrue(viewModel.CommittedMessage.Contains("20260831-001", StringComparison.Ordinal));
        Assert.IsFalse(viewModel.CommittedMessage.Contains(result.CommittedOrder!.Id.ToString(), StringComparison.Ordinal), "The normal output-failure path must not expose the technical GUID when a human reference is available.");
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
        private static OrderBrowserRow Row(OrderSnapshot value) => new(value.Id, value.PlannedFulfilmentDate, value.PlannedFulfilmentTime, value.Fulfilment, value.Status, value.TotalTtc, value.Telephone) { Reference = value.Reference, DeliveryAddress = value.DeliveryAddress, Comment = value.Comment };
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

    private sealed class ThrowingDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => throw new InvalidOperationException("synthetic output failure");
    }

    private sealed class SingleEntryCatalogue(OrderEntryProduct product) : IOrderEntryCatalogueQueries
    {
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([new(product.Aggregate.Product.CategoryId, product.CategoryName)]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([new(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name, product.Aggregate.Product.CategoryId, product.CategoryName, product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, false)]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(productId == product.Aggregate.Product.Id ? product : null);
    }

    private sealed class ReferenceOrderStore(string reference) : IOrderStore
    {
        private OrderSnapshot? snapshot;
        public Task SaveAsync(OrderSnapshot value, CancellationToken cancellationToken = default) { snapshot = value with { Reference = reference }; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(snapshot?.Id == orderId ? snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
    }

    private sealed class FallbackStore(OrderBrowserRow row) : IOrderStore
    {
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([row]);
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
