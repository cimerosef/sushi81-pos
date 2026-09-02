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
                Assert.IsFalse(entry.CanConfirm);
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
                Assert.IsFalse(entry.CanConfirm);
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
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? filterCategoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([new(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name, categoryId, product.CategoryName, product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, false)]);
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
