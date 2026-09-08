using System.Runtime.ExceptionServices;
using System.Threading;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.GitHubTransport;
using Microsoft.Extensions.Logging.Abstractions;
using System.ComponentModel;
using System.Windows.Threading;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M06DesktopTests
{
    [TestMethod]
    public void RealShellShowsStaleReadOnlySafetyBoundaryAcrossStatesAndSupportedSizesOnSta()
    {
        RunOnSta(() =>
        {
            foreach (var state in new[]
            {
                WriteAuthorityState.Authoritative,
                WriteAuthorityState.NonAuthoritativeReadOnly,
                WriteAuthorityState.Transitioning,
                WriteAuthorityState.RecoveryRequired
            })
            {
                var guard = new TestGuard(state);
                var notifier = new NoOpDurableChangeNotifier();
                var catalogueStore = new EmptyCatalogueStore();
                var settingsStore = new EmptySettingsStore();
                var catalogueService = new CatalogueService(catalogueStore, guard, notifier);
                var settingsService = new BusinessSettingsService(settingsStore, guard, notifier);
                var orderCatalogue = new OrderEntryCatalogueService(catalogueStore);
                var orderStore = new EmptyOrderStore();
                var clock = new FixedClock();
                var ids = new DeterministicIds();
                var entryService = new OrderEntryService(orderCatalogue, settingsStore, orderStore, new NoOpOrderPrintDispatcher(), ids, clock, guard, notifier);
                var lifecycleService = new OrderLifecycleService(orderStore, ids, clock, guard, notifier, orderCatalogue, settingsStore);
                using var shell = new ShellViewModel(
                    new InMemorySelectedCultureStore(),
                    true,
                    catalogueService,
                    settingsService,
                    entryService,
                    lifecycleService,
                    guard,
                    state);

                foreach (var size in new[] { (760d, 520d), (980d, 680d), (1400d, 900d) })
                {
                    var window = new MainWindow(shell)
                    {
                        Width = size.Item1,
                        Height = size.Item2,
                        ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = 0,
                        Top = 0
                    };
                    window.Show();
                    try
                    {
                        window.UpdateLayout();
                        window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                        window.UpdateLayout();

                        var banner = VisualDescendants<TextBlock>(window).Single(text => text.Text == shell.AuthorityStatus && text.Visibility == Visibility.Visible);
                        Assert.IsGreaterThan(0d, banner.ActualWidth, $"{state} banner width at {size.Item1}x{size.Item2}");
                        Assert.IsGreaterThan(0d, banner.ActualHeight, $"{state} banner height at {size.Item1}x{size.Item2}");
                        Assert.IsLessThanOrEqualTo(window.ActualWidth, banner.ActualWidth, "The safety banner must remain hosted by the window.");

                        var search = VisualDescendants<TextBox>(window).FirstOrDefault(textBox => textBox.IsEnabled);
                        Assert.IsNotNull(search, $"{state} must retain a usable read-only consultation/search route.");
                        var create = VisualDescendants<Button>(window).Single(button => Equals(button.Content, shell.Localized["NewProduct"]));

                        var mutationLabels = new HashSet<string>(StringComparer.Ordinal)
                        {
                            shell.Localized["NewProduct"], shell.Localized["Edit"], shell.Localized["Save"], shell.Localized["Confirm"],
                            shell.Localized["Add"], shell.Localized["NewOrder"], shell.Localized["OrderModify"], shell.Localized["OrderSave"],
                            shell.Localized["OrderClose"], shell.Localized["OrderCancel"], shell.Localized["OrderNewFromDetails"],
                            shell.Localized["Activate"], shell.Localized["Deactivate"], shell.Localized["BulkActivate"], shell.Localized["BulkDeactivate"]
                        };
                        var mutationButtons = VisualDescendants<Button>(window)
                            .Where(button => button.Content is string content && mutationLabels.Contains(content))
                            .ToArray();
                        Assert.IsNotEmpty(mutationButtons, $"{state} must render representative mutation controls.");

                        if (state == WriteAuthorityState.Authoritative)
                        {
                            Assert.IsTrue(shell.CanWrite);
                            Assert.IsTrue(shell.Admin!.CanCreateProduct);
                            Assert.IsTrue(create.IsEnabled);
                        }
                        else
                        {
                            Assert.IsFalse(shell.CanWrite);
                            Assert.IsFalse(shell.Admin!.CanCreateProduct);
                            Assert.IsFalse(create.IsEnabled);
                            Assert.IsTrue(mutationButtons.All(button => !button.IsEnabled), $"{state} must disable every representative mutation control.");
                            Assert.IsTrue(shell.AuthorityStatus.Contains("obsolètes", StringComparison.Ordinal));
                            var staleCommand = catalogueService.CreateCategoryAsync("synthetic").GetAwaiter().GetResult();
                            Assert.IsFalse(staleCommand.Succeeded, $"{state} must reject a direct stale application mutation.");
                        }
                    }
                    finally
                    {
                        window.Close();
                    }
                }

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                Assert.IsTrue(shell.AuthorityStatus.Contains("过时", StringComparison.Ordinal));
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                Assert.IsTrue(shell.AuthorityStatus.Contains("obsolètes", StringComparison.Ordinal));
            }
        });
    }

    [TestMethod]
    public void M07ShownMainWindowPreservesActionStateAcrossRefreshAndLocalizationOnSta()
    {
        M07DisasterRecoveryUiTests.AssertShownMainWindowPreservesM07ActionStateAcrossRefreshAndLocalizationOnSta();
    }

    [TestMethod]
    public void M07ShownFailClosedResultsRemainReadOnlyAcrossSafetyMatrixOnSta()
    {
        M07DisasterRecoveryUiTests.AssertShownFailClosedResultsAcrossTheM07SafetyMatrixOnSta();
    }

    [TestMethod]
    public void M07ShownShellPreservesM03M04M05StateAcrossRefreshAndLocalizationOnSta()
    {
        RunOnSta(() =>
        {
            var clock = new FixedClock();
            var categoryId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var category = new CategorySummary(categoryId, "Plats", "P");
            var summary = new ProductSummary(productId, "S81-001", "Saumon", categoryId, category.Name, Money.FromCents(1200), 10m, true, true, false);
            var aggregate = new ProductAggregate(
                new Product(productId, summary.Code, summary.Name, categoryId, summary.PriceTtc, summary.VatRate, true, true, false, default, default),
                [], new Dictionary<Guid, IReadOnlyList<ProductOption>>());
            var entryProduct = new OrderEntryProduct(aggregate, category.Name);
            var now = clock.UtcNow;
            var orderId = Guid.NewGuid();
            var item = new OrderItemSnapshot(
                Guid.NewGuid(), 1, productId, summary.Code, summary.Name, category.Name, summary.PriceTtc, summary.VatRate,
                summary.DiscountEligible, 1, summary.PriceTtc, summary.PriceTtc, []);
            var snapshot = new OrderSnapshot(
                orderId, OrderSourceType.Pos, OrderStatus.Open, now, now, null, null, FulfilmentMode.Retrait,
                clock.BusinessDate, new TimeOnly(12, 0), false, "0601020304", null, "synthetic history",
                summary.PriceTtc, false, false, null, Money.Zero, [item], []) { Reference = "S81-0001" };
            var catalogueStore = new ShownCatalogueStore(category, summary);
            var settingsStore = new ShownSettingsStore();
            var orderStore = new ShownOrderStore(snapshot);
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            var notifier = new NoOpDurableChangeNotifier();
            var catalogue = new CatalogueService(catalogueStore, guard, notifier);
            var settings = new BusinessSettingsService(settingsStore, guard, notifier);
            var orderCatalogue = new ShownEntryCatalogue(category, summary, entryProduct);
            var entryService = new OrderEntryService(orderCatalogue, settingsStore, orderStore, new NoOpOrderPrintDispatcher(), new DeterministicIds(), clock, guard, notifier);
            var lifecycleService = new OrderLifecycleService(orderStore, new DeterministicIds(), clock, guard, notifier, orderCatalogue, settingsStore);
            var runtime = M07DisasterRecoveryUiTests.CreateShownRuntime(guard, AuthorityPhase.NonAuthoritativeReadOnly);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(), true, catalogue, settings, entryService, lifecycleService,
                guard, guard.State, runtime, authorityPhase: AuthorityPhase.NonAuthoritativeReadOnly);

            shell.Admin!.Categories.Add(category);
            shell.Admin.CategoryFilters.Add(category);
            shell.Admin.Products.Add(summary);
            shell.Admin.SearchText = "Saumon";
            shell.Admin.SelectedCategoryId = categoryId;
            shell.Admin.SelectedStatusKey = "Active";
            shell.Admin.SelectedProduct = summary;

            shell.Entry!.Categories.Add(category);
            shell.Entry.Products.Add(summary);
            shell.Entry.SearchText = "Saumon";
            shell.Entry.SelectedCategoryId = categoryId;
            shell.Entry.SelectedFulfilment = FulfilmentMode.Retrait;
            shell.Entry.Telephone = "0601020304";
            shell.Entry.DeliveryAddress = "Rue synthétique";
            shell.Entry.AddConfiguredLine(entryProduct, [], [], 2);
            shell.Entry.SelectedCartLine = shell.Entry.Cart.Single();

            var row = new OrderManagementRowViewModel(new OrderBrowserRow(
                orderId, snapshot.PlannedFulfilmentDate, snapshot.PlannedFulfilmentTime, snapshot.Fulfilment,
                snapshot.Status, snapshot.TotalTtc, snapshot.Telephone) { Reference = snapshot.Reference, Comment = snapshot.Comment });
            shell.Lifecycle!.Orders.Add(row);
            shell.Lifecycle.SearchText = "S81-0001";
            shell.Lifecycle.BrowseDate = clock.BusinessDate.ToDateTime(TimeOnly.MinValue);
            shell.Lifecycle.SelectedRow = row;
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));

            var window = new MainWindow(shell)
            {
                Width = 980,
                Height = 680,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                var beforeAdmin = (shell.Admin.SearchText, shell.Admin.SelectedCategoryId, shell.Admin.SelectedStatusKey, shell.Admin.SelectedProduct?.Id);
                var beforeEntry = (shell.Entry.SearchText, shell.Entry.SelectedCategoryId, shell.Entry.SelectedFulfilment, shell.Entry.Telephone, shell.Entry.Cart.Single().Draft.LineId, shell.Entry.SelectedCartLine?.Draft.Quantity);
                var beforeLifecycle = (shell.Lifecycle.SearchText, shell.Lifecycle.BrowseDate, shell.Lifecycle.SelectedRow?.Id, shell.Lifecycle.SelectedOrder?.Id);

                shell.RefreshAuthorityStateAsync().GetAwaiter().GetResult();
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "fr-FR")).GetAwaiter().GetResult();
                window.UpdateLayout();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

                Assert.AreEqual(beforeAdmin, (shell.Admin.SearchText, shell.Admin.SelectedCategoryId, shell.Admin.SelectedStatusKey, shell.Admin.SelectedProduct?.Id));
                Assert.AreEqual(beforeEntry, (shell.Entry.SearchText, shell.Entry.SelectedCategoryId, shell.Entry.SelectedFulfilment, shell.Entry.Telephone, shell.Entry.Cart.Single().Draft.LineId, shell.Entry.SelectedCartLine?.Draft.Quantity));
                Assert.AreEqual(beforeLifecycle, (shell.Lifecycle.SearchText, shell.Lifecycle.BrowseDate, shell.Lifecycle.SelectedRow?.Id, shell.Lifecycle.SelectedOrder?.Id));
                Assert.AreEqual(0, catalogueStore.MutationCount);
                Assert.AreEqual(0, settingsStore.MutationCount);
                Assert.AreEqual(0, orderStore.MutationCount);
                Assert.IsFalse(shell.CanWrite);
            }
            finally
            {
                window.Hide();
                runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [TestMethod]
    public void RealStaWindowCloseFlushesPendingRecoveryBeforeCompleting()
    {
        RunOnSta(() =>
        {
            var snapshots = new RecordingSnapshotService();
            var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
            scheduler.NotifyCommitted(new DurableChange(41, DateTimeOffset.UtcNow));

            Exception? callbackFailure = null;
            var window = new Window { Width = 760, Height = 520, ShowInTaskbar = false };
            var closeCoordinator = new AsyncCloseCoordinator(
                scheduler.DisposeAsync,
                () => { _ = window.Dispatcher.BeginInvoke(new Action(window.Close)); },
                exception => callbackFailure = exception);
            window.Closing += (_, closing) => _ = closeCoordinator.HandleClosingAsync(closing);
            window.Show();
            window.Close();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (window.IsVisible && DateTime.UtcNow < deadline)
            {
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            }

            Assert.IsFalse(window.IsVisible, "The close orchestration must complete within the bounded test window.");
            Assert.IsTrue(closeCoordinator.IsComplete);
            Assert.IsNull(callbackFailure);
            Assert.HasCount(1, snapshots.Changes);
            Assert.AreEqual(41L, snapshots.Changes[0].Sequence);
        });
    }

    [TestMethod]
    public void M07CloseCancelLeavesTheRealStaWindowOpenWithoutFlush()
    {
        RunOnSta(() =>
        {
            var window = new Window { Width = 320, Height = 180, ShowInTaskbar = false };
            var flushCount = 0;
            var finalCloseCount = 0;
            var coordinator = new MainWindowCloseCoordinator(
                () => true,
                () => Task.FromResult(new MainWindowCloseRequest(MainWindowCloseIntent.Cancel)),
                _ => Task.FromResult(false),
                () => { flushCount++; return ValueTask.CompletedTask; },
                () => finalCloseCount++,
                _ => Assert.Fail("Cancel must not report an error."));
            var args = new CancelEventArgs();
            coordinator.HandleClosingAsync(args).GetAwaiter().GetResult();

            Assert.IsTrue(args.Cancel);
            Assert.IsFalse(coordinator.IsFinalCloseAllowed);
            Assert.AreEqual(0, flushCount);
            Assert.AreEqual(0, finalCloseCount);
            Assert.IsFalse(window.IsVisible, "The arbiter must not close a window when the user cancels before it is shown.");
        });
    }

    [TestMethod]
    public void M07CloseRetainUsesOneStaCloseContinuationAndFlushesOnce()
    {
        RunOnSta(() =>
        {
            var order = new List<string>();
            var window = new Window { Width = 320, Height = 180, ShowInTaskbar = false };
            var coordinator = new MainWindowCloseCoordinator(
                () => true,
                () => Task.FromResult(new MainWindowCloseRequest(MainWindowCloseIntent.Retain)),
                _ => Task.FromResult(false),
                () => { order.Add("flush"); return ValueTask.CompletedTask; },
                () => { order.Add("close"); window.Dispatcher.BeginInvoke(new Action(window.Close)); },
                exception => Assert.Fail(exception.Message));
            window.Closing += (_, args) => _ = coordinator.HandleClosingAsync(args);
            window.Show();
            window.Close();
            PumpUntilClosed(window);

            Assert.AreEqual("flush|close", string.Join("|", order));
            Assert.IsTrue(coordinator.IsFinalCloseAllowed);
            Assert.IsFalse(window.IsVisible);
        });
    }

    [TestMethod]
    public void M07CloseTransferOrdersTransferBeforeFlushAndFinalClose()
    {
        RunOnSta(() =>
        {
            var order = new List<string>();
            var window = new Window { Width = 320, Height = 180, ShowInTaskbar = false };
            var target = Guid.NewGuid();
            var coordinator = new MainWindowCloseCoordinator(
                () => true,
                () => { order.Add("intent"); return Task.FromResult(new MainWindowCloseRequest(MainWindowCloseIntent.Transfer, target)); },
                id => { Assert.AreEqual(target, id); order.Add("transfer"); return Task.FromResult(true); },
                () => { order.Add("flush"); return ValueTask.CompletedTask; },
                () => { order.Add("close"); window.Dispatcher.BeginInvoke(new Action(window.Close)); },
                exception => Assert.Fail(exception.Message));
            window.Closing += (_, args) => _ = coordinator.HandleClosingAsync(args);
            window.Show();
            window.Close();
            PumpUntilClosed(window);

            Assert.AreEqual("intent|transfer|flush|close", string.Join("|", order));
        });
    }

    [TestMethod]
    public void M07CloseTransferFailureKeepsWindowOpenAndDoesNotFlush()
    {
        RunOnSta(() =>
        {
            var window = new Window { Width = 320, Height = 180, ShowInTaskbar = false };
            var flushCount = 0;
            var finalCloseCount = 0;
            var coordinator = new MainWindowCloseCoordinator(
                () => true,
                () => Task.FromResult(new MainWindowCloseRequest(MainWindowCloseIntent.Transfer, Guid.NewGuid())),
                _ => Task.FromResult(false),
                () => { flushCount++; return ValueTask.CompletedTask; },
                () => finalCloseCount++,
                _ => { });
            var args = new CancelEventArgs();
            coordinator.HandleClosingAsync(args).GetAwaiter().GetResult();

            Assert.IsTrue(args.Cancel);
            Assert.IsFalse(coordinator.IsFinalCloseAllowed);
            Assert.AreEqual(0, flushCount);
            Assert.AreEqual(0, finalCloseCount);
        });
    }

    [TestMethod]
    public void M07CloseRepeatedRequestsAreReentrantSafeWhileFlushIsPending()
    {
        RunOnSta(() =>
        {
            var flush = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var finalCloseCount = 0;
            var coordinator = new MainWindowCloseCoordinator(
                () => false,
                () => Task.FromResult(new MainWindowCloseRequest(MainWindowCloseIntent.Cancel)),
                _ => Task.FromResult(false),
                () => new ValueTask(flush.Task),
                () => finalCloseCount++,
                _ => Assert.Fail("The non-authoritative close must not report an error."));
            var first = new CancelEventArgs();
            var second = new CancelEventArgs();
            var firstTask = coordinator.HandleClosingAsync(first);
            coordinator.HandleClosingAsync(second).GetAwaiter().GetResult();

            Assert.IsTrue(first.Cancel);
            Assert.IsTrue(second.Cancel);
            Assert.IsTrue(coordinator.IsCloseInProgress);
            Assert.AreEqual(0, finalCloseCount);
            flush.SetResult(null);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (finalCloseCount == 0 && DateTime.UtcNow < deadline)
                Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            firstTask.GetAwaiter().GetResult();
            Assert.AreEqual(1, finalCloseCount);
        });
    }

    [TestMethod]
    public void M07CloseFlushFailureIsReportedButDoesNotCreateASecondClosePath()
    {
        RunOnSta(() =>
        {
            var reportCount = 0;
            var finalCloseCount = 0;
            var coordinator = new MainWindowCloseCoordinator(
                () => false,
                () => Task.FromResult(new MainWindowCloseRequest(MainWindowCloseIntent.Cancel)),
                _ => Task.FromResult(false),
                () => ValueTask.FromException(new IOException("synthetic flush failure")),
                () => finalCloseCount++,
                _ => reportCount++);
            coordinator.HandleClosingAsync(new CancelEventArgs()).GetAwaiter().GetResult();

            Assert.AreEqual(1, reportCount);
            Assert.AreEqual(1, finalCloseCount);
            Assert.IsTrue(coordinator.IsFinalCloseAllowed);
        });
    }

    [TestMethod]
    public void M07ReadOnlyShellRendersAcquisitionAndConnectionActionsInFrenchAndChineseOnSta()
    {
        RunOnSta(() =>
        {
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            var store = new EmptyAuthorityStateStore();
            var metadata = new EmptySystemMetadataStore();
            var runtime = new M07RuntimeServices(
                guard,
                store,
                metadata,
                new SelfJoinService(store, guard, metadata, new FixedClock()),
                null,
                null,
                null,
                GitHubConnectionSetupState.RepositoryNotConfigured);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(),
                true,
                authorityGuard: guard,
                authorityState: WriteAuthorityState.NonAuthoritativeReadOnly,
                m07Runtime: runtime);
            var window = new MainWindow(shell) { Width = 760, Height = 520, ShowInTaskbar = false };
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

            var buttons = VisualDescendants<Button>(window).Where(button => button.Visibility == Visibility.Visible).Select(button => button.Content as string).ToArray();
            CollectionAssert.Contains(buttons, shell.Localized["M07AcquireAuthority"]);
            CollectionAssert.Contains(buttons, shell.Localized["M07ConnectionTest"]);
            Assert.IsTrue(shell.CanAcquireTransferredAuthority);
            Assert.IsTrue(shell.CanTestGitHubConnection);

            var connection = shell.TestGitHubConnectionAsync().GetAwaiter().GetResult();
            Assert.IsNotNull(connection);
            Assert.AreEqual(GitHubConnectionFailureKind.NotConfigured, connection!.FailureKind);
            Assert.AreEqual(shell.Localized["M07ConnectionNotConfigured"], shell.M07OperationStatus);

            shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
            Assert.AreEqual(shell.Localized["M07ConnectionNotConfigured"], shell.M07OperationStatus);
            window.Close();
        });
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void PumpUntilClosed(Window window)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (window.IsVisible && DateTime.UtcNow < deadline)
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        Assert.IsFalse(window.IsVisible, "The close continuation must complete within the bounded STA test window.");
    }

    private sealed class TestGuard(WriteAuthorityState state) : IWriteAuthorityGuard
    {
        public WriteAuthorityState State { get; } = state;
        public void RequireWriteAuthority()
        {
            if (State != WriteAuthorityState.Authoritative) throw new WriteAuthorityException(State);
        }
    }

    private sealed class ShownCatalogueStore(CategorySummary category, ProductSummary product) : ICatalogueStore
    {
        public int MutationCount { get; private set; }
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([category]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([product]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(new(product.Id, product.Code, product.Name, product.CategoryId, product.PriceTtc, product.VatRate, product.IsActive, product.DiscountEligible, product.OptionsEnabled, []));
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid categoryId, string name, string? shortCode, CancellationToken cancellationToken = default) => Mutate(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Mutate(OperationResult<Guid>.Success(product.Id));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Mutate(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Mutate(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Mutate(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 0)));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Mutate(OperationResult.Success());
        private Task<T> Mutate<T>(T result) { MutationCount++; return Task.FromResult(result); }
    }

    private sealed class ShownEntryCatalogue(CategorySummary category, ProductSummary product, OrderEntryProduct entryProduct) : IOrderEntryCatalogueQueries
    {
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([category]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([product]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(entryProduct);
    }

    private sealed class ShownSettingsStore : IBusinessSettingsStore
    {
        public int MutationCount { get; private set; }
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) { MutationCount++; return Task.FromResult(OperationResult.Success()); }
    }

    private sealed class ShownOrderStore(OrderSnapshot snapshot) : IOrderStore
    {
        public int MutationCount { get; private set; }
        public Task SaveAsync(OrderSnapshot value, CancellationToken cancellationToken = default) { MutationCount++; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(snapshot.Id == orderId ? snapshot : null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderBrowserRow>>([new OrderBrowserRow(snapshot.Id, snapshot.PlannedFulfilmentDate, snapshot.PlannedFulfilmentTime, snapshot.Fulfilment, snapshot.Status, snapshot.TotalTtc, snapshot.Telephone) { Reference = snapshot.Reference, Comment = snapshot.Comment }]);
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
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid()));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 0)));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class EmptySettingsStore : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class EmptyOrderStore : IOrderStore
    {
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
    }

    private sealed class NoOpOrderPrintDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 6);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class RecordingSnapshotService : ILocalRecoverySnapshotService
    {
        public List<DurableChange> Changes { get; } = [];

        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.FromResult(new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1));
        }
    }

    private sealed class EmptyAuthorityStateStore : IAuthorityStateStore
    {
        public Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthorityStateDocument?>(null);
        public Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptySystemMetadataStore : ISystemMetadataStore
    {
        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceRegistrationArtifact>>([]);
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Task.FromResult<ValidatedReadOnlySeed?>(null);
        public Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
