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
        CollectionAssert.AreEqual(FrenchFilters, viewModel.StatusFilters.Select(option => option.Label).ToArray());
        Assert.IsTrue(viewModel.HasProducts);

        viewModel.ApplyLocalization("全部", "启用", "停用");
        Assert.AreEqual("全部", viewModel.AllCategoryLabel);
        CollectionAssert.AreEqual(ChineseFilters, viewModel.StatusFilters.Select(option => option.Label).ToArray());
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
