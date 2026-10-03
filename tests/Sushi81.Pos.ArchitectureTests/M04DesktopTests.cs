using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;
using DomainSelectionMode = Sushi81.Pos.Domain.SelectionMode;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M04DesktopTests
{
    [TestMethod]
    public void OptionsDisabledDialogSkipsDormantGroupValidationOnSta()
    {
        RunOnSta(() =>
        {
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            var owner = new Window { DataContext = shell, ShowInTaskbar = false };
            owner.Show();
            try
            {
                var groupId = Guid.NewGuid();
                var product = new OrderEntryProduct(new ProductAggregate(
                    new Product(Guid.NewGuid(), "P1", "Plat", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, false, default, default),
                    [new OptionGroup(groupId, Guid.Empty, "Dormant", DomainSelectionMode.Single, true, null, null, 0, default, default)],
                    new Dictionary<Guid, IReadOnlyList<ProductOption>> { [groupId] = [] }), "Plats");
                var dialogType = typeof(MainWindow).GetNestedType("OptionSelectionDialog", BindingFlags.NonPublic)!;
                var constructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    [typeof(Window), typeof(OrderEntryProduct), typeof(OrderEntryCartLineViewModel)], null)!;
                var dialog = (Window)constructor.Invoke([owner, product, null]);
                Exception? callbackFailure = null;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var add = VisualDescendants<Button>(dialog).Single(button => string.Equals(button.Content?.ToString(), shell.Localized["Add"], StringComparison.Ordinal));
                        add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception exception) { callbackFailure = exception; dialog.Close(); }
                };
                var result = dialog.ShowDialog();
                if (callbackFailure is not null) ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                Assert.IsTrue(result);
                dialog.Close();
            }
            finally { owner.Close(); }
        });
    }

    [TestMethod]
    public void MainWindowM04LifecycleRendersLocalizedChoicesQuantityAndRetainsCompatibilitySeams()
    {
        RunOnSta(() =>
        {
            var productId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "P1", "Plat historique", categoryId, Money.FromCents(1250), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
            var store = new DesktopOrderStore();
            var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(),
                startupSucceeded: true,
                orderEntryService: new OrderEntryService(
                    new DesktopCatalogue(product, categoryId),
                    new DesktopSettingsStore(),
                    store,
                    new DesktopDispatcher(),
                    new DesktopIds(),
                    new DesktopClock()));
                var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                window.UpdateLayout();
                var caisse = VisualDescendants<TabItem>(window).Single(item => string.Equals(item.Header?.ToString(), shell.Localized["Caisse"], StringComparison.Ordinal));
                caisse.IsSelected = true;
                window.UpdateLayout();
                Assert.IsFalse(VisualDescendants<FrameworkElement>(window).Any(element => element.Name is "orderBrowserGrid" or "orderBrowserGroup" or "orderSavedGroup" or "orderReloadIdBox" or "orderReloadedDisplay"), "The duplicated M04 saved-order/reload controls must not be present in Caisse.");
                var entry = shell.Entry!;
                entry.AddConfiguredLine(product, [], [], 2);
                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                entry.RepriceAsync(clearManualOverride: true).GetAwaiter().GetResult();
                window.UpdateLayout();

                var fulfilment = VisualDescendants<ComboBox>(window).Single(combo => combo.ItemsSource is System.Collections.IEnumerable items && items.Cast<object>().OfType<FulfilmentChoice>().Any());
                Assert.AreEqual("Label", fulfilment.DisplayMemberPath);
                Assert.AreEqual("Retrait", ((FulfilmentChoice)fulfilment.Items[1]).Label);
                var quantity = VisualDescendants<TextBlock>(window).Single(text => text.Text.Contains("Quantité", StringComparison.Ordinal));
                StringAssert.Contains(quantity.Text, "2");
                var discount = VisualDescendants<CheckBox>(window).Single(check => check.Content?.ToString() == shell.Localized["PickupDiscountRequest"]);
                Assert.IsTrue(discount.IsEnabled);
                entry.SelectedFulfilment = FulfilmentMode.Livraison;
                window.UpdateLayout();
                Assert.IsFalse(discount.IsEnabled);
                Assert.IsFalse(entry.PickupDiscountRequested);
                entry.SelectedPlannedHour = 18;
                entry.SelectedPlannedMinute = 25;
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();

                var first = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(first!.Succeeded);
                var firstId = first.CommittedOrder!.Id;
                entry.RefreshOrderBrowserAsync().GetAwaiter().GetResult();
                Assert.HasCount(1, entry.BrowserOrders);
                Assert.AreEqual("18:25", entry.BrowserOrders.Single().PlannedTimeText);
                Assert.AreEqual(firstId, entry.SelectedBrowserOrder!.Id);
                entry.StartNewOrder();
                entry.AddConfiguredLine(product, [], [], 1);
                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                entry.SelectedPlannedHour = 18;
                entry.SelectedPlannedMinute = 25;
                var second = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(second!.Succeeded);
                Assert.AreNotEqual(firstId, second.CommittedOrder!.Id);
                Assert.HasCount(2, entry.BrowserOrders);
                Assert.AreEqual(second.CommittedOrder.Id, entry.SelectedBrowserOrder!.Id);

                entry.SelectBrowserOrderAsync(entry.BrowserOrders.Single(row => row.Id == firstId)).GetAwaiter().GetResult();
                Assert.AreEqual(firstId, entry.ReloadedOrder!.Id);
                Assert.IsNotNull(entry.BrowseDate);

                entry.ReloadOrderIdText = firstId.ToString();
                Assert.IsTrue(entry.ReloadOrderAsync().GetAwaiter().GetResult(), "The compatibility reload seam remains available without the removed Caisse controls.");

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.AreEqual("自取", ((FulfilmentChoice)fulfilment.Items[1]).Label);
                Assert.AreEqual(firstId, entry.SelectedBrowserOrder!.Id);
                Assert.AreEqual(firstId, entry.ReloadedOrder!.Id);
                Assert.AreEqual("18:25", entry.BrowserOrders.Single(row => row.Id == firstId).PlannedTimeText);
                Assert.AreEqual("配送", entry.BrowserOrders.Single(row => row.Id == firstId).FulfilmentText);
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                Assert.AreEqual("Retrait", ((FulfilmentChoice)fulfilment.Items[1]).Label);
                Assert.AreEqual("Livraison", entry.BrowserOrders.Single(row => row.Id == firstId).FulfilmentText);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void OrderBrowserUsesTwentyFourHourDisplayForEveningRows()
    {
        var row = new OrderBrowserRow(Guid.NewGuid(), new DateOnly(2026, 8, 31), new TimeOnly(18, 25), FulfilmentMode.Retrait, OrderStatus.Open, Money.FromCents(1250), null);
        var viewModel = new OrderBrowserRowViewModel(row);

        Assert.AreEqual("18:25", viewModel.PlannedTimeText);
        Assert.AreNotEqual("06:25", viewModel.PlannedTimeText);
    }

    [TestMethod]
    public void SuccessfulM03SettingsSaveRepricesUncommittedM04Draft()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "P1", "Plat", categoryId, Money.FromCents(3000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
            var settingsStore = new MutableSettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero, DeliveryMinMerchandiseTotalTtc = Money.Zero });
            var orders = new DesktopOrderStore();
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(),
                startupSucceeded: true,
                new CatalogueService(new SettingsCatalogueStore(product, categoryId)),
                new BusinessSettingsService(settingsStore),
                new OrderEntryService(new DesktopCatalogue(product, categoryId), settingsStore, orders, new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            var admin = shell.Admin!;
            admin.LoadSettingsAsync().GetAwaiter().GetResult();
            entry.AddConfiguredLine(product, [], [], 1);
            entry.SelectedFulfilment = FulfilmentMode.Retrait;
            entry.SelectedPlannedHour = 11;
            entry.SelectedPlannedMinute = 0;
            entry.PickupDiscountRequested = true;
            entry.RepriceAsync(clearManualOverride: true).GetAwaiter().GetResult();
            Assert.AreEqual(2700L, Money.FromEuros(decimal.Parse(entry.TotalText, CultureInfo.CurrentCulture)).Cents);
            entry.SetManualTotal("2500");
            entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
            Assert.IsTrue(entry.IsManualTotalOverrideActive);

            admin.PickupDiscountRateText = "20";
            Assert.IsTrue(admin.SaveSettingsAsync().GetAwaiter().GetResult().Succeeded);
            Assert.AreEqual(2400L, Money.FromEuros(decimal.Parse(entry.TotalText, CultureInfo.CurrentCulture)).Cents);
            Assert.IsFalse(entry.IsManualTotalOverrideActive);

            entry.SelectedFulfilment = FulfilmentMode.Livraison;
            admin.DeliveryFeeEnabled = true;
            admin.DeliveryFeeAmountText = "1.00";
            Assert.IsTrue(admin.SaveSettingsAsync().GetAwaiter().GetResult().Succeeded);
            Assert.AreEqual(3100L, Money.FromEuros(decimal.Parse(entry.TotalText, CultureInfo.CurrentCulture)).Cents);
        });
    }

    [TestMethod]
    public void SettingsSavePreservesManualOverrideWhenUnchangedOrIrrelevant()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "P1", "Plat", categoryId, Money.FromCents(3000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
            var initial = BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero, DeliveryMinMerchandiseTotalTtc = Money.Zero };
            var settingsStore = new MutableSettingsStore(initial);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new SettingsCatalogueStore(product, categoryId)),
                new BusinessSettingsService(settingsStore),
                new OrderEntryService(new DesktopCatalogue(product, categoryId), settingsStore, new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            var admin = shell.Admin!;
            admin.LoadSettingsAsync().GetAwaiter().GetResult();
            entry.AddConfiguredLine(product, [], [], 1);
            entry.SelectedFulfilment = FulfilmentMode.Retrait;
            entry.SelectedPlannedHour = 11;
            entry.SelectedPlannedMinute = 0;
            entry.SetManualTotal("25");
            entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
            Assert.IsTrue(entry.IsManualTotalOverrideActive);

            Assert.IsTrue(admin.SaveSettingsAsync().GetAwaiter().GetResult().Succeeded);
            Assert.IsTrue(entry.IsManualTotalOverrideActive, "Saving unchanged settings must preserve the manual total.");
            Assert.AreEqual(2500L, Money.FromEuros(decimal.Parse(entry.TotalText, CultureInfo.CurrentCulture)).Cents);

            entry.SelectedFulfilment = FulfilmentMode.Retrait;
            admin.DeliveryMinText = "99.00";
            Assert.IsTrue(admin.SaveSettingsAsync().GetAwaiter().GetResult().Succeeded);
            Assert.IsTrue(entry.IsManualTotalOverrideActive, "Delivery settings are irrelevant to a Retrait draft.");
            Assert.AreEqual(2500L, Money.FromEuros(decimal.Parse(entry.TotalText, CultureInfo.CurrentCulture)).Cents);
        });
    }

    [TestMethod]
    public void SettingsSaveOnlyRepricesTheApplicableUncommittedFulfilmentPath()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "P1", "Plat", categoryId, Money.FromCents(3000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
            var initial = BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero, DeliveryMinMerchandiseTotalTtc = Money.FromCents(3000) };
            var settingsStore = new MutableSettingsStore(initial);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new SettingsCatalogueStore(product, categoryId)),
                new BusinessSettingsService(settingsStore),
                new OrderEntryService(new DesktopCatalogue(product, categoryId), settingsStore, new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            var admin = shell.Admin!;
            admin.LoadSettingsAsync().GetAwaiter().GetResult();

            entry.AddConfiguredLine(product, [], [], 1);
            entry.SelectedFulfilment = FulfilmentMode.Livraison;
            entry.SelectedPlannedHour = 11;
            entry.SelectedPlannedMinute = 0;
            entry.RepriceAsync(clearManualOverride: true).GetAwaiter().GetResult();
            Assert.IsTrue(entry.CanConfirm);
            entry.SetManualTotal("25");
            entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
            Assert.IsTrue(entry.IsManualTotalOverrideActive);

            admin.DeliveryMinText = "30.01";
            Assert.IsTrue(admin.SaveSettingsAsync().GetAwaiter().GetResult().Succeeded);
            Assert.IsFalse(entry.CanConfirm, "A relevant delivery minimum change must revalidate the draft.");
            Assert.IsFalse(entry.IsManualTotalOverrideActive);
            StringAssert.Contains(entry.ValidationMessage, shell.Localized["ValidationDeliveryMinimum"]);
        });
    }

    [TestMethod]
    public void CommittedOrderStateIgnoresLaterSettingsSaves()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "P1", "Plat historique", categoryId, Money.FromCents(3000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
            var initial = BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountMinTotalTtc = Money.Zero, DeliveryMinMerchandiseTotalTtc = Money.Zero };
            var settingsStore = new MutableSettingsStore(initial);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new SettingsCatalogueStore(product, categoryId)),
                new BusinessSettingsService(settingsStore),
                new OrderEntryService(new DesktopCatalogue(product, categoryId), settingsStore, new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            var admin = shell.Admin!;
            admin.LoadSettingsAsync().GetAwaiter().GetResult();
            entry.AddConfiguredLine(product, [], [], 1);
            entry.SelectedFulfilment = FulfilmentMode.Retrait;
            entry.SelectedPlannedHour = 11;
            entry.SelectedPlannedMinute = 0;
            entry.RepriceAsync(clearManualOverride: true).GetAwaiter().GetResult();
            var result = entry.ConfirmAsync().GetAwaiter().GetResult();
            Assert.IsTrue(result!.Succeeded);
            var total = entry.TotalText;
            var lineTotal = entry.Cart[0].LineTotalText;
            var snapshotText = entry.ReloadedOrderDisplay;
            var manual = entry.IsManualTotalOverrideActive;

            admin.PickupDiscountRateText = "50";
            admin.DeliveryMinText = "999.00";
            Assert.IsTrue(admin.SaveSettingsAsync().GetAwaiter().GetResult().Succeeded);

            Assert.AreEqual(total, entry.TotalText);
            Assert.AreEqual(lineTotal, entry.Cart[0].LineTotalText);
            Assert.AreEqual(snapshotText, entry.ReloadedOrderDisplay);
            Assert.AreEqual(manual, entry.IsManualTotalOverrideActive);
        });
    }

    [TestMethod]
    public async Task DisappearingProductMessageFollowsFrAndChineseLocalization()
    {
        var catalogue = new DisappearingCatalogue();
        using var shell = new ShellViewModel(
            new InMemorySelectedCultureStore(), true,
            orderEntryService: new OrderEntryService(catalogue, new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
        var entry = shell.Entry!;
        entry.SelectedProduct = catalogue.Summary;
        await entry.AddSelectedProductAsync();
        Assert.AreEqual(shell.Localized["ProductInactive"], entry.ValidationMessage);

        await shell.ChangeLanguageAsync(shell.Languages.Single(option => option.CultureName == "zh-CN"));
        entry.SelectedProduct = catalogue.Summary;
        await entry.AddSelectedProductAsync();
        Assert.AreEqual(shell.Localized["ProductInactive"], entry.ValidationMessage);
        StringAssert.Contains(entry.ValidationMessage, "商品");
    }

    [TestMethod]
    public void MainWindowM04ControlsDriveQuantityRemoveTimeManualAndNewOrderState()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "P1", "Plat", categoryId, Money.FromCents(1000), 10m, true, true, false, default, default), [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
            var settingsStore = new MutableSettingsStore(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { DeliveryMinMerchandiseTotalTtc = Money.Zero });
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                new CatalogueService(new SettingsCatalogueStore(product, categoryId)),
                new BusinessSettingsService(settingsStore),
                orderEntryService: new OrderEntryService(new DesktopCatalogue(product, categoryId), settingsStore, new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var entry = shell.Entry!;
                entry.AddConfiguredLine(product, [], [], 1);
                entry.AddConfiguredLine(product, [], [], 1);
                window.UpdateLayout();
                var caisse = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]);
                caisse.IsSelected = true;
                window.UpdateLayout();
                Assert.HasCount(2, entry.Cart);

                var firstLine = entry.Cart[0];
                var plus = VisualDescendants<Button>(window).Single(button => button.Content?.ToString() == "+" && ReferenceEquals(button.Tag, firstLine));
                plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(2, firstLine.Quantity);
                window.UpdateLayout();
                StringAssert.Contains(VisualDescendants<TextBlock>(window).Single(text => text.Text.Contains("Quantité", StringComparison.Ordinal) && text.Text.Contains('2')).Text, "2");

                var minus = VisualDescendants<Button>(window).Single(button => button.Content?.ToString() == "−" && ReferenceEquals(button.Tag, firstLine));
                minus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, firstLine.Quantity);
                var remove = VisualDescendants<Button>(window).Single(button => button.Content?.ToString() == "×" && ReferenceEquals(button.Tag, firstLine));
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.HasCount(1, entry.Cart);

                var fulfilment = VisualDescendants<ComboBox>(window).Single(combo => combo.ItemsSource is System.Collections.IEnumerable items && items.Cast<object>().OfType<FulfilmentChoice>().Any());
                fulfilment.SelectedValue = FulfilmentMode.Retrait;
                window.UpdateLayout();
                var discount = VisualDescendants<CheckBox>(window).Single(check => check.Content?.ToString() == shell.Localized["PickupDiscountRequest"]);
                Assert.IsTrue(discount.IsEnabled);
                fulfilment.SelectedValue = FulfilmentMode.Livraison;
                window.UpdateLayout();
                Assert.IsFalse(discount.IsEnabled);
                Assert.IsFalse(entry.PickupDiscountRequested);

                var plannedHour = VisualDescendants<ComboBox>(window).Single(combo => combo.Name == "plannedHourBox");
                var plannedMinute = VisualDescendants<ComboBox>(window).Single(combo => combo.Name == "plannedMinuteBox");
                Assert.IsNull(entry.SelectedPlannedHour);
                Assert.IsNull(entry.SelectedPlannedMinute);
                Assert.IsTrue(entry.CanConfirm, "A fully unset planned time is valid when the fulfilment mode and date are valid.");
                CollectionAssert.AreEqual(
                    new int?[] { null, 11, 12, 13, 14, 18, 19, 20, 21, 22 },
                    plannedHour.Items.Cast<TimeChoice>().Select(choice => choice.Value).ToArray());
                CollectionAssert.AreEqual(
                    new int?[] { null, 0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55 },
                    plannedMinute.Items.Cast<TimeChoice>().Select(choice => choice.Value).ToArray());
                Assert.IsFalse(VisualDescendants<TextBox>(window).Any(textBox => textBox.Name.Contains("plannedTime", StringComparison.OrdinalIgnoreCase)));

                plannedHour.SelectedValue = 11;
                plannedMinute.SelectedValue = 0;
                window.UpdateLayout();
                Assert.AreEqual(new TimeSpan(11, 0, 0), entry.PlannedTime);
                Assert.IsTrue(entry.PlannedTimeValid);
                plannedHour.SelectedValue = 22;
                plannedMinute.SelectedValue = 55;
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                Assert.AreEqual(new TimeSpan(22, 55, 0), entry.PlannedTime);
                Assert.IsTrue(entry.CanConfirm);

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.AreEqual(22, entry.SelectedPlannedHour);
                Assert.AreEqual(55, entry.SelectedPlannedMinute);
                Assert.AreEqual(new TimeSpan(22, 55, 0), entry.PlannedTime);
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.AreEqual(22, entry.SelectedPlannedHour);
                Assert.AreEqual(55, entry.SelectedPlannedMinute);

                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                entry.SetManualTotal("25");
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                Assert.IsTrue(entry.IsManualTotalOverrideActive);
                plannedHour.SelectedValue = 11;
                plannedMinute.SelectedValue = 0;
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                Assert.IsTrue(entry.IsManualTotalOverrideActive, "A time-only change must preserve the manual total.");
                plannedHour.SelectedValue = null;
                window.UpdateLayout();
                Assert.IsNull(entry.PlannedTime);
                Assert.IsTrue(entry.CanConfirm, "Clearing the complete planned time is valid; only a partial selection is invalid.");
                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                plannedHour.SelectedValue = 11;
                plannedMinute.SelectedValue = 0;
                window.UpdateLayout();

                var totalBox = (TextBox)typeof(MainWindow).GetField("orderTotalBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                totalBox.Text = "25";
                totalBox.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                Assert.IsTrue(entry.IsManualTotalOverrideActive);
                entry.ChangeQuantity(entry.Cart[0], 2);
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                Assert.IsFalse(entry.IsManualTotalOverrideActive);

                var first = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(first!.Succeeded);
                var newOrder = VisualDescendants<Button>(window).Single(button => button.Content?.ToString() == shell.Localized["NewOrder"]);
                newOrder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsFalse(entry.IsCommitted);
                entry.AddConfiguredLine(product, [], [], 1);
                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                entry.SelectedPlannedHour = 11;
                entry.SelectedPlannedMinute = 0;
                var second = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(second!.Succeeded);
                Assert.AreNotEqual(first.CommittedOrder!.Id, second.CommittedOrder!.Id);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void MainWindowM04OperatorErgonomicsSupportsCategoryFirstDirectAddAndSavedOrderDiscovery()
    {
        RunOnSta(() =>
        {
            var firstCategoryId = Guid.NewGuid();
            var secondCategoryId = Guid.NewGuid();
            var first = SimpleProduct(Guid.NewGuid(), "P-001", "Plat du jour avec un nom suffisamment long pour tester la largeur", firstCategoryId, "Plats");
            var second = SimpleProduct(Guid.NewGuid(), "P-002", "Boisson", secondCategoryId, "Boissons");
            var catalogue = new MultiCategoryCatalogue(first, second);
            var settings = BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { DeliveryMinMerchandiseTotalTtc = Money.Zero };
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                orderEntryService: new OrderEntryService(catalogue, new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            entry.RefreshAsync().GetAwaiter().GetResult();
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var caisse = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]);
                caisse.IsSelected = true;
                window.UpdateLayout();

                var categories = (ListBox)typeof(MainWindow).GetField("orderCategoriesList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var productsGrid = (DataGrid)typeof(MainWindow).GetField("orderProductsGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var add = (Button)typeof(MainWindow).GetField("orderAddButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var address = (TextBox)typeof(MainWindow).GetField("orderDeliveryAddressBox", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var datePicker = (DatePicker)typeof(MainWindow).GetField("orderPlannedDatePicker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

                Assert.HasCount(3, categories.Items);
                Assert.AreEqual(shell.Localized["Code"], productsGrid.Columns[0].Header?.ToString());
                Assert.AreEqual(shell.Localized["Name"], productsGrid.Columns[1].Header?.ToString());
                Assert.AreEqual(shell.Localized["PriceTtc"], productsGrid.Columns[2].Header?.ToString());
                Assert.IsGreaterThan(145D, address.ActualWidth, "The delivery address must have usable width.");
                Assert.AreEqual(entry.MinimumPlannedDate.Date, datePicker.DisplayDateStart?.Date);

                productsGrid.SelectedItem = entry.Products.Single(product => product.Id == first.Aggregate.Product.Id);
                window.UpdateLayout();
                Assert.IsTrue(add.IsEnabled);
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.HasCount(1, entry.Cart, "A simple product must be added without an options dialog.");

                var productRow = VisualDescendants<DataGridRow>(productsGrid).First(row => row.DataContext is ProductSummary summary && summary.Id == first.Aggregate.Product.Id);
                var doubleClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent,
                    Source = productRow
                };
                productsGrid.RaiseEvent(doubleClick);
                Assert.HasCount(2, entry.Cart, "Double-clicking a simple product must use the same direct-add path.");

                entry.AddConfiguredLine(second, [], [], 1);
                window.UpdateLayout();
                var plusButtons = VisualDescendants<Button>(window).Where(button => button.Content?.ToString() == "+" && button.Tag is OrderEntryCartLineViewModel).ToArray();
                Assert.HasCount(3, plusButtons);
                var firstPlus = plusButtons[0].TranslatePoint(new Point(0, 0), window).X;
                var lastPlus = plusButtons[^1].TranslatePoint(new Point(0, 0), window).X;
                Assert.IsLessThan(1.0D, Math.Abs(firstPlus - lastPlus), "Cart quantity controls must share a fixed right edge.");

                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                entry.PlannedDate = entry.MinimumPlannedDate.AddDays(-1);
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                Assert.IsFalse(entry.PlannedDateValid);
                Assert.IsFalse(entry.CanConfirm);
                StringAssert.Contains(entry.ValidationMessage, shell.Localized["ValidationPlannedDatePast"]);
                entry.PlannedDate = entry.MinimumPlannedDate;
                entry.SelectedPlannedHour = 11;
                entry.SelectedPlannedMinute = 0;
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                var result = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(result!.Succeeded);
                window.UpdateLayout();
                Assert.IsFalse(VisualDescendants<FrameworkElement>(window).Any(element => element.Name is "orderSavedGroup" or "orderReloadIdBox" or "orderReloadedDisplay"), "The old reload display must not be recreated after a successful save.");

                categories.SelectedValue = secondCategoryId;
                entry.RefreshAsync().GetAwaiter().GetResult();
                Assert.AreEqual(secondCategoryId, entry.SelectedCategoryId);
                Assert.AreEqual(second.Aggregate.Product.Id, entry.Products.Single().Id);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CaisseGridWpfDoubleClickEventAddsEachSelectedProductExactlyOnceOnSta()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var first = SimpleProduct(Guid.NewGuid(), "A", "Premier", categoryId, "Plats");
            var second = SimpleProduct(Guid.NewGuid(), "B", "Deuxième", categoryId, "Plats");
            var third = SimpleProduct(Guid.NewGuid(), "C", "Troisième", categoryId, "Plats");
            var catalogue = new DelayedFirstProductCatalogue(first, second, third);
            catalogue.ReleaseFirst();
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                orderEntryService: new OrderEntryService(catalogue, new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            entry.RefreshAsync().GetAwaiter().GetResult();
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var caisse = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]);
                caisse.IsSelected = true;
                window.UpdateLayout();
                var grid = (DataGrid)typeof(MainWindow).GetField("orderProductsGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var firstRow = VisualDescendants<DataGridRow>(grid).Single(row => row.DataContext is ProductSummary summary && summary.Id == first.Aggregate.Product.Id);
                var secondRow = VisualDescendants<DataGridRow>(grid).Single(row => row.DataContext is ProductSummary summary && summary.Id == second.Aggregate.Product.Id);
                var thirdRow = VisualDescendants<DataGridRow>(grid).Single(row => row.DataContext is ProductSummary summary && summary.Id == third.Aggregate.Product.Id);

                void RaiseDoubleClick(DataGridRow row)
                {
                    var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = Control.MouseDoubleClickEvent,
                        Source = row
                    };
                    Assert.AreEqual(1, args.ClickCount, "WPF Control creates new MouseDoubleClick args with the constructor-default click count.");
                    grid.RaiseEvent(args);
                }

                RaiseDoubleClick(firstRow);
                Assert.HasCount(1, entry.Cart);
                Assert.AreEqual(first.Aggregate.Product.Id, entry.Cart[0].Draft.Product.Product.Id);

                // The underlying second mouse-down owns ClickCount == 2; WPF Control
                // synthesizes a separate MouseDoubleClick event for the grid handler.
                var secondMouseDown = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = secondRow
                };
                typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.SetValue(secondMouseDown, 2);
                grid.RaiseEvent(secondMouseDown);
                Assert.HasCount(2, entry.Cart);
                Assert.AreEqual(second.Aggregate.Product.Id, entry.Cart[1].Draft.Product.Product.Id);

                RaiseDoubleClick(thirdRow);
                Assert.HasCount(3, entry.Cart);
                Assert.AreEqual(third.Aggregate.Product.Id, entry.Cart[2].Draft.Product.Product.Id);

                RaiseDoubleClick(firstRow);
                Assert.HasCount(4, entry.Cart, "A separate deliberate double-click of A remains valid.");
                Assert.AreEqual(first.Aggregate.Product.Id, entry.Cart[3].Draft.Product.Product.Id);
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CaisseFastSequentialButtonAddsKeepEveryDistinctGestureWhileProductLoadsOnSta()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var first = SimpleProduct(Guid.NewGuid(), "A", "Premier", categoryId, "Plats");
            var second = SimpleProduct(Guid.NewGuid(), "B", "Deuxième", categoryId, "Plats");
            var third = SimpleProduct(Guid.NewGuid(), "C", "Troisième", categoryId, "Plats");
            var catalogue = new DelayedFirstProductCatalogue(first, second, third);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                orderEntryService: new OrderEntryService(catalogue, new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            entry.RefreshAsync().GetAwaiter().GetResult();
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                var caisse = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]);
                caisse.IsSelected = true;
                window.UpdateLayout();
                var grid = (DataGrid)typeof(MainWindow).GetField("orderProductsGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var add = (Button)typeof(MainWindow).GetField("orderAddButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

                void Add(OrderEntryProduct product)
                {
                    grid.SelectedItem = entry.Products.Single(summary => summary.Id == product.Aggregate.Product.Id);
                    window.UpdateLayout();
                    Assert.IsTrue(add.IsEnabled);
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }

                Add(first);
                Add(second);
                Add(third);
                Assert.IsEmpty(entry.Cart, "The first product lookup is deliberately still pending.");

                catalogue.ReleaseFirst();
                var frame = new System.Windows.Threading.DispatcherFrame();
                window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Assert.HasCount(3, entry.Cart, "Each deliberate Add click must survive the earlier async lookup.");
                CollectionAssert.AreEqual(
                    new[] { first.Aggregate.Product.Id, second.Aggregate.Product.Id, third.Aggregate.Product.Id },
                    entry.Cart.Select(line => line.Draft.Product.Product.Id).ToArray());

                Add(first);
                Assert.HasCount(4, entry.Cart);
                Assert.AreEqual(first.Aggregate.Product.Id, entry.Cart[3].Draft.Product.Product.Id);
                Add(first);
                Assert.HasCount(5, entry.Cart, "A second deliberate Add click of the same product remains valid.");
                Assert.AreEqual(first.Aggregate.Product.Id, entry.Cart[4].Draft.Product.Product.Id);
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); window.Close(); }
        });
    }

    [TestMethod]
    [DataRow(false, 760, 520)]
    [DataRow(true, 760, 520)]
    [DataRow(false, 800, 520)]
    [DataRow(true, 800, 520)]
    [DataRow(false, 980, 680)]
    [DataRow(true, 980, 680)]
    [DataRow(false, 1280, 900)]
    [DataRow(true, 1280, 900)]
    public void CaisseOverflowedCartRevealsEachCompletedGestureInsideActualLayoutClipOnSta(bool doubleClick, int width, int height)
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var products = new[]
            {
                SimpleProduct(Guid.NewGuid(), "A", "Premier", categoryId, "Plats"),
                SimpleProduct(Guid.NewGuid(), "B", "Deuxième", categoryId, "Plats"),
                SimpleProduct(Guid.NewGuid(), "C", "Troisième", categoryId, "Plats")
            };
            var catalogue = new ControlledProductCatalogue(products);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                orderEntryService: new OrderEntryService(catalogue, new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            entry.RefreshAsync().GetAwaiter().GetResult();
            Assert.IsTrue(entry.CanWrite);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = width, Height = height };
            window.Show();
            var previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]).IsSelected = true;
                window.UpdateLayout();
                var grid = (DataGrid)typeof(MainWindow).GetField("orderProductsGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var add = (Button)typeof(MainWindow).GetField("orderAddButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var cart = (ListBox)typeof(MainWindow).GetField("orderCartList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var viewer = VisualDescendants<ScrollViewer>(cart).Single();
                for (var index = 0; index < 12; index++) entry.AddConfiguredLine(products[0], [], [], 1);
                PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                viewer.ScrollToTop();
                PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.IsGreaterThan(0D, viewer.ScrollableHeight, "The initial cart must overflow.");
                var rows = VisualDescendants<DataGridRow>(grid).ToDictionary(row => ((ProductSummary)row.DataContext).Id);
                var seedCount = entry.Cart.Count;
                var sequence = new[] { products[0], products[1], products[2], products[0] };

                void Gesture(OrderEntryProduct product)
                {
                    window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                    {
                        if (doubleClick)
                            grid.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                            {
                                RoutedEvent = Control.MouseDoubleClickEvent,
                                Source = rows[product.Aggregate.Product.Id]
                            });
                        else
                        {
                            grid.SelectedItem = entry.Products.Single(summary => summary.Id == product.Aggregate.Product.Id);
                            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                    }));
                    PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.Input);
                }

                // Both real entry gestures can arrive while the first lookup is pending.
                Gesture(sequence[0]);
                Gesture(sequence[1]);
                Assert.HasCount(seedCount, entry.Cart);
                for (var index = 0; index < sequence.Length; index++)
                {
                    Assert.AreEqual(sequence[index].Aggregate.Product.Id, catalogue.PendingProductId);
                    // A finite normal/render backlog models a slower client. Observe at
                    // Background, after the Input-priority async continuation and normal
                    // WPF Loaded/render work, never at ApplicationIdle.
                    for (var backlog = 0; backlog < 4; backlog++)
                        window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(window.UpdateLayout));
                    window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(catalogue.ReleaseNext));
                    PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.HasCount(seedCount + index + 1, entry.Cart);
                    CollectionAssert.AreEqual(sequence.Take(index + 1).Select(product => product.Aggregate.Product.Id).ToArray(),
                        entry.Cart.Skip(seedCount).Select(line => line.Draft.Product.Product.Id).ToArray(), "Completed mutations must preserve exact gesture identity.");
                    AssertCartLineVisibleThroughLayoutClips(window, cart, viewer, entry.Cart[^1]);
                    if (index + 2 < sequence.Length) Gesture(sequence[index + 2]);
                }
                Assert.IsNull(cart.SelectedItem, "Reveal must preserve cart selection.");
            }
            finally { SynchronizationContext.SetSynchronizationContext(previousContext); window.Close(); }
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CaissePendingCartRevealCannotScrollAfterClearOrCloseOnSta(bool close)
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var product = SimpleProduct(Guid.NewGuid(), "A", "Premier", categoryId, "Plats");
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true,
                orderEntryService: new OrderEntryService(new DesktopCatalogue(product, categoryId), new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            entry.RefreshAsync().GetAwaiter().GetResult();
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 800, Height = 520 };
            window.Show();
            try
            {
                VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]).IsSelected = true;
                window.UpdateLayout();
                var cart = (ListBox)typeof(MainWindow).GetField("orderCartList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var viewer = VisualDescendants<ScrollViewer>(cart).Single();
                var add = (Button)typeof(MainWindow).GetField("orderAddButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                for (var index = 0; index < 12; index++) entry.AddConfiguredLine(product, [], [], 1);
                PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                viewer.ScrollToTop();
                entry.SelectedProduct = entry.Products.Single();
                PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Assert.IsGreaterThan(0D, viewer.ScrollableHeight);
                Assert.AreEqual(0D, viewer.VerticalOffset, 0.01D);
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.HasCount(13, entry.Cart, "The actual button handler added a line with a pending reveal.");
                if (close) window.Close();
                else entry.Cart.Clear();
                PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.Background);
                Assert.AreEqual(0D, viewer.VerticalOffset, 0.01D, "A stale reveal cannot scroll after clear/close.");
                if (!close)
                {
                    Assert.IsEmpty(cart.Items);
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    PumpDispatcher(window, System.Windows.Threading.DispatcherPriority.Background);
                    Assert.HasCount(1, entry.Cart, "A fresh gesture remains usable after the clear.");
                    AssertCartLineVisibleThroughLayoutClips(window, cart, viewer, entry.Cart.Single());
                }
            }
            finally { if (window.IsVisible) window.Close(); }
        });
    }

    [TestMethod]
    public void CaisseCartRevealsOnlyNewlyInsertedLinesOnSta()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var product = SimpleProduct(Guid.NewGuid(), "CART", "Produit du panier", categoryId, "Plats");
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                orderEntryService: new OrderEntryService(new DesktopCatalogue(product, categoryId), new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            entry.RefreshAsync().GetAwaiter().GetResult();
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 800, Height = 520 };
            window.Show();
            try
            {
                var caisse = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]);
                caisse.IsSelected = true;
                window.UpdateLayout();
                var cart = (ListBox)typeof(MainWindow).GetField("orderCartList", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var viewer = VisualDescendants<ScrollViewer>(cart).Single();

                void FlushLayout()
                {
                    window.UpdateLayout();
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    window.UpdateLayout();
                }

                void AssertVisible(OrderEntryCartLineViewModel line)
                {
                    AssertCartLineVisibleThroughLayoutClips(window, cart, viewer, line);
                }

                for (var index = 0; index < 12; index++) entry.AddConfiguredLine(product, [], [], 1);
                FlushLayout();
                Assert.IsGreaterThan(0D, viewer.ScrollableHeight, "The cart must overflow before the reveal assertions.");
                viewer.ScrollToTop();
                FlushLayout();
                Assert.AreEqual(0D, viewer.VerticalOffset, 0.01D);

                entry.AddConfiguredLine(product, [], [], 1);
                var firstNew = entry.Cart[^1];
                FlushLayout();
                AssertVisible(firstNew);
                Assert.IsGreaterThan(0D, viewer.VerticalOffset);

                entry.AddConfiguredLine(product, [], [], 1);
                var secondNew = entry.Cart[^1];
                FlushLayout();
                AssertVisible(secondNew);

                viewer.ScrollToTop();
                FlushLayout();
                entry.ChangeQuantity(entry.Cart[0], 2);
                entry.UpdateConfiguredLine(entry.Cart[0], [], [], quantity: 3);
                entry.RepriceAsync(clearManualOverride: false).GetAwaiter().GetResult();
                entry.RemoveLine(entry.Cart[1]);
                FlushLayout();
                Assert.AreEqual(0D, viewer.VerticalOffset, 0.01D, "Edit, quantity, reprice and removal must not force a bottom jump.");
                Assert.IsNull(cart.SelectedItem, "Auto-reveal must not change cart selection.");
                entry.AddConfiguredLine(product, [], [], 1);
                entry.Cart.Clear();
                FlushLayout();
                Assert.AreEqual(0D, viewer.VerticalOffset, 0.01D, "Clearing the cart must not leave a pending reveal.");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void CaisseOptionDialogConfirmAddsOnceAndCancelAddsNoneOnSta()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var groupId = Guid.NewGuid();
            var optionId = Guid.NewGuid();
            var product = new OrderEntryProduct(new ProductAggregate(
                new Product(productId, "OPT", "Avec option", categoryId, Money.FromCents(1000), 10m, true, true, true, default, default),
                [new OptionGroup(groupId, productId, "Choix", DomainSelectionMode.Single, true, null, null, 0, default, default)],
                new Dictionary<Guid, IReadOnlyList<ProductOption>>
                {
                    [groupId] = [new(optionId, groupId, "Sauce", Money.Zero, true, 0, default, default)]
                }), "Plats");
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true,
                orderEntryService: new OrderEntryService(new DesktopCatalogue(product, categoryId), new DesktopSettingsStore(), new DesktopOrderStore(), new DesktopDispatcher(), new DesktopIds(), new DesktopClock()));
            var entry = shell.Entry!;
            entry.RefreshAsync().GetAwaiter().GetResult();
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };
            window.Show();
            try
            {
                var caisse = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["Caisse"]);
                caisse.IsSelected = true;
                window.UpdateLayout();
                var grid = (DataGrid)typeof(MainWindow).GetField("orderProductsGrid", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var add = (Button)typeof(MainWindow).GetField("orderAddButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                grid.SelectedItem = entry.Products.Single();
                window.UpdateLayout();

                void ClickAndRespond(bool confirm)
                {
                    Exception? dialogFailure = null;
                    window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        Window? dialog = null;
                        try
                        {
                            dialog = window.OwnedWindows.Cast<Window>().Single(owned => owned.GetType().Name == "OptionSelectionDialog");
                            if (confirm)
                            {
                                VisualDescendants<RadioButton>(dialog).Single(option => Equals(option.Tag, optionId)).IsChecked = true;
                                VisualDescendants<Button>(dialog).Single(button => Equals(button.Content, shell.Localized["Add"]))
                                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                            else
                            {
                                VisualDescendants<Button>(dialog).Single(button => Equals(button.Content, shell.Localized["Cancel"]))
                                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }
                        }
                        catch (Exception exception) { dialogFailure = exception; dialog?.Close(); }
                    }));
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (dialogFailure is not null) ExceptionDispatchInfo.Capture(dialogFailure).Throw();
                }

                ClickAndRespond(confirm: true);
                Assert.HasCount(1, entry.Cart);
                CollectionAssert.AreEqual(new[] { optionId }, entry.Cart[0].Draft.SelectedOptionIds.ToArray());

                ClickAndRespond(confirm: false);
                Assert.HasCount(1, entry.Cart, "Cancelling the next option dialog must not add another line.");
            }
            finally { window.Close(); }
        });
    }

    [TestMethod]
    public void OptionDialogControlsAcceptConfiguredMultiSelectionAndCustomAdjustmentOnSta()
    {
        RunOnSta(() =>
        {
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true);
            var owner = new Window { DataContext = shell, ShowInTaskbar = false };
            owner.Show();
            try
            {
                var groupId = Guid.NewGuid();
                var optionA = Guid.NewGuid();
                var optionB = Guid.NewGuid();
                var product = new OrderEntryProduct(new ProductAggregate(
                    new Product(Guid.NewGuid(), "P1", "Plat", Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, true, default, default),
                    [new OptionGroup(groupId, Guid.Empty, "Choix", DomainSelectionMode.Multi, true, 1, 2, 0, default, default)],
                    new Dictionary<Guid, IReadOnlyList<ProductOption>> { [groupId] = [new(optionA, groupId, "A", Money.Zero, true, 0, default, default), new(optionB, groupId, "B", Money.Zero, true, 1, default, default)] }), "Plats");
                var dialogType = typeof(MainWindow).GetNestedType("OptionSelectionDialog", BindingFlags.NonPublic)!;
                var constructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [typeof(Window), typeof(OrderEntryProduct), typeof(OrderEntryCartLineViewModel)], null)!;
                var dialog = (Window)constructor.Invoke([owner, product, null]);
                dialog.ContentRendered += (_, _) =>
                {
                    var options = VisualDescendants<CheckBox>(dialog).Where(check => check.Tag is Guid).Take(2).ToArray();
                    Assert.HasCount(2, options);
                    options[0].IsChecked = true;
                    var addAdjustment = VisualDescendants<Button>(dialog).Single(button => button.Content?.ToString() == "+ " + shell.Localized["AddAdjustment"]);
                    addAdjustment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var textBoxes = VisualDescendants<TextBox>(dialog).ToArray();
                    var label = textBoxes[^2];
                    var amount = textBoxes[^1];
                    label.Text = "Préparation";
                    amount.Text = "0";
                    var add = VisualDescendants<Button>(dialog).Single(button => button.Content?.ToString() == shell.Localized["Add"]);
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                };
                Assert.IsTrue(dialog.ShowDialog());
                var selected = (IReadOnlyList<Guid>)dialogType.GetProperty("SelectedOptionIds")!.GetValue(dialog)!;
                var adjustments = (IReadOnlyList<OrderLineAdjustmentDraft>)dialogType.GetProperty("CustomAdjustments")!.GetValue(dialog)!;
                CollectionAssert.Contains(selected.ToArray(), optionA);
                Assert.HasCount(1, adjustments);
                Assert.AreEqual("Préparation", adjustments[0].Label);
                dialog.Close();
            }
            finally { owner.Close(); }
        });
    }

    private static TextBox FindLabeledTextBox(DependencyObject root, string label)
    {
        foreach (var panel in VisualDescendants<StackPanel>(root))
        {
            if (!VisualDescendants<TextBlock>(panel).Any(text => text.Text == label)) continue;
            var box = VisualDescendants<TextBox>(panel).FirstOrDefault();
            if (box is not null) return box;
        }
        throw new AssertFailedException($"TextBox labelled '{label}' was not found.");
    }

    private static OrderEntryProduct SimpleProduct(Guid id, string code, string name, Guid categoryId, string categoryName) =>
        new(new ProductAggregate(
            new Product(id, code, name, categoryId, Money.FromCents(1000), 10m, true, true, false, default, default),
            [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), categoryName);

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        var children = 0;
        try { children = VisualTreeHelper.GetChildrenCount(root); } catch (InvalidOperationException) { yield break; }
        for (var index = 0; index < children; index++)
            foreach (var descendant in VisualDescendants<T>(VisualTreeHelper.GetChild(root, index))) yield return descendant;
    }

    private static void PumpDispatcher(Window window, System.Windows.Threading.DispatcherPriority priority)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        window.Dispatcher.BeginInvoke(priority, new Action(() => frame.Continue = false));
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    private static void AssertCartLineVisibleThroughLayoutClips(Window window, ListBox cart, ScrollViewer viewer, OrderEntryCartLineViewModel line)
    {
        var container = (ListBoxItem?)cart.ItemContainerGenerator.ContainerFromItem(line);
        Assert.IsNotNull(container, "The completed add must be realized before subsequent input.");
        var bounds = container.TransformToAncestor(window).TransformBounds(new Rect(container.RenderSize));
        var visible = bounds;
        visible.Intersect(viewer.TransformToAncestor(window).TransformBounds(new Rect(viewer.RenderSize)));
        if (window.Content is FrameworkElement content)
            visible.Intersect(content.TransformToAncestor(window).TransformBounds(new Rect(content.RenderSize)));
        for (DependencyObject? ancestor = container; ancestor is not null && !ReferenceEquals(ancestor, window); ancestor = VisualTreeHelper.GetParent(ancestor))
        {
            if (ancestor is not FrameworkElement element) continue;
            var clip = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(element);
            if (clip is not null)
                visible.Intersect(element.TransformToAncestor(window).TransformBounds(clip.Bounds));
            if (element.ClipToBounds)
                visible.Intersect(element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize)));
        }
        var slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(cart);
        Assert.IsGreaterThanOrEqualTo(bounds.Height - 2D, visible.IsEmpty ? 0D : visible.Height,
            $"Completed line is clipped: item={bounds}; visible={visible}; cart nominal={cart.RenderSize}; allocated slot={slot}; viewer={viewer.RenderSize}. Nominal viewport visibility alone is insufficient.");
        Assert.IsGreaterThanOrEqualTo(bounds.Width - 2D, visible.IsEmpty ? 0D : visible.Width, "The new line must also fit the visible width.");
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

    private sealed class DesktopCatalogue(OrderEntryProduct product, Guid categoryId) : IOrderEntryCatalogueQueries
    {
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([new(categoryId, "Plats")]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([new(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name, categoryId, product.CategoryName, product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, product.Aggregate.Product.OptionsEnabled)]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(productId == product.Aggregate.Product.Id ? product : null);
    }

    private sealed class MultiCategoryCatalogue(OrderEntryProduct first, OrderEntryProduct second) : IOrderEntryCatalogueQueries
    {
        private readonly IReadOnlyList<CategorySummary> categories =
        [
            new(first.Aggregate.Product.CategoryId, first.CategoryName),
            new(second.Aggregate.Product.CategoryId, second.CategoryName)
        ];

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult(categories);

        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default)
        {
            var values = new[] { first, second }
                .Where(product => !filterCategoryId.HasValue || product.Aggregate.Product.CategoryId == filterCategoryId.Value)
                .Where(product => string.IsNullOrWhiteSpace(search) || product.Aggregate.Product.Code.Contains(search, StringComparison.OrdinalIgnoreCase) || product.Aggregate.Product.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                .Select(product => new ProductSummary(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name, product.Aggregate.Product.CategoryId, product.CategoryName, product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, false))
                .ToArray();
            return Task.FromResult<IReadOnlyList<ProductSummary>>(values);
        }

        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new[] { first, second }.SingleOrDefault(product => product.Aggregate.Product.Id == productId));
    }

    private sealed class ControlledProductCatalogue(params OrderEntryProduct[] products) : IOrderEntryCatalogueQueries
    {
        private TaskCompletionSource<OrderEntryProduct?>? pending;
        public Guid PendingProductId { get; private set; }
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategorySummary>>([new(products[0].Aggregate.Product.CategoryId, products[0].CategoryName)]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>(products.Select(product => new ProductSummary(product.Aggregate.Product.Id,
                product.Aggregate.Product.Code, product.Aggregate.Product.Name, product.Aggregate.Product.CategoryId, product.CategoryName,
                product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, false)).ToArray());
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            Assert.IsNull(pending, "The window must serialize product lookup requests.");
            PendingProductId = productId;
            pending = new TaskCompletionSource<OrderEntryProduct?>();
            return pending.Task;
        }
        public void ReleaseNext()
        {
            var completion = pending!;
            var product = products.Single(product => product.Aggregate.Product.Id == PendingProductId);
            pending = null;
            completion.SetResult(product);
        }
    }

    private sealed class DelayedFirstProductCatalogue(params OrderEntryProduct[] products) : IOrderEntryCatalogueQueries
    {
        private readonly TaskCompletionSource<OrderEntryProduct?> firstLookup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool firstLookupPending = true;

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CategorySummary>>([new(products[0].Aggregate.Product.CategoryId, products[0].CategoryName)]);

        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>(products.Select(product =>
                new ProductSummary(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name,
                    product.Aggregate.Product.CategoryId, product.CategoryName, product.Aggregate.Product.PriceTtc,
                    product.Aggregate.Product.VatRate, true, true, false)).ToArray());

        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            if (firstLookupPending && productId == products[0].Aggregate.Product.Id)
            {
                firstLookupPending = false;
                return firstLookup.Task;
            }
            return Task.FromResult<OrderEntryProduct?>(products.SingleOrDefault(product => product.Aggregate.Product.Id == productId));
        }

        public void ReleaseFirst() => firstLookup.SetResult(products[0]);
    }

    private sealed class DisappearingCatalogue : IOrderEntryCatalogueQueries
    {
        private readonly Guid productId = Guid.NewGuid();
        public ProductSummary Summary => new(productId, "P1", "Plat", Guid.NewGuid(), "Plats", Money.FromCents(1000), 10m, true, true, false);
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([Summary]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(null);
    }

    private sealed class DesktopSettingsStore : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { DeliveryMinMerchandiseTotalTtc = Money.Zero });
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class MutableSettingsStore(BusinessSettings initial) : IBusinessSettingsStore
    {
        private BusinessSettings current = initial;
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(current);
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) { current = settings; return Task.FromResult(OperationResult.Success()); }
    }

    private sealed class SettingsCatalogueStore(OrderEntryProduct product, Guid categoryId) : ICatalogueStore
    {
        private ProductDraft Draft => new(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name, categoryId, product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, false, []);
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([new(categoryId, product.CategoryName)]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(productId == product.Aggregate.Product.Id ? Draft : null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(id, name)));
        public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name, shortCode)));
        public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid id, string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(id, name, shortCode)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(draft.Id == Guid.Empty ? Guid.NewGuid() : draft.Id));
        public Task<OperationResult> UpdateProductAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 0)));
        public Task<OperationResult> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class DesktopOrderStore : IOrderStore
    {
        private readonly Dictionary<Guid, OrderSnapshot> snapshots = [];
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) { snapshots[snapshot.Id] = snapshot; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(snapshots.GetValueOrDefault(orderId));
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderBrowserRow>>(snapshots.Values
                .Where(snapshot => snapshot.PlannedFulfilmentDate == plannedDate)
                .OrderBy(snapshot => snapshot.PlannedFulfilmentTime is null)
                .ThenBy(snapshot => snapshot.PlannedFulfilmentTime)
                .ThenBy(snapshot => snapshot.Id)
                .Select(snapshot => new OrderBrowserRow(snapshot.Id, snapshot.PlannedFulfilmentDate, snapshot.PlannedFulfilmentTime, snapshot.Fulfilment, snapshot.Status, snapshot.TotalTtc, snapshot.Telephone))
                .ToArray());
    }

    private sealed class DesktopDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DesktopClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 8, 31);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DesktopIds : IIdGenerator
    {
        private int counter;
        public Guid NewId() => Guid.Parse($"10000000-0000-0000-0000-{Interlocked.Increment(ref counter):D12}");
    }
}
