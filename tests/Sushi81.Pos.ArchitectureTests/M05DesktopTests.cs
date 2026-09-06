using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    private static readonly string[] PickerCultures = ["fr-FR", "zh-CN"];

    [TestMethod]
    public void CatalogueProductPickerShowsCodeAndNameWithOneLocalizedAddActionOnSta()
    {
        RunOnSta(() =>
        {
            var product = PickerProduct();
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            var owner = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            owner.Show();
            try
            {
                foreach (var cultureName in PickerCultures)
                {
                    shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == cultureName)).GetAwaiter().GetResult();
                    var picker = CreateProductPicker(owner, [product]);
                    Exception? callbackFailure = null;
                    picker.ContentRendered += (_, _) =>
                    {
                        try
                        {
                            var list = VisualDescendants<ListBox>(picker).Single();
                            var buttons = VisualDescendants<Button>(picker).ToArray();
                            Assert.HasCount(1, buttons);
                            Assert.AreEqual(shell.Localized["Add"], buttons[0].Content?.ToString());
                            Assert.IsFalse(buttons.Any(button => string.Equals(button.Content?.ToString(), shell.Localized["Cancel"], StringComparison.Ordinal)));
                            Assert.IsGreaterThan(0D, list.ActualHeight, "The picker list must remain usable at its supported small size.");
                            Assert.IsGreaterThan(0D, buttons[0].ActualWidth, "The picker Add action must remain visible at its supported small size.");
                            var renderedText = string.Join(" | ", VisualDescendants<TextBlock>(picker).Select(text => text.Text));
                            StringAssert.Contains(renderedText, product.Code);
                            StringAssert.Contains(renderedText, product.Name);
                            picker.Close();
                        }
                        catch (Exception exception)
                        {
                            callbackFailure = exception;
                            picker.Close();
                        }
                    };

                    var result = picker.ShowDialog();
                    if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                    Assert.AreNotEqual(true, result, $"The {cultureName} picker layout probe must not add a product.");
                }
            }
            finally { owner.Close(); }
        });
    }

    [TestMethod]
    public void CatalogueProductPickerDoubleClickAcceptsSelectedProductOnSta()
    {
        RunOnSta(() =>
        {
            var product = PickerProduct();
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            var owner = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            owner.Show();
            try
            {
                var picker = CreateProductPicker(owner, [product]);
                Exception? callbackFailure = null;
                picker.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var list = VisualDescendants<ListBox>(picker).Single();
                        list.SelectedIndex = 0;
                        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = Control.MouseDoubleClickEvent,
                            Source = list
                        });
                    }
                    catch (Exception exception) { callbackFailure = exception; picker.Close(); }
                };

                Assert.IsTrue(picker.ShowDialog());
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                Assert.AreEqual(product.Id, PickerSelection(picker)?.Id);
            }
            finally { owner.Close(); }
        });
    }

    [TestMethod]
    public void CatalogueProductPickerRejectsEmptyAddAndTitleBarCloseWithoutAddingOnSta()
    {
        RunOnSta(() =>
        {
            var product = PickerProduct();
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            var owner = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            owner.Show();
            try
            {
                var emptyAddPicker = CreateProductPicker(owner, [product]);
                Exception? emptyAddFailure = null;
                emptyAddPicker.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var add = VisualDescendants<Button>(emptyAddPicker).Single();
                        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.IsTrue(emptyAddPicker.IsVisible, "Add without a selection must leave the picker open.");
                        Assert.IsNull(PickerSelection(emptyAddPicker));
                        emptyAddPicker.Close();
                    }
                    catch (Exception exception) { emptyAddFailure = exception; emptyAddPicker.Close(); }
                };
                Assert.AreNotEqual(true, emptyAddPicker.ShowDialog());
                if (emptyAddFailure is not null) ExceptionDispatchInfo.Capture(emptyAddFailure).Throw();
                Assert.IsNull(PickerSelection(emptyAddPicker));

                var closePicker = CreateProductPicker(owner, [product]);
                Exception? closeFailure = null;
                closePicker.ContentRendered += (_, _) =>
                {
                    try { closePicker.Close(); }
                    catch (Exception exception) { closeFailure = exception; closePicker.Close(); }
                };
                Assert.AreNotEqual(true, closePicker.ShowDialog(), "Title-bar close must retain cancel/no-add semantics.");
                if (closeFailure is not null) ExceptionDispatchInfo.Capture(closeFailure).Throw();
                Assert.IsNull(PickerSelection(closePicker));
            }
            finally { owner.Close(); }
        });
    }

    [TestMethod]
    public void CatalogueProductPickerFiltersCodeAndNameWithoutHiddenSelectionOnSta()
    {
        RunOnSta(() =>
        {
            var optionsProduct = PickerProduct();
            var simpleProduct = PickerSimpleProduct();
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            var owner = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            owner.Show();
            try
            {
                var picker = CreateProductPicker(owner, [optionsProduct, simpleProduct]);
                Exception? callbackFailure = null;
                picker.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var list = VisualDescendants<ListBox>(picker).Single();
                        var search = VisualDescendants<TextBox>(picker).Single();
                        Assert.HasCount(2, list.Items);
                        Assert.IsNull(list.SelectedItem);

                        search.Text = "002";
                        Assert.HasCount(1, list.Items);
                        Assert.AreEqual(optionsProduct.Id, ((ProductSummary)list.Items[0]).Id);

                        search.Text = "SIMPLE";
                        Assert.HasCount(1, list.Items);
                        Assert.AreEqual(simpleProduct.Id, ((ProductSummary)list.Items[0]).Id);

                        search.Text = string.Empty;
                        Assert.HasCount(2, list.Items);
                        list.SelectedIndex = 0;
                        search.Text = "simple";
                        Assert.IsNull(list.SelectedItem, "Filtering out the selected item must clear the selection.");

                        list.SelectedIndex = 0;
                        var add = VisualDescendants<Button>(picker).Single();
                        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception exception) { callbackFailure = exception; picker.Close(); }
                };

                Assert.IsTrue(picker.ShowDialog());
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                Assert.AreEqual(simpleProduct.Id, PickerSelection(picker)?.Id);

                var doubleClickPicker = CreateProductPicker(owner, [optionsProduct, simpleProduct]);
                Exception? doubleClickFailure = null;
                doubleClickPicker.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var list = VisualDescendants<ListBox>(doubleClickPicker).Single();
                        var search = VisualDescendants<TextBox>(doubleClickPicker).Single();
                        search.Text = "002";
                        list.SelectedIndex = 0;
                        list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = Control.MouseDoubleClickEvent,
                            Source = list
                        });
                    }
                    catch (Exception exception) { doubleClickFailure = exception; doubleClickPicker.Close(); }
                };

                Assert.IsTrue(doubleClickPicker.ShowDialog());
                if (doubleClickFailure is not null) ExceptionDispatchInfo.Capture(doubleClickFailure).Throw();
                Assert.AreEqual(optionsProduct.Id, PickerSelection(doubleClickPicker)?.Id);
            }
            finally { owner.Close(); }
        });
    }

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
                    Assert.IsTrue(buttons.All(row => row.Length == 4), $"{size}: each selected-order row must contain quantity decrement, quantity increment, edit and delete buttons.");
                    var decreaseLefts = buttons.Select(row => LeftEdge(row[0], window)).ToArray();
                    var increaseLefts = buttons.Select(row => LeftEdge(row[1], window)).ToArray();
                    var editLefts = buttons.Select(row => LeftEdge(row[2], window)).ToArray();
                    var deleteLefts = buttons.Select(row => LeftEdge(row[3], window)).ToArray();
                    var deleteRights = buttons.Select(row => RightEdge(row[3], window)).ToArray();
                    var detailRight = RightEdge(detail, window);
                    Assert.IsLessThanOrEqualTo(1D, decreaseLefts.Max() - decreaseLefts.Min(), $"{size}: decrease buttons must align vertically.");
                    Assert.IsLessThanOrEqualTo(1D, increaseLefts.Max() - increaseLefts.Min(), $"{size}: increase buttons must align vertically.");
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
    public void ExistingOrderPencilQuantityOnlyPathPreservesHistoricalSnapshotThroughWpfPresentationOnSta()
    {
        RunOnSta(() =>
        {
            var productId = Guid.NewGuid();
            var currentProduct = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "SNAP", "Produit historique", Guid.NewGuid(), Money.FromCents(950), 20m, false, true, false, default, default),
                [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Tests");
            var historicalAdjustment = new OrderLineAdjustmentSnapshot(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Ajustement historique", Money.FromCents(-50), 5.5m);
            var item = new OrderItemSnapshot(Guid.NewGuid(), 0, productId, "SNAP", "Produit historique", "Tests", Money.FromCents(850), 5.5m, true, 2, Money.FromCents(1700), Money.FromCents(1600), [historicalAdjustment]);
            var order = Snapshot(new DateOnly(2026, 8, 31)) with { Items = [item], TotalTtc = Money.FromCents(1600) };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            var catalogue = new SingleEntryCatalogue(currentProduct);
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
                var detail = Field<ListBox>(window, "lifecycleDetailLinesList");
                Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel).IsSelected = true;
                window.UpdateLayout();
                var edit = VisualDescendants<Button>(detail).Single(button => Equals(button.Content, "✎"));
                Assert.IsTrue(edit.IsEnabled, "The pencil action must remain enabled during existing-order modification.");
                var line = lifecycle.DetailLines.Single();
                Assert.IsTrue(lifecycle.UpdateLineQuantity(line, 3), "The presentation edit path must accept a quantity-only update without a Catalogue rewrite.");

                Assert.AreEqual(3, lifecycle.DetailLines.Single().Quantity);
                Assert.AreEqual(850L, lifecycle.DetailLines.Single().Item.ProductBasePriceTtc.Cents);
                Assert.AreEqual(5.5m, lifecycle.DetailLines.Single().Item.ProductVatRate);
                Assert.IsTrue(lifecycle.DetailLines.Single().Item.ProductDiscountEligible);
                Assert.AreEqual(-50L, lifecycle.DetailLines.Single().Item.Adjustments.Single().AdjustmentTtcPerUnit.Cents);

                lifecycle.SaveModificationAsync().GetAwaiter().GetResult();
                var saved = store.Snapshot.Items.Single();
                Assert.AreEqual(3, saved.Quantity);
                Assert.AreEqual(850L, saved.ProductBasePriceTtc.Cents);
                Assert.AreEqual(5.5m, saved.ProductVatRate);
                Assert.IsTrue(saved.ProductDiscountEligible);
                Assert.AreEqual(-50L, saved.Adjustments.Single().AdjustmentTtcPerUnit.Cents);
                Assert.AreEqual(2400L, store.Snapshot.TotalTtc.Cents);
                Assert.AreEqual(Money.FromCents(2400).Euros.ToString("0.00", CultureInfo.CurrentCulture), lifecycle.TotalText);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void ExistingOrderLineConfigurationComparisonKeepsUnchangedHistoricalOptionsAndCustomAdjustmentsInSnapshotMode()
    {
        var optionId = Guid.NewGuid();
        var item = new OrderItemSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), "CFG", "Configurable", "Tests", Money.FromCents(850), 10m, true, 1, Money.FromCents(850), Money.FromCents(900),
        [
            new(Guid.NewGuid(), 0, OrderAdjustmentKind.PredefinedOption, optionId, "Choix", "Option", Money.FromCents(25), 10m),
            new(Guid.NewGuid(), 1, OrderAdjustmentKind.CustomAdjustment, null, null, "Ajustement", Money.FromCents(-10), 10m)
        ]);
        var line = new OrderDetailLineViewModel(item);

        Assert.IsTrue(line.HasSameConfiguration([optionId], [new(null, null, "Ajustement", Money.FromCents(-10), OrderAdjustmentKind.CustomAdjustment, 0)]));
        Assert.IsFalse(line.HasSameConfiguration([optionId], [new(null, null, "Ajustement", Money.FromCents(-11), OrderAdjustmentKind.CustomAdjustment, 0)]));
        Assert.IsFalse(line.HasSameConfiguration([], [new(null, null, "Ajustement", Money.FromCents(-10), OrderAdjustmentKind.CustomAdjustment, 0)]));
    }

    [TestMethod]
    public void ExistingOrderExplicitReconfigurationUsesCurrentCatalogueAndRejectsUnavailableConfigurationOnSta()
    {
        RunOnSta(() =>
        {
            var productId = Guid.NewGuid();
            var currentProduct = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "RECONF", "Produit configurable", Guid.NewGuid(), Money.FromCents(950), 20m, true, true, false, default, default),
                [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Tests");
            var historical = new OrderItemSnapshot(Guid.NewGuid(), 0, productId, "RECONF", "Produit configurable", "Tests", Money.FromCents(850), 5.5m, false, 1, Money.FromCents(850), Money.FromCents(900), [new(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Choix historique", Money.FromCents(50), 5.5m)]);
            var order = Snapshot(new DateOnly(2026, 8, 31)) with { Items = [historical], TotalTtc = Money.FromCents(900) };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            var catalogue = new SingleEntryCatalogue(currentProduct);
            using var lifecycleService = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), catalogue, settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();
                Assert.IsTrue(lifecycle.IsEditing);
                var line = lifecycle.DetailLines.Single();

                lifecycle.ReplaceLineAsync(line, new OrderLineDraft(line.Item.Id, currentProduct.Aggregate, [], [new(null, null, "Choix actuel", Money.FromCents(125))], 1, currentProduct.CategoryName)).GetAwaiter().GetResult();
                Assert.AreEqual(950L, lifecycle.DetailLines.Single().Item.ProductBasePriceTtc.Cents, lifecycle.ValidationMessage);
                Assert.AreEqual(20m, lifecycle.DetailLines.Single().Item.ProductVatRate);
                Assert.AreEqual(125L, lifecycle.DetailLines.Single().Item.Adjustments.Single().AdjustmentTtcPerUnit.Cents);

                var beforeInvalid = lifecycle.DetailLines.Single().Item;
                lifecycle.ReplaceLineAsync(lifecycle.DetailLines.Single(), new OrderLineDraft(beforeInvalid.Id, currentProduct.Aggregate, [Guid.NewGuid()], [], 1, currentProduct.CategoryName)).GetAwaiter().GetResult();
                Assert.IsFalse(string.IsNullOrWhiteSpace(lifecycle.ValidationMessage));
                Assert.AreEqual(950L, lifecycle.DetailLines.Single().Item.ProductBasePriceTtc.Cents);
                Assert.AreEqual(125L, lifecycle.DetailLines.Single().Item.Adjustments.Single().AdjustmentTtcPerUnit.Cents);
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
    public void Fix13OperationalViewCanReturnToDateBrowseAndPaymentDateIsEditOnlyOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 31));
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow))), orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                var commandes = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                commandes.IsSelected = true;
                var effectiveDate = Field<DatePicker>(window, "lifecycleEffectivePaymentDatePicker");
                Assert.AreEqual(Visibility.Collapsed, effectiveDate.Visibility);

                lifecycle.SelectOperationalView("future");
                Assert.IsTrue(lifecycle.IsOperationalViewActive);
                lifecycle.ReturnToDateBrowse();
                Assert.IsFalse(lifecycle.IsOperationalViewActive);
                Assert.AreEqual(new DateOnly(2026, 9, 1), DateOnly.FromDateTime(lifecycle.BrowseDate!.Value));

                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Visible, effectiveDate.Visibility);
                Assert.AreEqual(new DateOnly(2026, 8, 31), DateOnly.FromDateTime(lifecycle.EffectivePaymentDate!.Value));
                Assert.IsTrue(VisualDescendants<TextBlock>(window).Any(text => text.Text == shell.Localized["OrderEffectiveDateEdit"]));
                Assert.IsTrue(VisualDescendants<TextBlock>(window).Any(text => text.Text == shell.Localized["OrderEffectiveDateHint"] && text.Visibility == Visibility.Visible));
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.IsTrue(VisualDescendants<TextBlock>(window).Any(text => text.Text == shell.Localized["OrderEffectiveDateEdit"] && text.Visibility == Visibility.Visible));
                Assert.IsTrue(VisualDescendants<TextBlock>(window).Any(text => text.Text == shell.Localized["OrderEffectiveDateHint"] && text.Visibility == Visibility.Visible));
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.IsTrue(VisualDescendants<TextBlock>(window).Any(text => text.Text == shell.Localized["OrderEffectiveDateEdit"] && text.Visibility == Visibility.Visible));
                lifecycle.AbandonModification();
                Assert.AreEqual(Visibility.Collapsed, effectiveDate.Visibility);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix16PaymentEffectiveDateLayoutRemainsReadableAcrossWindowSizesAndLanguagesOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 31));
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                var commandes = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                commandes.IsSelected = true;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();

                var panel = Field<Grid>(window, "lifecycleEffectivePaymentDatePanel");
                var label = Field<TextBlock>(window, "lifecycleEffectivePaymentDateLabel");
                var hint = Field<TextBlock>(window, "lifecycleEffectivePaymentDateHint");
                var picker = Field<DatePicker>(window, "lifecycleEffectivePaymentDatePicker");

                void AssertReadable(string culture, double expectedWidth, double expectedHeight)
                {
                    window.Width = expectedWidth;
                    window.Height = expectedHeight;
                    window.UpdateLayout();
                    Assert.AreEqual(Visibility.Visible, panel.Visibility, $"{culture}: effective payment-date panel must be visible while editing.");
                    Assert.IsGreaterThan(0D, label.ActualWidth, $"{culture}: label must have usable width.");
                    Assert.IsGreaterThan(0D, hint.ActualWidth, $"{culture}: hint must have usable width.");
                    Assert.IsGreaterThan(0D, label.ActualHeight, $"{culture}: label must have rendered height.");
                    Assert.IsGreaterThan(0D, hint.ActualHeight, $"{culture}: hint must have rendered height.");
                    Assert.AreEqual(TextWrapping.Wrap, label.TextWrapping, $"{culture}: label must wrap instead of clipping.");
                    Assert.IsTrue(picker.ActualWidth > 0D && picker.ActualWidth <= 145.5D, $"{culture}: DatePicker must remain compact.");
                    var panelLeft = panel.TranslatePoint(new Point(0, 0), window).X;
                    var pickerLeft = picker.TranslatePoint(new Point(0, 0), window).X;
                    Assert.IsLessThanOrEqualTo(panelLeft + 250D, pickerLeft, $"{culture}: DatePicker must stay in the compact left-side edit cluster.");
                    Assert.IsLessThanOrEqualTo(400D, panel.ActualWidth, $"{culture}: effective payment-date group must remain compact.");
                    Assert.AreEqual(shell.Localized["OrderEffectiveDateEdit"], label.Text);
                    Assert.AreEqual(shell.Localized["OrderEffectiveDateHint"], hint.Text);
                }

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                AssertReadable("fr-FR normal", 980, 700);
                AssertReadable("fr-FR small", 760, 520);
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                AssertReadable("zh-CN small", 760, 520);
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                AssertReadable("fr-FR after round trip", 760, 520);

                lifecycle.AbandonModification();
                window.UpdateLayout();
                Assert.AreEqual(Visibility.Collapsed, panel.Visibility, "The effective payment-date block must be hidden outside edit mode.");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix16CaisseCategorySelectionStaysStableWhileOnlyProductsRefreshOnSta()
    {
        RunOnSta(() =>
        {
            var allId = Guid.Empty;
            var lunchId = Guid.NewGuid();
            var platesId = Guid.NewGuid();
            var drinksId = Guid.NewGuid();
            var emptyId = Guid.NewGuid();
            var categories = new[]
            {
                new CategorySummary(lunchId, "Lunch", "L"),
                new CategorySummary(platesId, "Plats", "P"),
                new CategorySummary(drinksId, "Boissons", "R"),
                new CategorySummary(emptyId, "Sans produits", "Z")
            };
            var products = new[]
            {
                FilterProduct(lunchId, "L-001", "Lunch maki"),
                FilterProduct(platesId, "P-001", "Plat du jour"),
                FilterProduct(drinksId, "R-001", "Eau")
            };
            var catalogue = new FilterableEntryCatalogue(categories, products);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var entryService = new OrderEntryService(catalogue, settings, new FallbackStore(Row(Guid.NewGuid(), "FIX16", new DateOnly(2026, 8, 31))), new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), entryService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var entry = shell.Entry!;
                var caisse = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderEntryShellViewModel);
                caisse.IsSelected = true;
                entry.RefreshAsync().GetAwaiter().GetResult();
                window.UpdateLayout();
                var categoriesList = Field<ListBox>(window, "orderCategoriesList");
                var productsGrid = Field<DataGrid>(window, "orderProductsGrid");
                var categoryCallCountAfterFullRefresh = catalogue.CategoryCallCount;

                void AssertSelection(Guid categoryId, bool expectProductRefresh, params Guid[] expectedProductIds)
                {
                    var productCallCountBefore = catalogue.ProductCallCount;
                    categoriesList.SelectedValue = categoryId;
                    categoriesList.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateSource();
                    window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                    window.UpdateLayout();

                    Assert.AreEqual(expectProductRefresh ? productCallCountBefore + 1 : productCallCountBefore, catalogue.ProductCallCount);
                    Assert.AreEqual(categoryId, entry.SelectedCategoryId);
                    Assert.AreEqual(categoryId, categoriesList.SelectedValue);
                    Assert.HasCount(1, categoriesList.SelectedItems);
                    var selectedContainer = categoriesList.ItemContainerGenerator.ContainerFromItem(categoriesList.SelectedItem) as ListBoxItem;
                    Assert.IsNotNull(selectedContainer);
                    Assert.IsTrue(selectedContainer!.IsSelected, $"Category {categoryId} must retain its selected-row visual.");
                    Assert.AreEqual(1, VisualDescendants<ListBoxItem>(categoriesList).Count(item => item.IsSelected));
                    CollectionAssert.AreEquivalent(expectedProductIds, productsGrid.Items.Cast<ProductSummary>().Select(product => product.Id).ToArray());
                    Assert.AreEqual(categoryCallCountAfterFullRefresh, catalogue.CategoryCallCount, "Ordinary category filtering must not rebuild the category source.");
                }

                AssertSelection(lunchId, true, products[0].Id);
                AssertSelection(platesId, true, products[1].Id);
                AssertSelection(emptyId, true);
                AssertSelection(drinksId, true, products[2].Id);
                AssertSelection(allId, true, products.Select(product => product.Id).ToArray());

                var productCallCountBeforeUnrelatedInteraction = catalogue.ProductCallCount;
                Field<TextBox>(window, "orderProductSearchBox").Focus();
                productsGrid.SelectedItem = products[0];
                Field<DatePicker>(window, "orderPlannedDatePicker").Focus();
                window.UpdateLayout();
                Assert.AreEqual(productCallCountBeforeUnrelatedInteraction, catalogue.ProductCallCount, "Unrelated focus and product-selection interactions must not refresh the catalogue.");

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                AssertSelection(allId, false, products.Select(product => product.Id).ToArray());
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                AssertSelection(allId, false, products.Select(product => product.Id).ToArray());
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public async Task Fix17RapidEntryFilterChangesCancelStaleProductWorkWithoutCategoryQueries()
    {
        var categoryId = Guid.NewGuid();
        var latest = FilterProduct(categoryId, "LATEST", "Latest result");
        var catalogue = new DelayedEntryCatalogue();
        var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
        using var service = new OrderEntryService(catalogue, settings, new FallbackStore(Row(Guid.NewGuid(), "FIX17", new DateOnly(2026, 8, 31))), new NoopDispatcher(), new DeterministicIds(), new FixedClock());
        using var entry = new OrderEntryShellViewModel(service);
        var latestApplied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Products.CollectionChanged += (_, _) =>
        {
            if (entry.Products.Any(product => product.Id == latest.Id)) latestApplied.TrySetResult(true);
        };

        entry.SelectedCategoryId = categoryId;
        await catalogue.WaitForProductCallAsync(1).WaitAsync(TimeSpan.FromSeconds(3));
        entry.SearchText = "latest";
        await catalogue.WaitForProductCallAsync(2).WaitAsync(TimeSpan.FromSeconds(3));
        await catalogue.FirstCallCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));

        catalogue.CompleteProductCall(1, [latest]);
        await latestApplied.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.AreEqual(2, catalogue.ProductCallCount);
        Assert.AreEqual(0, catalogue.CategoryCallCount);
        Assert.AreEqual(latest.Id, entry.Products.Single().Id);
        Assert.AreEqual("latest", entry.SearchText);
    }

    [TestMethod]
    public void Fix13NumericInputsAcceptBothSeparatorsRejectGroupingAndSelectAllOnFocusOnSta()
    {
        Assert.IsTrue(M03Presentation.TryParseDecimalInput("12,50", out var comma));
        Assert.AreEqual(12.50m, comma);
        Assert.IsTrue(M03Presentation.TryParseDecimalInput("12.50", out var dot));
        Assert.AreEqual(12.50m, dot);
        Assert.IsFalse(M03Presentation.TryParseDecimalInput("1,234.50", out _));

        RunOnSta(() =>
        {
            var box = new TextBox { Text = "12.50" };
            NumericInputBehavior.SetSelectAllOnFocus(box, true);
            var window = new Window { Content = box, ShowInTaskbar = false, Width = 160, Height = 80 };
            window.Show();
            try
            {
                box.Focus();
                window.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
                Assert.AreEqual("12.50", box.SelectedText);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix13QuantityZeroRemovesCaisseLineAndDashboardStylesAreExplicitOnSta()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(new Product(Guid.NewGuid(), "Q", "Quantité", categoryId, Money.FromCents(1000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Tests");
            using var service = new OrderEntryService(new SingleEntryCatalogue(product), new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow)), new FallbackStore(Row(Guid.NewGuid(), "Q-1", new DateOnly(2026, 8, 31))), new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var entry = new OrderEntryShellViewModel(service);
            entry.AddConfiguredLine(product, [], [], 1);
            entry.ChangeQuantity(entry.Cart.Single(), 0);
            Assert.IsEmpty(entry.Cart);

            var order = Snapshot(new DateOnly(2026, 8, 31));
            using var lifecycleService = new OrderLifecycleService(new LifecycleStore(order), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow))), orderLifecycleService: lifecycleService);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                void AssertDashboardStyles()
                {
                    Assert.AreEqual(Brushes.DarkRed, Field<TextBlock>(window, "dashboardFutureCountText").Foreground);
                    Assert.AreEqual(Brushes.DarkRed, Field<TextBlock>(window, "dashboardDueCountText").Foreground);
                    Assert.AreEqual(Brushes.DarkRed, Field<TextBlock>(window, "dashboardOverdueCountText").Foreground);
                    Assert.AreEqual(Brushes.DarkBlue, Field<TextBlock>(window, "dashboardReceivedCardText").Foreground);
                    Assert.AreEqual(Brushes.DarkGreen, Field<TextBlock>(window, "dashboardReceivedCashText").Foreground);
                    Assert.AreEqual(FontWeights.Bold, Field<TextBlock>(window, "dashboardTurnoverText").FontWeight);
                }

                AssertDashboardStyles();
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                AssertDashboardStyles();
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                AssertDashboardStyles();
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix14QuantityDialogsExposeCompactControlsAndZeroMeansRemovalOnSta()
    {
        RunOnSta(() =>
        {
            var productId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "QCTRL", "Quantité contrôlée", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, false, default, default),
                [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Tests");
            var item = new OrderItemSnapshot(Guid.NewGuid(), 0, productId, "QCTRL", "Quantité contrôlée", "Tests", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), []);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true);
            var owner = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            owner.Show();
            try
            {
                var optionType = typeof(MainWindow).GetNestedType("OptionSelectionDialog", BindingFlags.NonPublic)!;
                var optionConstructor = optionType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    [typeof(Window), typeof(OrderEntryProduct), typeof(int), typeof(IReadOnlyList<Guid>), typeof(IReadOnlyList<OrderLineAdjustmentDraft>)], null)!;
                var plusDialog = (Window)optionConstructor.Invoke([owner, product, 1, Array.Empty<Guid>(), Array.Empty<OrderLineAdjustmentDraft>()]);
                plusDialog.Show();
                plusDialog.UpdateLayout();
                try
                {
                    var controls = VisualDescendants<Button>(plusDialog).Where(button => button.Content is "−" or "+").ToArray();
                    Assert.HasCount(2, controls);
                    Assert.IsTrue(controls.All(button => button.Width <= 30D && button.ActualWidth > 0D), "Option quantity controls must stay compact and visible.");
                    controls.Single(button => Equals(button.Content, "+")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.AreEqual("2", ((TextBox)optionType.GetField("quantity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plusDialog)!).Text);
                    controls.Single(button => Equals(button.Content, "−")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.AreEqual("1", ((TextBox)optionType.GetField("quantity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(plusDialog)!).Text);
                }
                finally { plusDialog.Close(); }

                var optionRemove = (Window)optionConstructor.Invoke([owner, product, 1, Array.Empty<Guid>(), Array.Empty<OrderLineAdjustmentDraft>()]);
                var optionFailure = default(Exception);
                optionRemove.ContentRendered += (_, _) =>
                {
                    try { VisualDescendants<Button>(optionRemove).Single(button => Equals(button.Content, "−")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    catch (Exception exception) { optionFailure = exception; optionRemove.Close(); }
                };
                Assert.IsFalse(optionRemove.ShowDialog());
                if (optionFailure is not null) ExceptionDispatchInfo.Capture(optionFailure).Throw();
                Assert.IsTrue((bool)optionType.GetProperty("RemoveRequested")!.GetValue(optionRemove)!);

                var existingType = typeof(MainWindow).GetNestedType("ExistingLineEditDialog", BindingFlags.NonPublic)!;
                var existingConstructor = existingType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    [typeof(Window), typeof(OrderDetailLineViewModel), typeof(bool)], null)!;
                var existingLine = new OrderDetailLineViewModel(item);
                var existingDialog = (Window)existingConstructor.Invoke([owner, existingLine, true]);
                existingDialog.Show();
                existingDialog.UpdateLayout();
                try
                {
                    var controls = VisualDescendants<Button>(existingDialog).Where(button => button.Content is "−" or "+").ToArray();
                    Assert.HasCount(2, controls);
                    Assert.IsTrue(controls.All(button => button.Width <= 30D && button.ActualWidth > 0D), "Existing-line quantity controls must stay compact and visible.");
                    controls.Single(button => Equals(button.Content, "+")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.AreEqual("2", ((TextBox)existingType.GetField("quantity", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(existingDialog)!).Text);
                }
                finally { existingDialog.Close(); }

                var existingRemove = (Window)existingConstructor.Invoke([owner, new OrderDetailLineViewModel(item), true]);
                var existingFailure = default(Exception);
                existingRemove.ContentRendered += (_, _) =>
                {
                    try { VisualDescendants<Button>(existingRemove).Single(button => Equals(button.Content, "−")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    catch (Exception exception) { existingFailure = exception; existingRemove.Close(); }
                };
                Assert.IsFalse(existingRemove.ShowDialog());
                if (existingFailure is not null) ExceptionDispatchInfo.Capture(existingFailure).Throw();
                Assert.IsTrue((bool)existingType.GetProperty("RemoveRequested")!.GetValue(existingRemove)!);
            }
            finally { owner.Close(); }
        });
    }

    [TestMethod]
    public void Fix15PendingNewQuantityRemovalLeavesCartEmptyAndNextAddUsableOnSta()
    {
        RunOnSta(() =>
        {
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(Guid.NewGuid(), "PENDING", "Produit en attente", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, true, default, default),
                [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Tests");
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var entryService = new OrderEntryService(new SingleEntryCatalogue(product), settings, new FallbackStore(Row(Guid.NewGuid(), "PENDING-ROW", new DateOnly(2026, 8, 31))), new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), entryService);
            var owner = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            owner.Show();
            try
            {
                var entry = shell.Entry!;
                var summary = new ProductSummary(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name, product.Aggregate.Product.CategoryId, product.CategoryName, product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, true);
                entry.SelectedProduct = summary;
                entry.AddSelectedProductAsync().GetAwaiter().GetResult();
                Assert.IsNotNull(entry.PendingProduct);
                Assert.IsEmpty(entry.Cart);

                var dialogType = typeof(MainWindow).GetNestedType("OptionSelectionDialog", BindingFlags.NonPublic)!;
                var constructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    [typeof(Window), typeof(OrderEntryProduct), typeof(OrderEntryCartLineViewModel)], null)!;
                var dialog = (Window)constructor.Invoke([owner, product, null]);
                Exception? callbackFailure = null;
                dialog.ContentRendered += (_, _) =>
                {
                    try { VisualDescendants<Button>(dialog).Single(button => Equals(button.Content, "−")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                    catch (Exception exception) { callbackFailure = exception; dialog.Close(); }
                };
                Assert.IsFalse(dialog.ShowDialog(), "Pending-new 1 -> 0 must cancel the add dialog.");
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                Assert.IsEmpty(entry.Cart, "Cancelling a pending-new quantity at zero must not add a line.");
                Assert.IsTrue(entry.Cart.All(line => line.Quantity > 0), "A pending-new zero quantity must never enter the cart.");

                entry.SelectedProduct = summary;
                entry.AddSelectedProductAsync().GetAwaiter().GetResult();
                Assert.IsNotNull(entry.PendingProduct, "The next normal product-add attempt must remain usable.");
                entry.AddConfiguredLine(entry.PendingProduct!, [], [], 1);
                Assert.HasCount(1, entry.Cart);
                Assert.AreEqual(1, entry.Cart.Single().Quantity);
            }
            finally { owner.Close(); }
        });
    }

    [TestMethod]
    public void Fix14ExistingLineRemovalRestoresOnAbandonAndPersistsOnSaveOnSta()
    {
        RunOnSta(() =>
        {
            var first = new OrderItemSnapshot(Guid.NewGuid(), 0, Guid.NewGuid(), "REMOVE", "À retirer", "Tests", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), []);
            var second = new OrderItemSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), "KEEP", "À conserver", "Tests", Money.FromCents(1200), 10m, true, 1, Money.FromCents(1200), Money.FromCents(1200), []);
            var order = Snapshot(new DateOnly(2026, 8, 31)) with { Items = [first, second], TotalTtc = Money.FromCents(2200) };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();
                lifecycle.RemoveLine(lifecycle.DetailLines.Single(line => line.Item.ProductCode == "REMOVE"));
                Assert.HasCount(1, lifecycle.DetailLines);
                Assert.IsTrue(lifecycle.DetailLines.All(line => line.Quantity > 0), "Removal must not leave a zero-quantity line.");
                lifecycle.AbandonModification();
                Assert.HasCount(2, lifecycle.DetailLines, "Abandon must restore the complete saved snapshot.");

                lifecycle.BeginModification();
                lifecycle.RemoveLine(lifecycle.DetailLines.Single(line => line.Item.ProductCode == "REMOVE"));
                lifecycle.SaveModificationAsync().GetAwaiter().GetResult();
                Assert.HasCount(1, store.Snapshot.Items);
                Assert.AreEqual("KEEP", store.Snapshot.Items.Single().ProductCode);
                Assert.IsTrue(store.Snapshot.Items.All(item => item.Quantity > 0), "Persistence must never contain a zero-quantity item.");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix14CaisseWidthsAndLifecycleSearchExitRemainUsableAcrossLanguagesOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 31)) with { DeliveryAddress = new string('a', 160), Comment = new string('c', 160) };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            var entryProduct = new OrderEntryProduct(new ProductAggregate(
                new Product(Guid.NewGuid(), "LAYOUT", "Produit de mise en page", Guid.NewGuid(), Money.FromCents(1000), 10m,  true, true, false, default, default),
                [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Tests");
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var entryService = new OrderEntryService(new SingleEntryCatalogue(entryProduct), settings, store, new NoopDispatcher(), new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), entryService, service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderEntryShellViewModel).IsSelected = true;
                var fulfilment = Field<ComboBox>(window, "orderFulfilmentBox");
                var plannedDate = Field<DatePicker>(window, "orderPlannedDatePicker");
                var telephone = Field<TextBox>(window, "orderTelephoneBox");
                var address = Field<TextBox>(window, "orderDeliveryAddressBox");
                var comment = Field<TextBox>(window, "orderCommentBox");
                var total = Field<TextBox>(window, "orderTotalBox");

                void AssertCaisse(string culture)
                {
                    window.UpdateLayout();
                    Assert.IsTrue(fulfilment.ActualWidth > 0D && fulfilment.ActualWidth <= 150D, $"{culture}: fulfilment selector must retain its compact width.");
                    Assert.IsTrue(plannedDate.ActualWidth > 0D && plannedDate.ActualWidth <= 145D, $"{culture}: date picker must retain its compact width.");
                    Assert.IsTrue(telephone.ActualWidth > 0D && telephone.ActualWidth <= 170D, $"{culture}: telephone field must retain its compact width.");
                    Assert.IsTrue(total.ActualWidth > 0D && total.ActualWidth <= 120D, $"{culture}: total field must retain its compact width.");
                    Assert.IsGreaterThan(0D, address.ActualWidth, $"{culture}: long address must remain usable.");
                    Assert.IsGreaterThan(0D, comment.ActualWidth, $"{culture}: long comment must remain usable.");
                }

                AssertCaisse("fr-FR-normal");
                window.Width = 760;
                window.Height = 520;
                AssertCaisse("fr-FR-small");
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                AssertCaisse("zh-CN-small");
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                AssertCaisse("fr-FR-restored");

                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectOperationalView("future");
                Assert.IsTrue(lifecycle.IsOperationalViewActive);
                lifecycle.SearchText = "old-filter";
                Assert.IsFalse(lifecycle.IsOperationalViewActive, "Typing a search must leave the operational dashboard view.");
                lifecycle.SearchText = string.Empty;
                Assert.IsFalse(lifecycle.IsOperationalViewActive, "Clearing search must not resurrect the previous operational filter.");
                lifecycle.SelectOperationalView("future");
                lifecycle.BrowseDate = new DateTime(2026, 9, 2);
                Assert.IsFalse(lifecycle.IsOperationalViewActive, "Explicit date browsing must leave the operational dashboard view.");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix15OperationalExitLoadsDateRowsAndLocalizedBrowseButtonWorksOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 31));
            var ordinaryOperationalDate = new OrderBrowserRow(Guid.NewGuid(), new DateOnly(2026, 9, 1), new TimeOnly(11, 0), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromCents(1100), "06 00 00 00 01") { Reference = "DATE-OPERATIONAL" };
            var ordinaryManualDate = new OrderBrowserRow(Guid.NewGuid(), new DateOnly(2026, 9, 2), new TimeOnly(11, 0), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromCents(1200), "06 00 00 00 02") { Reference = "DATE-MANUAL" };
            var operationalFuture = new OrderBrowserRow(Guid.NewGuid(), new DateOnly(2026, 9, 1), new TimeOnly(11, 0), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromCents(1300), "06 00 00 00 03") { Reference = "OP-FUTURE" };
            var searchRow = new OrderBrowserRow(Guid.NewGuid(), new DateOnly(2026, 8, 30), new TimeOnly(11, 0), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromCents(1400), "06 00 00 00 04") { Reference = "SEARCH-RESULT" };
            var store = new NavigationLifecycleStore(order, [ordinaryOperationalDate, ordinaryManualDate], [operationalFuture], [searchRow]);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                var commandes = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                commandes.IsSelected = true;

                lifecycle.SelectOperationalView("future");
                lifecycle.RefreshAsync().GetAwaiter().GetResult();
                Assert.AreEqual("OP-FUTURE", lifecycle.Orders.Single().ReferenceText);

                lifecycle.BrowseDate = new DateTime(2026, 9, 2);
                lifecycle.RefreshAsync().GetAwaiter().GetResult();
                Assert.IsFalse(lifecycle.IsOperationalViewActive);
                Assert.AreEqual("DATE-MANUAL", lifecycle.Orders.Single().ReferenceText, "Manual date browsing must load ordinary planned-date rows.");

                lifecycle.SelectOperationalView("future");
                lifecycle.RefreshAsync().GetAwaiter().GetResult();
                lifecycle.SearchText = "needle";
                lifecycle.RefreshAsync().GetAwaiter().GetResult();
                Assert.AreEqual("SEARCH-RESULT", lifecycle.Orders.Single().ReferenceText);
                lifecycle.SearchText = string.Empty;
                lifecycle.RefreshAsync().GetAwaiter().GetResult();
                Assert.IsFalse(lifecycle.IsOperationalViewActive, "Clearing search must leave operational mode.");
                Assert.AreEqual("DATE-OPERATIONAL", lifecycle.Orders.Single().ReferenceText, "Clearing search must not resurrect the prior operational result set.");

                foreach (var cultureName in PickerCultures)
                {
                    shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == cultureName)).GetAwaiter().GetResult();
                    lifecycle.SelectOperationalView("future");
                    lifecycle.RefreshAsync().GetAwaiter().GetResult();
                    var browse = Field<Button>(window, "commandesBrowseByDateButton");
                    Assert.AreEqual(shell.Localized["OrderBrowseByDate"], browse.Content?.ToString(), $"{cultureName}: browse button must be localized.");
                    browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    lifecycle.RefreshAsync().GetAwaiter().GetResult();
                    Assert.IsFalse(lifecycle.IsOperationalViewActive, $"{cultureName}: localized browse button must clear operational mode.");
                    Assert.AreEqual("DATE-OPERATIONAL", lifecycle.Orders.Single().ReferenceText, $"{cultureName}: localized browse button must restore ordinary date rows.");
                }
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix15CommandesCompactEditControlsRemainVisibleAcrossSizesAndLanguagesOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 31)) with { DeliveryAddress = new string('a', 160), Comment = new string('c', 160) };
            var store = new LifecycleStore(order);
            var settings = new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock(), settings: settings);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(settings), orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                var commandes = Field<TabControl>(window, "mainTabs").Items.OfType<TabItem>().Single(item => item.DataContext is OrderLifecycleShellViewModel);
                commandes.IsSelected = true;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();

                var total = Field<TextBox>(window, "lifecycleEditTotalBox");
                var card = Field<TextBox>(window, "lifecycleEditCardBox");
                var cash = Field<TextBox>(window, "lifecycleEditCashBox");
                var effectiveDate = Field<DatePicker>(window, "lifecycleEffectivePaymentDatePicker");
                var address = Field<TextBox>(window, "lifecycleEditAddressBox");
                var comment = Field<TextBox>(window, "lifecycleEditCommentBox");

                void AssertCompact(string culture)
                {
                    window.UpdateLayout();
                    Assert.IsTrue(total.ActualWidth > 0D && total.ActualWidth <= 120D, $"{culture}: Total TTC must remain compact and visible.");
                    Assert.IsTrue(card.ActualWidth > 0D && card.ActualWidth <= 120D, $"{culture}: CB must remain compact and visible.");
                    Assert.IsTrue(cash.ActualWidth > 0D && cash.ActualWidth <= 120D, $"{culture}: Espèce must remain compact and visible.");
                    Assert.IsTrue(effectiveDate.ActualWidth > 0D && effectiveDate.ActualWidth <= 145D, $"{culture}: effective payment date must remain compact and visible.");
                    Assert.IsGreaterThan(0D, address.ActualWidth, $"{culture}: long Commandes address must remain usable.");
                    Assert.IsGreaterThan(0D, comment.ActualWidth, $"{culture}: long Commandes comment must remain usable.");
                }

                AssertCompact("fr-FR-normal");
                window.Width = 760;
                window.Height = 520;
                AssertCompact("fr-FR-small");
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                AssertCompact("zh-CN-small");
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                AssertCompact("fr-FR-restored");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void Fix14DateOnlyPaymentEditCreatesNoPaymentAdjustmentOnSta()
    {
        RunOnSta(() =>
        {
            var order = Snapshot(new DateOnly(2026, 8, 31));
            var store = new LifecycleStore(order);
            using var service = new OrderLifecycleService(store, new DeterministicIds(), new FixedClock());
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(new EmptyCatalogueStore()), new BusinessSettingsService(new SettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow))), orderLifecycleService: service);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var lifecycle = shell.Lifecycle!;
                lifecycle.SelectAsync(new OrderManagementRowViewModel(new OrderBrowserRow(order.Id, order.PlannedFulfilmentDate, order.PlannedFulfilmentTime, order.Fulfilment, order.Status, order.TotalTtc, order.Telephone) { Reference = order.Reference })).GetAwaiter().GetResult();
                lifecycle.BeginModification();
                lifecycle.EffectivePaymentDate = new DateTime(2026, 8, 29);
                lifecycle.SaveModificationAsync().GetAwaiter().GetResult();
                Assert.IsEmpty(store.LastAdjustments, "Changing only the effective date must not create a PaymentAdjustment.");
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

    private static ProductSummary PickerProduct() => new(Guid.NewGuid(), "TST002", "Produit test options", Guid.NewGuid(), "Tests", Money.FromCents(1000), 10m, true, true, false);

    private static ProductSummary PickerSimpleProduct() => new(Guid.NewGuid(), "TST001A", "Produit test simple", Guid.NewGuid(), "Tests", Money.FromCents(800), 10m, true, true, false);

    private static ProductSummary FilterProduct(Guid categoryId, string code, string name) => new(Guid.NewGuid(), code, name, categoryId, "Synthetic", Money.FromCents(1000), 10m, true, true, false);

    private static Window CreateProductPicker(MainWindow owner, IReadOnlyList<ProductSummary> products)
    {
        var dialogType = typeof(MainWindow).GetNestedType("CatalogueProductPickerDialog", BindingFlags.NonPublic)!;
        var constructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(Window), typeof(IReadOnlyList<ProductSummary>)], null)!;
        return (Window)constructor.Invoke([owner, products]);
    }

    private static ProductSummary? PickerSelection(Window picker) => (ProductSummary?)picker.GetType().GetProperty("SelectedProduct", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(picker);

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

    private sealed class NavigationLifecycleStore(
        OrderSnapshot initial,
        IReadOnlyList<OrderBrowserRow> ordinaryRows,
        IReadOnlyList<OrderBrowserRow> operationalRows,
        IReadOnlyList<OrderBrowserRow> searchRows) : IOrderStore, IOrderLifecycleStore
    {
        public OrderSnapshot Snapshot { get; private set; } = initial;
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { Snapshot = snapshot; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(Snapshot.Id == orderId ? Snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>(ordinaryRows.Where(row => row.PlannedFulfilmentDate == plannedDate).ToArray());
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) { Snapshot = snapshot; return Task.CompletedTask; }
        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(string? query, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>(query is null ? operationalRows : searchRows);
        public Task<OrderOperationalSummary> GetOperationalSummaryAsync(DateOnly businessDate, CancellationToken cancellationToken = default) => Task.FromResult(new OrderOperationalSummary(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0));
    }

    private sealed class LifecycleStore(OrderSnapshot initial) : IOrderStore, IOrderLifecycleStore
    {
        public OrderSnapshot Snapshot { get; private set; } = initial;
        public IReadOnlyList<PaymentAdjustment> LastAdjustments { get; private set; } = [];
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { Snapshot = snapshot; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(Snapshot.Id == orderId ? Snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([Row(Snapshot)]);
        public Task SaveLifecycleAsync(OrderSnapshot snapshot, IReadOnlyList<PaymentAdjustment> adjustments, CancellationToken cancellationToken = default) { Snapshot = snapshot; LastAdjustments = adjustments; return Task.CompletedTask; }
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

    private sealed class FilterableEntryCatalogue(IReadOnlyList<CategorySummary> categories, IReadOnlyList<ProductSummary> products) : IOrderEntryCatalogueQueries
    {
        public int CategoryCallCount { get; private set; }
        public int ProductCallCount { get; private set; }

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        {
            CategoryCallCount++;
            return Task.FromResult(categories);
        }

        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default)
        {
            ProductCallCount++;
            IEnumerable<ProductSummary> result = products;
            if (filterCategoryId is { } categoryId) result = result.Where(product => product.CategoryId == categoryId);
            if (!string.IsNullOrWhiteSpace(search)) result = result.Where(product => product.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || product.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<IReadOnlyList<ProductSummary>>(result.ToArray());
        }

        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(null);
    }

    private sealed class DelayedEntryCatalogue : IOrderEntryCatalogueQueries
    {
        private readonly object gate = new();
        private readonly List<TaskCompletionSource<IReadOnlyList<ProductSummary>>> pendingProductCalls = [];
        private readonly List<TaskCompletionSource<bool>> productCallWaiters = [];

        public int CategoryCallCount { get; private set; }
        public int ProductCallCount { get { lock (gate) return pendingProductCalls.Count; } }
        public TaskCompletionSource<bool> FirstCallCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        {
            CategoryCallCount++;
            return Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        }

        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default)
        {
            var result = new TaskCompletionSource<IReadOnlyList<ProductSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
            int callNumber;
            lock (gate)
            {
                pendingProductCalls.Add(result);
                callNumber = pendingProductCalls.Count;
                foreach (var waiter in productCallWaiters.ToArray()) waiter.TrySetResult(true);
                productCallWaiters.Clear();
            }
            cancellationToken.Register(() =>
            {
                if (callNumber == 1) FirstCallCancelled.TrySetResult(true);
                result.TrySetCanceled(cancellationToken);
            });
            return result.Task;
        }

        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(null);

        public Task WaitForProductCallAsync(int count)
        {
            lock (gate)
            {
                if (pendingProductCalls.Count >= count) return Task.CompletedTask;
                var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                productCallWaiters.Add(waiter);
                return waiter.Task;
            }
        }

        public void CompleteProductCall(int zeroBasedIndex, IReadOnlyList<ProductSummary> products)
        {
            lock (gate) pendingProductCalls[zeroBasedIndex].TrySetResult(products);
        }
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
