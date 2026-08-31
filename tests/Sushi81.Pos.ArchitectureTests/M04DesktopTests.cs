using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
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
    public void MainWindowM04LifecycleRendersLocalizedChoicesQuantityAndReloadSnapshot()
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

                var first = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(first!.Succeeded);
                var firstId = first.CommittedOrder!.Id;
                entry.StartNewOrder();
                entry.AddConfiguredLine(product, [], [], 1);
                entry.SelectedFulfilment = FulfilmentMode.Retrait;
                var second = entry.ConfirmAsync().GetAwaiter().GetResult();
                Assert.IsTrue(second!.Succeeded);
                Assert.AreNotEqual(firstId, second.CommittedOrder!.Id);

                entry.ReloadOrderIdText = firstId.ToString();
                Assert.IsTrue(entry.ReloadOrderAsync().GetAwaiter().GetResult());
                window.UpdateLayout();
                var snapshotText = VisualDescendants<TextBlock>(window).Single(text => text.Text.Contains(firstId.ToString(), StringComparison.Ordinal));
                StringAssert.Contains(snapshotText.Text, firstId.ToString());
                StringAssert.Contains(snapshotText.Text, "Total TTC");

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.AreEqual("自取", ((FulfilmentChoice)fulfilment.Items[1]).Label);
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                Assert.AreEqual("Retrait", ((FulfilmentChoice)fulfilment.Items[1]).Label);
            }
            finally { window.Close(); }
        });
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
