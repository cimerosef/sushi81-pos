using System.Reflection;
using System.Runtime.ExceptionServices;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;
using DomainSelectionMode = Sushi81.Pos.Domain.SelectionMode;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M03DesktopTests
{
    private static readonly string[] FrenchFilters = ["Tous", "Actifs", "Inactifs"];
    private static readonly string[] ChineseFilters = ["全部", "启用", "停用"];
    private static readonly string[] OptionGroupCultures = ["fr-FR", "zh-CN"];
    private static readonly string[] AllProductCodes = ["P-A", "P-I", "D-A"];
    private static readonly string[] ActiveProductCodes = ["P-A", "D-A"];
    private static readonly string[] InactiveProductCodes = ["P-I"];
    private static readonly string[] DessertProductCodes = ["D-A"];
    private static readonly string[] NewProductCodes = ["NEW"];
    private static readonly string[] ActiveDishProductCodes = ["P-A"];
    private static readonly string[] BulkRenderCultures = ["fr-FR", "zh-CN"];
    private static readonly (double Width, double Height, string Name)[] BulkRenderSizes =
        [(980d, 680d, "normal"), (760d, 520d, "minimum"), (1400d, 900d, "large")];

    [TestMethod]
    public async Task M03FiltersExposeLocalizedAllAndStatusValues()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var store = new FakeCatalogueStore(category);
        var viewModel = new M03ShellViewModel(new CatalogueService(store), new BusinessSettingsService(new FakeSettingsStore()));

        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        await viewModel.RefreshAsync();

        Assert.AreEqual(Guid.Empty, viewModel.CategoryFilters[0].Id);
        Assert.AreEqual("Tous", viewModel.CategoryFilters[0].Name);
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("All", viewModel.ActiveFilter);
        CollectionAssert.AreEqual(FrenchFilters, viewModel.StatusFilters.Select(option => option.Label).ToArray());
        Assert.IsTrue(viewModel.HasProducts);

        viewModel.ApplyLocalization("全部", "启用", "停用");
        Assert.AreEqual("全部", viewModel.AllCategoryLabel);
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("All", viewModel.ActiveFilter);
        CollectionAssert.AreEqual(ChineseFilters, viewModel.StatusFilters.Select(option => option.Label).ToArray());
    }

    [TestMethod]
    public async Task FilterSelectionsRemainSemanticAcrossLanguageSwitchAndRefresh()
    {
        var first = new CategorySummary(Guid.NewGuid(), "Plats");
        var second = new CategorySummary(Guid.NewGuid(), "Desserts");
        var store = new MutableCatalogueStore(first, second);
        var viewModel = new M03ShellViewModel(new CatalogueService(store), new BusinessSettingsService(new FakeSettingsStore()));

        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        await viewModel.RefreshAsync();
        viewModel.SelectedCategory = viewModel.CategoryFilters.Single(category => category.Id == first.Id);
        viewModel.ActiveFilter = "Active";
        viewModel.ApplyLocalization("全部", "启用", "停用");

        Assert.AreEqual(first.Id, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("Plats", viewModel.SelectedCategory.Name);
        Assert.AreEqual("Active", viewModel.ActiveFilter);
        Assert.AreEqual("启用", viewModel.StatusFilters.Single(option => option.Key == "Active").Label);
        Assert.AreEqual("全部", viewModel.CategoryFilters[0].Name);

        viewModel.ActiveFilter = "Inactive";
        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        Assert.AreEqual("Inactive", viewModel.ActiveFilter);
        Assert.AreEqual("Inactifs", viewModel.StatusFilters.Single(option => option.Key == "Inactive").Label);

        await viewModel.RefreshAsync();
        Assert.AreEqual(first.Id, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("Inactive", viewModel.ActiveFilter);

        store.Categories.Remove(first);
        await viewModel.RefreshAsync();
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("Inactive", viewModel.ActiveFilter);
    }

    [TestMethod]
    public async Task FreshStateAndLanguageRoundTripKeepAllFiltersSelected()
    {
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore()), new BusinessSettingsService(new FakeSettingsStore()));
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("All", viewModel.ActiveFilter);

        viewModel.ApplyLocalization("全部", "启用", "停用");
        await viewModel.RefreshAsync();
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("All", viewModel.ActiveFilter);
        Assert.AreEqual("全部", viewModel.CategoryFilters[0].Name);
        Assert.AreEqual("全部", viewModel.StatusFilters[0].Label);

        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("All", viewModel.ActiveFilter);
        Assert.AreEqual("Tous", viewModel.CategoryFilters[0].Name);
        Assert.AreEqual("Tous", viewModel.StatusFilters[0].Label);
    }

    [TestMethod]
    public async Task SettingsReloadRestoresUnsavedEditorValues()
    {
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore()), new BusinessSettingsService(new FakeSettingsStore()));
        await viewModel.LoadSettingsAsync();
        viewModel.PickupDiscountRateText = "99";
        await viewModel.LoadSettingsAsync();

        Assert.AreEqual("10", viewModel.PickupDiscountRateText);
    }

    [TestMethod]
    public async Task SettingsSaveRejectsConcurrentSubmission()
    {
        var store = new BlockingSettingsStore();
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore()), new BusinessSettingsService(store));
        await viewModel.LoadSettingsAsync();

        var first = viewModel.SaveSettingsAsync();
        await store.UpdateStarted.Task;
        var second = await viewModel.SaveSettingsAsync();
        Assert.IsFalse(second.Succeeded);

        store.Release.TrySetResult(true);
        Assert.IsTrue((await first).Succeeded);
    }

    [TestMethod]
    public async Task ToggleActionLabelFollowsSelectedProductStateAndLocalization()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore(category)), new BusinessSettingsService(new FakeSettingsStore()));
        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs", "Activer", "Désactiver");
        await viewModel.RefreshAsync();
        viewModel.SelectedProduct = viewModel.Products[0];
        Assert.AreEqual("Désactiver", viewModel.ToggleProductActionLabel);
        viewModel.SelectedProduct = viewModel.Products[0] with { IsActive = false };
        Assert.AreEqual("Activer", viewModel.ToggleProductActionLabel);
        Assert.IsTrue(viewModel.CanToggleProduct);
    }

    [TestMethod]
    public void ProductActionsAreDisabledWithoutSelectionAndEnabledWithSelection()
    {
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore()), new BusinessSettingsService(new FakeSettingsStore()));
        Assert.IsFalse(viewModel.CanEditProduct);
        Assert.IsFalse(viewModel.CanDeleteProduct);
        Assert.IsFalse(viewModel.CanToggleProduct);

        viewModel.SelectedProduct = new ProductSummary(Guid.NewGuid(), "P1", "Product", Guid.NewGuid(), "Plats", Money.FromCents(100), 10m, true, true, false);
        Assert.IsTrue(viewModel.CanEditProduct);
        Assert.IsTrue(viewModel.CanDeleteProduct);
        Assert.IsTrue(viewModel.CanToggleProduct);
    }

    [TestMethod]
    public async Task CategoryFilterBindingKeyIsStableAcrossEmptyLocalizationAndRefresh()
    {
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore()), new BusinessSettingsService(new FakeSettingsStore()));

        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategoryId);
        Assert.AreEqual(Guid.Empty, viewModel.CategoryFilters[0].Id);

        viewModel.ApplyLocalization("全部", "启用", "停用");
        await viewModel.RefreshAsync();

        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategoryId);
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("全部", viewModel.CategoryFilters[0].Name);

        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategoryId);
        Assert.AreEqual("Tous", viewModel.CategoryFilters[0].Name);

        // Simulate the WPF SelectedValue path writing the All key after a transient
        // null selection during ItemsSource replacement.
        viewModel.SelectedCategoryId = null;
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategoryId);
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
    }

    [TestMethod]
    public async Task CategoryFilterBindingKeyPreservesRealCategoryAndFallsBackWhenRemoved()
    {
        var first = new CategorySummary(Guid.NewGuid(), "Plats");
        var second = new CategorySummary(Guid.NewGuid(), "Desserts");
        var store = new MutableCatalogueStore(first, second);
        var viewModel = new M03ShellViewModel(new CatalogueService(store), new BusinessSettingsService(new FakeSettingsStore()));

        await viewModel.RefreshAsync();
        viewModel.SelectedCategoryId = first.Id;
        Assert.AreEqual(first.Id, viewModel.SelectedCategoryId);
        Assert.AreEqual(first.Id, viewModel.SelectedCategory!.Id);

        viewModel.ApplyLocalization("全部", "启用", "停用");
        await viewModel.RefreshAsync();
        Assert.AreEqual(first.Id, viewModel.SelectedCategoryId);
        Assert.AreEqual(first.Id, viewModel.SelectedCategory!.Id);

        store.Categories.Remove(first);
        await viewModel.RefreshAsync();
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategoryId);
        Assert.AreEqual(Guid.Empty, viewModel.SelectedCategory!.Id);
        Assert.AreEqual("全部", viewModel.CategoryFilters[0].Name);
    }

    [TestMethod]
    public async Task StatusFilterBindingKeyIsStableAcrossEmptyLocalizationAndRefresh()
    {
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore()), new BusinessSettingsService(new FakeSettingsStore()));

        Assert.AreEqual("All", viewModel.SelectedStatusKey);
        viewModel.ApplyLocalization("全部", "启用", "停用");
        await viewModel.RefreshAsync();
        Assert.AreEqual("All", viewModel.SelectedStatusKey);
        Assert.AreEqual("全部", viewModel.StatusFilters[0].Label);

        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        Assert.AreEqual("All", viewModel.SelectedStatusKey);
        Assert.AreEqual("Tous", viewModel.StatusFilters[0].Label);

        // Simulate the WPF SelectedValue path writing null during ItemsSource replacement.
        viewModel.SelectedStatusKey = null;
        Assert.AreEqual("All", viewModel.SelectedStatusKey);
        Assert.AreEqual("All", viewModel.ActiveFilter);
    }

    [TestMethod]
    public async Task StatusFilterBindingKeyPreservesActiveAndInactiveAcrossLocalizationAndRefresh()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore(category)), new BusinessSettingsService(new FakeSettingsStore()));

        await viewModel.RefreshAsync();
        viewModel.SelectedStatusKey = "Active";
        viewModel.ApplyLocalization("全部", "启用", "停用");
        await viewModel.RefreshAsync();
        Assert.AreEqual("Active", viewModel.SelectedStatusKey);
        Assert.AreEqual("Active", viewModel.ActiveFilter);
        Assert.AreEqual("启用", viewModel.StatusFilters.Single(option => option.Key == "Active").Label);

        viewModel.SelectedStatusKey = "Inactive";
        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        await viewModel.RefreshAsync();
        Assert.AreEqual("Inactive", viewModel.SelectedStatusKey);
        Assert.AreEqual("Inactive", viewModel.ActiveFilter);
        Assert.AreEqual("Inactifs", viewModel.StatusFilters.Single(option => option.Key == "Inactive").Label);
    }

    [TestMethod]
    public async Task StatusFilterBindingOptionsRemainStableAcrossLocalizationAndRefresh()
    {
        var viewModel = new M03ShellViewModel(new CatalogueService(new FakeCatalogueStore()), new BusinessSettingsService(new FakeSettingsStore()));
        var initial = viewModel.StatusFilters.ToArray();
        var all = initial.Single(option => option.Key == "All");
        var active = initial.Single(option => option.Key == "Active");
        var inactive = initial.Single(option => option.Key == "Inactive");

        viewModel.ApplyLocalization("全部", "启用", "停用");
        await viewModel.RefreshAsync();
        Assert.AreSame(all, viewModel.StatusFilters[0]);
        Assert.AreSame(active, viewModel.StatusFilters[1]);
        Assert.AreSame(inactive, viewModel.StatusFilters[2]);
        Assert.AreEqual("全部", all.Label);
        Assert.AreEqual("启用", active.Label);
        Assert.AreEqual("停用", inactive.Label);

        viewModel.SelectedStatusKey = "Active";
        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        await viewModel.RefreshAsync();
        Assert.AreSame(all, viewModel.StatusFilters[0]);
        Assert.AreSame(active, viewModel.StatusFilters[1]);
        Assert.AreSame(inactive, viewModel.StatusFilters[2]);
        Assert.AreEqual("Active", viewModel.SelectedStatusKey);
        Assert.AreEqual("Actifs", active.Label);

        viewModel.SelectedStatusKey = "Inactive";
        viewModel.ApplyLocalization("全部", "启用", "停用");
        await viewModel.RefreshAsync();
        Assert.AreEqual("Inactive", viewModel.SelectedStatusKey);
        Assert.AreEqual("停用", inactive.Label);
    }

    [TestMethod]
    public async Task LiveStatusAndCategoryFiltersQueryAutomaticallyAndManualRefreshStillForcesReload()
    {
        var first = new CategorySummary(Guid.NewGuid(), "Plats");
        var second = new CategorySummary(Guid.NewGuid(), "Desserts");
        var store = new LiveFilterCatalogueStore(new[] { first, second },
            Product("P-A", "Active dish", first, true),
            Product("P-I", "Inactive dish", first, false),
            Product("D-A", "Active dessert", second, true));
        var viewModel = NewLiveFilterViewModel(store);

        await viewModel.RefreshAsync();
        CollectionAssert.AreEquivalent(AllProductCodes, ProductCodes(viewModel));
        viewModel.SelectedProduct = viewModel.Products.Single(product => product.Code == "P-I");

        viewModel.SelectedStatusKey = "Active";
        await viewModel.FilterRefreshTask;
        CollectionAssert.AreEquivalent(ActiveProductCodes, ProductCodes(viewModel));
        Assert.IsNull(viewModel.SelectedProduct);

        viewModel.SelectedStatusKey = "Inactive";
        await viewModel.FilterRefreshTask;
        CollectionAssert.AreEquivalent(InactiveProductCodes, ProductCodes(viewModel));

        viewModel.SelectedStatusKey = "All";
        await viewModel.FilterRefreshTask;
        CollectionAssert.AreEquivalent(AllProductCodes, ProductCodes(viewModel));

        viewModel.SelectedCategoryId = second.Id;
        await viewModel.FilterRefreshTask;
        CollectionAssert.AreEquivalent(DessertProductCodes, ProductCodes(viewModel));

        var beforeManualRefresh = store.ProductQueries.Count;
        await viewModel.RefreshAsync();
        Assert.HasCount(beforeManualRefresh + 1, store.ProductQueries);
        CollectionAssert.AreEquivalent(DessertProductCodes, ProductCodes(viewModel));
    }

    [TestMethod]
    public async Task LiveSearchDebouncesAndOlderIgnoringCancellationCannotOverwriteLatestResult()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var oldProduct = Product("OLD", "Old result", category, true);
        var newProduct = Product("NEW", "New result", category, true);
        var store = new LiveFilterCatalogueStore(new[] { category }, oldProduct, newProduct);
        var viewModel = NewLiveFilterViewModel(store);

        await viewModel.RefreshAsync();
        store.ClearProductQueries();
        viewModel.SearchText = "o";
        viewModel.SearchText = "ol";
        viewModel.SearchText = "old";
        await viewModel.FilterRefreshTask;

        Assert.HasCount(1, store.ProductQueries);
        Assert.AreEqual("old", store.ProductQueries[0].Search);

        var blockingStore = new LiveFilterCatalogueStore(new[] { category }, oldProduct, newProduct) { BlockOldQueries = true };
        var blockingViewModel = NewLiveFilterViewModel(blockingStore);
        await blockingViewModel.RefreshAsync();
        blockingStore.ClearProductQueries();
        blockingViewModel.SearchText = "old";
        await blockingStore.OldQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var oldTask = blockingViewModel.FilterRefreshTask;

        blockingViewModel.SearchText = "new";
        var latestTask = blockingViewModel.FilterRefreshTask;
        await latestTask;
        blockingStore.ReleaseOldQuery();
        await oldTask;

        Assert.AreEqual("new", blockingStore.ProductQueries[^1].Search);
        CollectionAssert.AreEqual(NewProductCodes, ProductCodes(blockingViewModel));
    }

    [TestMethod]
    public async Task LiveFiltersPreserveSemanticSelectionAcrossFrenchChineseRoundTrip()
    {
        var first = new CategorySummary(Guid.NewGuid(), "Plats");
        var second = new CategorySummary(Guid.NewGuid(), "Desserts");
        var store = new LiveFilterCatalogueStore(new[] { first, second },
            Product("P-A", "Active dish", first, true),
            Product("P-I", "Inactive dish", first, false),
            Product("D-A", "Active dessert", second, true));
        var viewModel = NewLiveFilterViewModel(store);

        await viewModel.RefreshAsync();
        viewModel.SelectedCategoryId = first.Id;
        await viewModel.FilterRefreshTask;
        viewModel.SelectedStatusKey = "Active";
        await viewModel.FilterRefreshTask;
        var queryCount = store.ProductQueries.Count;

        viewModel.ApplyLocalization("全部", "启用", "停用");
        viewModel.ApplyLocalization("Tous", "Actifs", "Inactifs");
        await viewModel.FilterRefreshTask;

        Assert.AreEqual(first.Id, viewModel.SelectedCategoryId);
        Assert.AreEqual("Active", viewModel.SelectedStatusKey);
        Assert.HasCount(queryCount, store.ProductQueries);

        viewModel.SearchText = "active";
        await viewModel.FilterRefreshTask;
        Assert.AreEqual("active", store.ProductQueries[^1].Search);
        CollectionAssert.AreEqual(ActiveDishProductCodes, ProductCodes(viewModel));
    }

    [TestMethod]
    public async Task BulkCaptureWaitsForLatestDebouncedComposedFilterAndUsesExactIds()
    {
        var first = new CategorySummary(Guid.NewGuid(), "Plats");
        var second = new CategorySummary(Guid.NewGuid(), "Desserts");
        var firstActive = Product("P-A", "Active dish", first, true);
        var firstInactive = Product("P-I", "Inactive dish", first, false);
        var secondActive = Product("D-A", "Active dessert", second, true);
        var store = new LiveFilterCatalogueStore(new[] { first, second }, firstActive, firstInactive, secondActive);
        var viewModel = NewLiveFilterViewModel(store);
        await viewModel.RefreshAsync();

        // Search remains debounced while category and status changes schedule immediate
        // refreshes. Capture must wait for the final composed request, not stale Products.
        viewModel.SearchText = "dish";
        viewModel.SelectedCategoryId = first.Id;
        viewModel.SelectedStatusKey = "Active";
        var capture = await viewModel.CaptureBulkProductActiveStateAsync(false);

        Assert.HasCount(1, capture.Items);
        Assert.AreEqual(firstActive.Id, capture.Items[0].ProductId);
        Assert.IsTrue(capture.Items[0].ExpectedIsActive);
    }

    [TestMethod]
    public async Task BulkCaptureIsImmutableAcrossLaterFilterChangesAndEnablementReflectsTargetState()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var active = Product("A", "Active", category, true);
        var inactive = Product("I", "Inactive", category, false);
        var store = new LiveFilterCatalogueStore(new[] { category }, active, inactive);
        var viewModel = NewLiveFilterViewModel(store);
        await viewModel.RefreshAsync();

        Assert.IsTrue(viewModel.CanBulkActivate);
        Assert.IsTrue(viewModel.CanBulkDeactivate);
        var capture = await viewModel.CaptureBulkProductActiveStateAsync(false);
        viewModel.SelectedStatusKey = "Inactive";
        await viewModel.FilterRefreshTask;

        Assert.HasCount(2, capture.Items);
        CollectionAssert.AreEquivalent(new[] { active.Id, inactive.Id }, capture.Items.Select(item => item.ProductId).ToArray());
        Assert.IsTrue(viewModel.CanBulkActivate);
        Assert.IsFalse(viewModel.CanBulkDeactivate);
    }

    [TestMethod]
    public async Task BulkWorkflowCancelAndNoOpPerformNoMutation()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var cancelStore = new WorkflowCatalogueStore(category, Product("A", "Active", category, true), Product("I", "Inactive", category, false));
        var cancelViewModel = NewWorkflowViewModel(cancelStore);
        await cancelViewModel.RefreshAsync();
        var beforeCancel = cancelStore.Products.ToArray();
        var cancelled = await cancelViewModel.ExecuteBulkActiveStateWorkflowAsync(false, confirmation =>
        {
            Assert.AreEqual(2, confirmation.MatchedCount);
            Assert.AreEqual(1, confirmation.ChangedCount);
            return false;
        });
        Assert.AreEqual(M03Presentation.BulkWorkflowOutcome.Cancelled, cancelled.Outcome);
        Assert.IsNull(cancelled.Mutation);
        Assert.AreEqual(0, cancelStore.BulkCalls);
        CollectionAssert.AreEqual(beforeCancel, cancelStore.Products.ToArray());

        var noOpStore = new WorkflowCatalogueStore(category, Product("A", "Active", category, true));
        var noOpViewModel = NewWorkflowViewModel(noOpStore);
        await noOpViewModel.RefreshAsync();
        var noOp = await noOpViewModel.ExecuteBulkActiveStateWorkflowAsync(true, _ => throw new AssertFailedException("no-op must not ask for confirmation"));
        Assert.AreEqual(M03Presentation.BulkWorkflowOutcome.NoOp, noOp.Outcome);
        Assert.IsNull(noOp.Mutation);
        Assert.AreEqual(0, noOpStore.BulkCalls);
    }

    [TestMethod]
    public async Task BulkWorkflowConfirmedDeactivateRefreshesAndClearsProductOutsideActiveFilter()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var active = Product("A", "Active", category, true);
        var inactive = Product("I", "Inactive", category, false);
        var store = new WorkflowCatalogueStore(category, active, inactive);
        var viewModel = NewWorkflowViewModel(store);
        await viewModel.RefreshAsync();
        viewModel.SearchText = "active";
        viewModel.SelectedCategoryId = category.Id;
        viewModel.SelectedStatusKey = "Active";
        await viewModel.FilterRefreshTask;
        viewModel.SelectedProduct = viewModel.Products.Single();

        var workflow = await viewModel.ExecuteBulkActiveStateWorkflowAsync(false, confirmation =>
        {
            Assert.AreEqual(1, confirmation.MatchedCount);
            Assert.AreEqual(1, confirmation.ChangedCount);
            return true;
        });

        Assert.AreEqual(M03Presentation.BulkWorkflowOutcome.Confirmed, workflow.Outcome);
        Assert.IsTrue(workflow.Mutation!.Succeeded);
        Assert.AreEqual(1, store.BulkCalls);
        Assert.AreEqual("active", viewModel.SearchText);
        Assert.AreEqual(category.Id, viewModel.SelectedCategoryId);
        Assert.AreEqual("Active", viewModel.SelectedStatusKey);
        Assert.AreEqual("Active", viewModel.ActiveFilter);
        Assert.IsEmpty(viewModel.Products);
        Assert.IsNull(viewModel.SelectedProduct);
        Assert.IsFalse(store.Products.Single(product => product.Id == active.Id).IsActive);
    }

    [TestMethod]
    public async Task BulkWorkflowConfirmedActivateRefreshesAndClearsProductOutsideInactiveFilter()
    {
        var category = new CategorySummary(Guid.NewGuid(), "Plats");
        var active = Product("A", "Active", category, true);
        var inactive = Product("I", "Inactive", category, false);
        var store = new WorkflowCatalogueStore(category, active, inactive);
        var viewModel = NewWorkflowViewModel(store);
        await viewModel.RefreshAsync();
        viewModel.SearchText = "inactive";
        viewModel.SelectedCategoryId = category.Id;
        viewModel.SelectedStatusKey = "Inactive";
        await viewModel.FilterRefreshTask;
        viewModel.SelectedProduct = viewModel.Products.Single();

        var workflow = await viewModel.ExecuteBulkActiveStateWorkflowAsync(true, confirmation =>
        {
            Assert.AreEqual(1, confirmation.MatchedCount);
            Assert.AreEqual(1, confirmation.ChangedCount);
            return true;
        });

        Assert.AreEqual(M03Presentation.BulkWorkflowOutcome.Confirmed, workflow.Outcome);
        Assert.IsTrue(workflow.Mutation!.Succeeded);
        Assert.AreEqual(1, store.BulkCalls);
        Assert.AreEqual("inactive", viewModel.SearchText);
        Assert.AreEqual(category.Id, viewModel.SelectedCategoryId);
        Assert.AreEqual("Inactive", viewModel.SelectedStatusKey);
        Assert.IsEmpty(viewModel.Products);
        Assert.IsNull(viewModel.SelectedProduct);
        Assert.IsTrue(store.Products.Single(product => product.Id == inactive.Id).IsActive);
    }

    [TestMethod]
    public async Task BulkWorkflowCaptureUsesLatestComposedFilterBeforeConfirmation()
    {
        var first = new CategorySummary(Guid.NewGuid(), "Plats");
        var second = new CategorySummary(Guid.NewGuid(), "Desserts");
        var target = Product("TARGET", "Target dish", first, false);
        var excluded = Product("OTHER", "Other dish", second, false);
        var store = new WorkflowCatalogueStore(first, target, excluded) { CategoriesOverride = [first, second] };
        var viewModel = NewWorkflowViewModel(store);
        await viewModel.RefreshAsync();
        viewModel.SearchText = "target";
        viewModel.SelectedCategoryId = first.Id;
        viewModel.SelectedStatusKey = "Inactive";

        var workflow = await viewModel.ExecuteBulkActiveStateWorkflowAsync(true, confirmation =>
        {
            Assert.AreEqual(1, confirmation.MatchedCount);
            Assert.AreEqual(1, confirmation.ChangedCount);
            return false;
        });

        Assert.AreEqual(M03Presentation.BulkWorkflowOutcome.Cancelled, workflow.Outcome);
        Assert.HasCount(1, workflow.Request.Items);
        Assert.AreEqual(target.Id, workflow.Request.Items[0].ProductId);
        Assert.AreEqual("target", viewModel.SearchText);
        Assert.AreEqual(first.Id, viewModel.SelectedCategoryId);
        Assert.AreEqual("Inactive", viewModel.SelectedStatusKey);
        Assert.AreEqual(0, store.BulkCalls);
    }

    [TestMethod]
    public async Task OlderFullRefreshCannotCommitStaleCategoriesOrClearBusyForLatestRefresh()
    {
        var first = new CategorySummary(Guid.NewGuid(), "Plats");
        var second = new CategorySummary(Guid.NewGuid(), "Desserts");
        var store = new LiveFilterCatalogueStore(new[] { first }, Product("P-A", "Active dish", first, true));
        var viewModel = NewLiveFilterViewModel(store);
        await viewModel.RefreshAsync();

        store.SetCategories(first);
        store.BlockCategoryQueries = true;
        var staleRefresh = viewModel.RefreshAsync();
        await store.FirstCategoryQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

        store.SetCategories(second);
        var latestRefresh = viewModel.RefreshAsync();
        await store.TwoCategoryQueriesStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        store.ReleaseCategoryQueries();

        await Task.WhenAll(staleRefresh, latestRefresh);
        Assert.HasCount(1, viewModel.Categories);
        Assert.AreEqual(second.Id, viewModel.Categories[0].Id);
        Assert.IsFalse(viewModel.IsBusy);
    }

    [TestMethod]
    public void PresentationParsingRejectsInvalidNumericInputAndFormatsLocalizedIssues()
    {
        Assert.IsFalse(M03Presentation.TryParseMoney("not-a-number", "price", out _, out var issue));
        Assert.AreEqual("invalid-number", issue!.StableCode);
        var result = OperationResult.Failure(issue);
        var labels = new Dictionary<string, string> { ["PriceTtc"] = "Prix TTC", ["ValidationInvalidNumber"] = "Nombre invalide", ["ValidationField"] = "Champ" };
        StringAssert.Contains(M03Presentation.FormatIssues(result, labels), "Prix TTC");
        var edit = new CategoryEditBuffer(); edit.BeginCreate(); edit.SetName("Plats"); edit.Cancel(); Assert.IsFalse(edit.IsEditing);
    }

    [TestMethod]
    public void CategoryEditBufferExposesClearCreateRenameActionLifecycle()
    {
        var edit = new CategoryEditBuffer();
        Assert.IsTrue(edit.CanBeginEdit);
        Assert.IsFalse(edit.CanSave);
        Assert.IsFalse(edit.CanCancel);

        edit.BeginCreate();
        Assert.IsTrue(edit.IsEditing);
        Assert.IsNull(edit.CategoryId);
        Assert.AreEqual(string.Empty, edit.Name);
        Assert.IsFalse(edit.CanBeginEdit);
        Assert.IsTrue(edit.CanSave);
        Assert.IsTrue(edit.CanCancel);

        edit.SetName("Plats");
        edit.Cancel();
        Assert.IsFalse(edit.IsEditing);
        Assert.IsTrue(edit.CanBeginEdit);
        Assert.IsFalse(edit.CanSave);
        Assert.IsFalse(edit.CanCancel);
        Assert.AreEqual(string.Empty, edit.Name);

        var categoryId = Guid.NewGuid();
        edit.BeginRename(categoryId, "Desserts");
        Assert.IsTrue(edit.IsEditing);
        Assert.AreEqual(categoryId, edit.CategoryId);
        Assert.AreEqual("Desserts", edit.Name);
        Assert.IsFalse(edit.CanBeginEdit);
        Assert.IsTrue(edit.CanSave);
        Assert.IsTrue(edit.CanCancel);

        edit.CompleteSave();
        Assert.IsFalse(edit.IsEditing);
        Assert.IsTrue(edit.CanBeginEdit);
        Assert.IsFalse(edit.CanSave);
        Assert.IsFalse(edit.CanCancel);
        Assert.IsNull(edit.CategoryId);
    }

    [TestMethod]
    public void ProductEditCancelAndDirtyCloseStateAreExplicit()
    {
        var session = new ProductEditSession<string>("saved");
        session.SetDraft("unsaved"); Assert.IsTrue(session.IsDirty); Assert.IsFalse(session.CanClose);
        session.Cancel(); Assert.AreEqual("saved", session.Draft); Assert.IsTrue(session.CanClose);
        session.SetDraft("new"); session.Commit("committed"); Assert.AreEqual("committed", session.Draft); Assert.IsTrue(session.CanClose);
    }

    [TestMethod]
    public async Task M03FrenchAndChineseResourcesHaveRequiredVisibleKeys()
    {
        var fr = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        var store = new InMemorySelectedCultureStore(); var zh = new ShellViewModel(store, true); await zh.ChangeLanguageAsync(zh.Languages.Single(language => language.CultureName == "zh-CN"));
        var required = new[] { "Catalogue", "Settings", "All", "Active", "Inactive", "BulkActivate", "BulkDeactivate", "BulkConfirm", "BulkNoChange", "BulkSuccess", "ValidationInvalidNumber", "ValidationCategoryDuplicate", "OptionName", "OptionActive" };
        foreach (var key in required) { Assert.IsTrue(fr.Localized.ContainsKey(key)); Assert.IsTrue(zh.Localized.ContainsKey(key)); }
    }

    [TestMethod]
    public async Task BulkActionsAreLocalizedAndThereIsNoBulkDeleteControl()
    {
        var fr = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        var zh = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        await zh.ChangeLanguageAsync(zh.Languages.Single(language => language.CultureName == "zh-CN"));
        Assert.AreEqual("Activer les résultats filtrés", fr.Localized["BulkActivate"]);
        Assert.AreEqual("批量启用筛选结果", zh.Localized["BulkActivate"]);
        Assert.AreEqual("Désactiver les résultats filtrés", fr.Localized["BulkDeactivate"]);
        Assert.AreEqual("批量停用筛选结果", zh.Localized["BulkDeactivate"]);

        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        Assert.AreEqual(1, xaml.Split("Localized[BulkActivate]", StringSplitOptions.None).Length - 1);
        Assert.AreEqual(1, xaml.Split("Localized[BulkDeactivate]", StringSplitOptions.None).Length - 1);
        Assert.IsFalse(xaml.Contains("BulkDelete", StringComparison.Ordinal));
        var codeBehind = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs"));
        StringAssert.Contains(codeBehind, "OnBulkActivate");
        StringAssert.Contains(codeBehind, "OnBulkDeactivate");
    }

    [TestMethod]
    public void BulkButtonsRenderWithoutClippingAtSupportedSizesInFrenchAndChinese()
    {
        RunOnSta(() =>
        {
            foreach (var cultureName in BulkRenderCultures)
            {
                foreach (var size in BulkRenderSizes)
                {
                    var category = new CategorySummary(Guid.NewGuid(), "Plats");
                    var store = new RenderCatalogueStore(category);
                    var shell = new ShellViewModel(
                        new InMemorySelectedCultureStore(),
                        true,
                        new CatalogueService(store),
                        new BusinessSettingsService(new FakeSettingsStore()));
                    if (cultureName == "zh-CN")
                    {
                        shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == cultureName)).GetAwaiter().GetResult();
                    }

                    var window = new MainWindow(shell)
                    {
                        Width = size.Width,
                        Height = size.Height,
                        ShowInTaskbar = false,
                        WindowStartupLocation = WindowStartupLocation.Manual,
                        Left = 0,
                        Top = 0,
                    };
                    try
                    {
                        window.Show();
                        window.UpdateLayout();
                        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        window.UpdateLayout();

                        var buttons = VisualDescendants<Button>(window)
                            .Where(button => button.Visibility == Visibility.Visible)
                            .ToArray();
                        var activate = buttons.Single(button => string.Equals(button.Content?.ToString(), shell.Localized["BulkActivate"], StringComparison.Ordinal));
                        var deactivate = buttons.Single(button => string.Equals(button.Content?.ToString(), shell.Localized["BulkDeactivate"], StringComparison.Ordinal));
                        Assert.IsTrue(activate.IsEnabled, $"{cultureName} {size.Name} activate button is not usable.");
                        Assert.IsTrue(deactivate.IsEnabled, $"{cultureName} {size.Name} deactivate button is not usable.");
                        Assert.IsTrue(activate.IsHitTestVisible && deactivate.IsHitTestVisible, $"{cultureName} {size.Name} bulk buttons are not hit-testable.");
                        AssertRenderedButtonFits(activate, cultureName, size.Name);
                        AssertRenderedButtonFits(deactivate, cultureName, size.Name);

                        Assert.IsFalse(buttons.Any(button => button.Content?.ToString()?.Contains("Bulk", StringComparison.OrdinalIgnoreCase) == true && button.Content?.ToString()?.Contains("Delete", StringComparison.OrdinalIgnoreCase) == true));
                    }
                    finally
                    {
                        window.Close();
                    }
                }
            }
        });
    }

    [TestMethod]
    public async Task BulkConfirmationModelFormatsActionCountsAndNoOpStateForBothLanguages()
    {
        var fr = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        var zh = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        await zh.ChangeLanguageAsync(zh.Languages.Single(language => language.CultureName == "zh-CN"));

        var activate = new M03Presentation.BulkConfirmationModel(TargetIsActive: true, MatchedCount: 5, ChangedCount: 3);
        StringAssert.Contains(activate.Format(fr.Localized), "Activer les produits filtrés");
        StringAssert.Contains(activate.Format(fr.Localized), "Produits correspondants : 5");
        StringAssert.Contains(activate.Format(fr.Localized), "Changements effectifs : 3");
        StringAssert.Contains(activate.Format(zh.Localized), "启用筛选结果中的商品");
        StringAssert.Contains(activate.Format(zh.Localized), "匹配商品数：5");
        StringAssert.Contains(activate.Format(zh.Localized), "实际变更数：3");

        var noOp = new M03Presentation.BulkConfirmationModel(TargetIsActive: false, MatchedCount: 2, ChangedCount: 0);
        Assert.IsFalse(noOp.HasEffectiveChanges);
        StringAssert.Contains(noOp.Format(fr.Localized), "Désactiver les produits filtrés");
    }

    [TestMethod]
    public async Task CatalogueHeadersStayLocalizedAcrossLanguageRoundTripAndUseReadableWidths()
    {
        var fr = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        var zh = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        var headerSet = new CatalogueHeaderSet();
        var keys = new[] { "Code", "Name", "Category", "PriceTtc", "Vat", "Active" };

        headerSet.Apply(fr.Localized);
        CollectionAssert.AreEqual(keys.Select(key => fr.Localized[key]).ToArray(), headerSet.Values.ToArray());

        await zh.ChangeLanguageAsync(zh.Languages.Single(language => language.CultureName == "zh-CN"));
        headerSet.Apply(zh.Localized);
        CollectionAssert.AreEqual(keys.Select(key => zh.Localized[key]).ToArray(), headerSet.Values.ToArray());

        headerSet.Apply(fr.Localized);
        CollectionAssert.AreEqual(keys.Select(key => fr.Localized[key]).ToArray(), headerSet.Values.ToArray());
        Assert.IsTrue(headerSet.Values.All(value => !string.IsNullOrWhiteSpace(value)));

        var xamlPath = Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        var gridStart = xaml.IndexOf("<DataGrid x:Name=\"catalogueGrid\"", StringComparison.Ordinal);
        var gridEnd = xaml.IndexOf("</DataGrid>", gridStart, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, gridStart);
        Assert.IsGreaterThan(gridStart, gridEnd);
        var grid = xaml[gridStart..gridEnd];
        Assert.IsFalse(grid.Contains("RelativeSource={RelativeSource AncestorType=Window}", StringComparison.Ordinal));
        StringAssert.Contains(grid, "Width=\"2*\"");
        StringAssert.Contains(grid, "Width=\"1.5*\"");

        var codeBehind = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs"));
        StringAssert.Contains(codeBehind, "catalogueGrid.Columns[index].Header = values[index]");
        StringAssert.Contains(codeBehind, "await viewModel.ChangeLanguageAsync(language); ApplyCatalogueHeaders();");
    }

    [TestMethod]
    public void ProductEditorAddGroupLifecycleUsesTheIntendedContainerAndDoesNotCrash()
    {
        RunOnSta(() =>
        {
            var catalogueStore = new FakeCatalogueStore(new CategorySummary(Guid.NewGuid(), "Entrées"));
            var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(catalogueStore), new BusinessSettingsService(new FakeSettingsStore()));
            shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
            var admin = shell.Admin!;
            var owner = new Window { DataContext = shell };
            owner.Show();
            var dialogType = typeof(MainWindow).GetNestedType("ProductEditorDialog", BindingFlags.NonPublic)!;
            var constructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [typeof(Window), typeof(M03ShellViewModel), typeof(ProductDraft), typeof(IReadOnlyList<CategorySummary>)], null)!;
            var categories = (IReadOnlyList<CategorySummary>)[new CategorySummary(Guid.NewGuid(), "Entrées")];
            var dialog = (Window)constructor.Invoke([owner, admin, null, categories]);

            Exception? callbackFailure = null;
            dialog.ContentRendered += (_, _) =>
            {
                try
                {
                    var addGroup = VisualDescendants<Button>(dialog).Single(button => string.Equals(button.Content?.ToString(), $"+ {shell.Localized["OptionGroups"]}", StringComparison.Ordinal));
                    addGroup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    dialog.UpdateLayout();

                var groupsPanel = (StackPanel)GetPrivateField(dialog, "groupsPanel");
                Assert.AreEqual(1, groupsPanel.Children.Count);
                Assert.IsInstanceOfType<Border>(groupsPanel.Children[0]);
                var firstBorder = (Border)groupsPanel.Children[0];
                Assert.IsInstanceOfType<StackPanel>(firstBorder.Child);

                var groups = (System.Collections.IList)GetPrivateField(dialog, "groups");
                var firstGroup = groups[0]!;
                var mode = (ComboBox)GetPrivateField(firstGroup, "mode");
                var min = (TextBox)GetPrivateField(firstGroup, "min");
                var max = (TextBox)GetPrivateField(firstGroup, "max");
                Assert.AreEqual(DomainSelectionMode.Single, mode.SelectedValue);
                Assert.AreEqual(2, mode.Items.Count);
                Assert.IsFalse(min.IsEnabled);
                Assert.IsFalse(max.IsEnabled);
                Assert.AreEqual(string.Empty, min.Text);
                Assert.AreEqual(string.Empty, max.Text);

                mode.SelectedValue = DomainSelectionMode.Multi;
                Assert.IsTrue(min.IsEnabled);
                Assert.IsTrue(max.IsEnabled);
                mode.SelectedValue = DomainSelectionMode.Single;
                Assert.IsFalse(min.IsEnabled);
                Assert.IsFalse(max.IsEnabled);

                var addOption = VisualDescendants<Button>(firstBorder).Single(button => string.Equals(button.Content?.ToString(), $"+ {shell.Localized["Options"]}", StringComparison.Ordinal));
                addOption.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                var optionsPanel = (StackPanel)GetPrivateField(firstGroup, "optionsPanel");
                Assert.AreEqual(1, optionsPanel.Children.Count);
                var optionLabels = VisualDescendants<TextBlock>(optionsPanel).Select(text => text.Text).ToArray();
                CollectionAssert.Contains(optionLabels, shell.Localized["OptionName"]);
                CollectionAssert.Contains(optionLabels, shell.Localized["AdjustmentTtc"]);
                Assert.IsTrue(VisualDescendants<CheckBox>(optionsPanel).Any(check => string.Equals(check.Content?.ToString(), shell.Localized["OptionActive"], StringComparison.Ordinal)));
                addOption.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Assert.AreEqual(2, optionsPanel.Children.Count);
                var firstOptionRoot = optionsPanel.Children[0];
                var secondOptionRoot = optionsPanel.Children[1];
                var moveOptionDown = VisualDescendants<Button>(firstOptionRoot).Single(button => string.Equals(button.Content?.ToString(), shell.Localized["MoveDown"], StringComparison.Ordinal));
                moveOptionDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreSame(secondOptionRoot, optionsPanel.Children[0]);
                var deleteOption = VisualDescendants<Button>(optionsPanel.Children[0]).Single(button => string.Equals(button.Content?.ToString(), "×", StringComparison.Ordinal));
                deleteOption.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, optionsPanel.Children.Count);

                addGroup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.UpdateLayout();
                Assert.AreEqual(2, groupsPanel.Children.Count);
                var secondGroup = groups[1]!;
                var secondBorder = (Border)groupsPanel.Children[1];
                var moveUp = VisualDescendants<Button>(secondBorder).Single(button => string.Equals(button.Content?.ToString(), shell.Localized["MoveUp"], StringComparison.Ordinal));
                moveUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreSame(secondGroup, groups[0]);
                Assert.AreSame(secondBorder, groupsPanel.Children[0]);

                var remove = VisualDescendants<Button>((Border)groupsPanel.Children[0]).Single(button => string.Equals(button.Content?.ToString(), shell.Localized["DeletePermanently"], StringComparison.Ordinal));
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, groupsPanel.Children.Count);
                Assert.AreEqual(0, catalogueStore.CreateProductCalls);

                    var cancel = VisualDescendants<Button>(dialog).Single(button => string.Equals(button.Content?.ToString(), shell.Localized["Cancel"], StringComparison.Ordinal));
                    cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.IsFalse(dialog.IsVisible);
                    Assert.AreEqual(0, catalogueStore.CreateProductCalls);
                }
                catch (Exception ex)
                {
                    callbackFailure = ex;
                }
                finally
                {
                    if (dialog.IsVisible) dialog.Close();
                }
            };
            dialog.ShowDialog();
            if (callbackFailure is not null)
            {
                ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            }

            if (owner.IsVisible) owner.Close();
        });
    }

    [TestMethod]
    public void ProductEditorExistingSingleGroupInitializesWithoutFalseDirtyState()
    {
        RunOnSta(() =>
        {
            var categoryId = Guid.NewGuid();
            var catalogueStore = new FakeCatalogueStore(new CategorySummary(categoryId, "Entrées"));
            var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(catalogueStore), new BusinessSettingsService(new FakeSettingsStore()));
            var owner = new Window { DataContext = shell };
            owner.Show();
            var existing = new ProductDraft(Guid.NewGuid(), "TST001", "Produit test", categoryId, Money.FromEuros(8.50m), 10m, true, true, true,
                [new OptionGroupDraft(Guid.NewGuid(), "Choix", DomainSelectionMode.Single, false, null, null, 0, [])]);
            var dialogType = typeof(MainWindow).GetNestedType("ProductEditorDialog", BindingFlags.NonPublic)!;
            var constructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                [typeof(Window), typeof(M03ShellViewModel), typeof(ProductDraft), typeof(IReadOnlyList<CategorySummary>)], null)!;
            var dialog = (Window)constructor.Invoke([owner, shell.Admin!, existing, (IReadOnlyList<CategorySummary>)[new CategorySummary(categoryId, "Entrées")]]);
            Exception? callbackFailure = null;
            dialog.ContentRendered += (_, _) =>
            {
                try
                {
                    Assert.IsFalse((bool)GetPrivateField(dialog, "dirty"));
                }
                catch (Exception ex)
                {
                    callbackFailure = ex;
                }
                finally
                {
                    dialog.Close();
                }
            };
            dialog.ShowDialog();
            if (callbackFailure is not null)
            {
                ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            }

            owner.Close();
        });
    }

    [TestMethod]
    public void ProductEditorOptionGroupLabelsFitFrenchAndChineseAtSupportedWidths()
    {
        RunOnSta(() =>
        {
            foreach (var cultureName in OptionGroupCultures)
            {
                var categoryId = Guid.NewGuid();
                var catalogueStore = new FakeCatalogueStore(new CategorySummary(categoryId, "Entrées"));
                var shell = new ShellViewModel(new InMemorySelectedCultureStore(), true, new CatalogueService(catalogueStore), new BusinessSettingsService(new FakeSettingsStore()));
                if (cultureName == "zh-CN") shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == cultureName)).GetAwaiter().GetResult();
                var owner = new Window { DataContext = shell };
                owner.Show();
                var dialogType = typeof(MainWindow).GetNestedType("ProductEditorDialog", BindingFlags.NonPublic)!;
                var constructor = dialogType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                    [typeof(Window), typeof(M03ShellViewModel), typeof(ProductDraft), typeof(IReadOnlyList<CategorySummary>)], null)!;
                var dialog = (Window)constructor.Invoke([owner, shell.Admin!, null, (IReadOnlyList<CategorySummary>)[new CategorySummary(categoryId, "Entrées")]]);
                Exception? callbackFailure = null;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var addGroup = VisualDescendants<Button>(dialog).Single(button => string.Equals(button.Content?.ToString(), $"+ {shell.Localized["OptionGroups"]}", StringComparison.Ordinal));
                        addGroup.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        dialog.UpdateLayout();
                        var firstBorder = (Border)((StackPanel)GetPrivateField(dialog, "groupsPanel")).Children[0];
                        AssertWpfLayoutFits(firstBorder, cultureName, "normal");
                        var addOption = VisualDescendants<Button>(firstBorder).Single(button => string.Equals(button.Content?.ToString(), $"+ {shell.Localized["Options"]}", StringComparison.Ordinal));
                        addOption.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        dialog.UpdateLayout();
                        AssertWpfLayoutFits(firstBorder, cultureName, "option");
                        AssertPeerButtonHeights(firstBorder, cultureName, "option");
                        dialog.Width = 900;
                        dialog.Height = 800;
                        dialog.UpdateLayout();
                        AssertWpfLayoutFits(firstBorder, cultureName, "large");
                        AssertPeerButtonHeights(firstBorder, cultureName, "large");
                    }
                    catch (Exception ex)
                    {
                        callbackFailure = ex;
                    }
                    finally
                    {
                        dialogType.GetField("closeAllowed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, true);
                        dialog.Close();
                    }
                };
                dialog.ShowDialog();
                if (callbackFailure is not null)
                {
                    ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                }

                owner.Close();
            }
        });
    }

    [TestMethod]
    public void CategoryManagerLayoutUsesContentSizedEditorAndActionRows()
    {
        var sourcePath = Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs");
        var source = File.ReadAllText(sourcePath);
        var start = source.IndexOf("private sealed class CategoryManagerDialog", StringComparison.Ordinal);
        var end = source.IndexOf("private sealed class ProductEditorDialog", start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);

        var dialog = source[start..end];
        StringAssert.Contains(dialog, "var root = new Grid");
        StringAssert.Contains(dialog, "GridUnitType.Star");
        Assert.IsGreaterThanOrEqualTo(4, dialog.Split("Height = GridLength.Auto", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(dialog, "var buttons = new WrapPanel");
        StringAssert.Contains(dialog, "VerticalAlignment = VerticalAlignment.Top");
        StringAssert.Contains(dialog, "MinWidth = 172");
        Assert.IsFalse(dialog.Contains("new DockPanel", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sushi81.Pos.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static object GetPrivateField(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;

    private static M03ShellViewModel NewLiveFilterViewModel(LiveFilterCatalogueStore store) =>
        new(new CatalogueService(store), new BusinessSettingsService(new FakeSettingsStore()));

    private static M03ShellViewModel NewWorkflowViewModel(WorkflowCatalogueStore store) =>
        new(new CatalogueService(store), new BusinessSettingsService(new FakeSettingsStore()));

    private static ProductSummary Product(string code, string name, CategorySummary category, bool active) =>
        new(Guid.NewGuid(), code, name, category.Id, category.Name, Money.FromCents(100), 10m, active, true, false);

    private static string[] ProductCodes(M03ShellViewModel viewModel) => viewModel.Products.Select(product => product.Code).ToArray();

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        int childCount;
        try { childCount = VisualTreeHelper.GetChildrenCount(root); }
        catch (InvalidOperationException) { yield break; }
        for (var index = 0; index < childCount; index++)
        {
            foreach (var child in VisualDescendants<T>(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }

    private static void AssertWpfLayoutFits(DependencyObject root, string cultureName, string sizeName)
    {
        foreach (var textBlock in VisualDescendants<TextBlock>(root).Where(text => text.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(text.Text)))
        {
            var actualWidth = textBlock.ActualWidth;
            var natural = new TextBlock { Text = textBlock.Text, FontFamily = textBlock.FontFamily, FontSize = textBlock.FontSize, FontStretch = textBlock.FontStretch, FontStyle = textBlock.FontStyle, FontWeight = textBlock.FontWeight, TextWrapping = TextWrapping.NoWrap };
            natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.IsGreaterThanOrEqualTo(natural.DesiredSize.Width, actualWidth + 0.5,
                $"{cultureName} {sizeName} TextBlock '{textBlock.Text}' is clipped: actual={actualWidth}, natural={natural.DesiredSize.Width}.");
        }

        foreach (var button in VisualDescendants<Button>(root).Where(button => button.Visibility == Visibility.Visible && button.IsEnabled))
        {
            var actualWidth = button.ActualWidth;
            button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var naturalWidth = Math.Max(0, button.DesiredSize.Width - button.Margin.Left - button.Margin.Right);
            Assert.IsGreaterThanOrEqualTo(naturalWidth, actualWidth + 1.0,
                $"{cultureName} {sizeName} Button '{button.Content}' is clipped: actual={actualWidth}, natural={naturalWidth}.");
        }
    }

    private static void AssertPeerButtonHeights(DependencyObject root, string cultureName, string sizeName)
    {
        foreach (var peerGroup in VisualDescendants<Button>(root)
            .Where(button => button.Visibility == Visibility.Visible && button.IsEnabled)
            .GroupBy(button => button.Parent)
            .Where(group => group.Count() >= 2))
        {
            var heights = peerGroup.Select(button => button.ActualHeight).ToArray();
            var spread = heights.Max() - heights.Min();
            Assert.IsGreaterThanOrEqualTo(-1.0, -spread,
                $"{cultureName} {sizeName} peer buttons have inconsistent heights: spread={spread}; heights={string.Join(",", heights)}.");
        }
    }

    private static void AssertRenderedButtonFits(Button button, string cultureName, string sizeName)
    {
        Assert.IsTrue(button.ActualWidth > 0 && button.ActualHeight > 0, $"{cultureName} {sizeName} button '{button.Content}' is not rendered.");
        button.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var naturalContentWidth = Math.Max(0, button.DesiredSize.Width - button.Margin.Left - button.Margin.Right);
        Assert.IsGreaterThanOrEqualTo(naturalContentWidth, button.ActualWidth + 0.5,
            $"{cultureName} {sizeName} button '{button.Content}' is clipped: actual={button.ActualWidth}, natural={naturalContentWidth}.");

        if (button.Parent is FrameworkElement parent && parent.ActualWidth > 0)
        {
            var origin = button.TranslatePoint(new Point(0, 0), parent);
            Assert.IsLessThanOrEqualTo(parent.ActualWidth + 1.0, origin.X + button.ActualWidth,
                $"{cultureName} {sizeName} button '{button.Content}' exceeds its action-row bounds.");
        }
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

    private sealed class FakeCatalogueStore(CategorySummary? category = null) : ICatalogueStore
    {
        private readonly CategorySummary? category = category;
        public int CreateProductCalls { get; private set; }
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>(category is null ? [] : [category]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>(category is null ? [] : [new(Guid.NewGuid(), "P1", "Product", category.Id, category.Name, Money.FromCents(100), 10m, true, true, false)]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) { CreateProductCalls++; return Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid())); }
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, request.Items.Count(item => item.ExpectedIsActive != request.TargetIsActive))));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class RenderCatalogueStore(CategorySummary category) : ICatalogueStore
    {
        private readonly ProductSummary[] products =
        [
            new(Guid.NewGuid(), "A", "Active product", category.Id, category.Name, Money.FromCents(100), 10m, true, true, false),
            new(Guid.NewGuid(), "I", "Inactive product", category.Id, category.Name, Money.FromCents(200), 10m, false, true, false),
        ];

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([category]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProductSummary>>(products.Where(product => (categoryId is null || product.CategoryId == categoryId) && (active is null || product.IsActive == active)).ToArray());
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid()));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, request.Items.Count(item => item.ExpectedIsActive != request.TargetIsActive))));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class MutableCatalogueStore(params CategorySummary[] initialCategories) : ICatalogueStore
    {
        public List<CategorySummary> Categories { get; } = [.. initialCategories];

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>(Categories.ToArray());
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid()));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, request.Items.Count(item => item.ExpectedIsActive != request.TargetIsActive))));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class WorkflowCatalogueStore(CategorySummary category, params ProductSummary[] initialProducts) : ICatalogueStore
    {
        private readonly List<ProductSummary> products = [.. initialProducts];
        public IReadOnlyList<ProductSummary> Products => products;
        public int BulkCalls { get; private set; }
        public IReadOnlyList<CategorySummary> CategoriesOverride { get; set; } = [category];

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult(CategoriesOverride);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default)
        {
            var trimmedSearch = search?.Trim() ?? string.Empty;
            return Task.FromResult<IReadOnlyList<ProductSummary>>(products.Where(product =>
                (categoryId is null || product.CategoryId == categoryId.Value) &&
                (active is null || product.IsActive == active.Value) &&
                (trimmedSearch.Length == 0 || product.Code.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase) || product.Name.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase))).ToArray());
        }
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid()));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default)
        {
            BulkCalls++;
            var changed = 0;
            foreach (var item in request.Items)
            {
                var index = products.FindIndex(product => product.Id == item.ProductId);
                if (index < 0 || products[index].IsActive != item.ExpectedIsActive)
                    return Task.FromResult(OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "Synthetic conflict.", ValidationCodes.Conflict)));
                if (products[index].IsActive == request.TargetIsActive) continue;
                products[index] = products[index] with { IsActive = request.TargetIsActive };
                changed++;
            }
            return Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, changed)));
        }
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class LiveFilterCatalogueStore : ICatalogueStore
    {
        private readonly List<ProductSummary> products = [];
        private readonly TaskCompletionSource<bool> oldQueryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> categoryQueryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public LiveFilterCatalogueStore(IEnumerable<CategorySummary> initialCategories, params ProductSummary[] initialProducts)
        {
            Categories = [.. initialCategories];
            products.AddRange(initialProducts);
        }

        public List<CategorySummary> Categories { get; }
        public List<ProductQuery> ProductQueries { get; } = [];
        public TaskCompletionSource<bool> OldQueryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> FirstCategoryQueryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> TwoCategoryQueriesStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CategoryQueries { get; private set; }
        public bool BlockCategoryQueries { get; set; }
        public bool BlockOldQueries { get; set; }

        public async Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        {
            var snapshot = Categories.ToArray();
            CategoryQueries++;
            if (BlockCategoryQueries && !FirstCategoryQueryStarted.Task.IsCompleted) FirstCategoryQueryStarted.TrySetResult(true);
            if (BlockCategoryQueries && CategoryQueries >= 3) TwoCategoryQueriesStarted.TrySetResult(true);
            if (BlockCategoryQueries) await categoryQueryRelease.Task;
            return snapshot;
        }

        public async Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default)
        {
            ProductQueries.Add(new(search ?? string.Empty, categoryId, active));
            if (BlockOldQueries && string.Equals(search, "old", StringComparison.Ordinal))
            {
                OldQueryStarted.TrySetResult(true);
                await oldQueryRelease.Task;
            }

            var trimmedSearch = search?.Trim() ?? string.Empty;
            return products.Where(product =>
                (categoryId is null || product.CategoryId == categoryId.Value) &&
                (active is null || product.IsActive == active.Value) &&
                (trimmedSearch.Length == 0 || product.Code.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase) || product.Name.Contains(trimmedSearch, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
        }

        public void ClearProductQueries() => ProductQueries.Clear();
        public void ReleaseOldQuery() => oldQueryRelease.TrySetResult(true);
        public void SetCategories(params CategorySummary[] categories)
        {
            Categories.Clear();
            Categories.AddRange(categories);
        }
        public void ReleaseCategoryQueries() => categoryQueryRelease.TrySetResult(true);

        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid()));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, request.Items.Count(item => item.ExpectedIsActive != request.TargetIsActive))));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());

        public sealed record ProductQuery(string Search, Guid? CategoryId, bool? Active);
    }

    private sealed class FakeSettingsStore : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UnixEpoch));
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class BlockingSettingsStore : IBusinessSettingsStore
    {
        public TaskCompletionSource<bool> UpdateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UnixEpoch));
        public async Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default)
        {
            UpdateStarted.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return OperationResult.Success();
        }
    }
}
