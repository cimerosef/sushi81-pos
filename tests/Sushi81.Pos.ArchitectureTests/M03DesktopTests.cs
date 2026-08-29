using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M03DesktopTests
{
    private static readonly string[] FrenchFilters = ["Tous", "Actifs", "Inactifs"];
    private static readonly string[] ChineseFilters = ["全部", "启用", "停用"];

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
        var required = new[] { "Catalogue", "Settings", "All", "Active", "Inactive", "ValidationInvalidNumber", "ValidationCategoryDuplicate", "OptionName", "OptionActive" };
        foreach (var key in required) { Assert.IsTrue(fr.Localized.ContainsKey(key)); Assert.IsTrue(zh.Localized.ContainsKey(key)); }
    }

    private sealed class FakeCatalogueStore(CategorySummary? category = null) : ICatalogueStore
    {
        private readonly CategorySummary? category = category;
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>(category is null ? [] : [category]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>(category is null ? [] : [new(Guid.NewGuid(), "P1", "Product", category.Id, category.Name, Money.FromCents(100), 10m, true, true, false)]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(Guid.NewGuid()));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
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
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
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
