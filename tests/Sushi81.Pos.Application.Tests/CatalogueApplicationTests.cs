using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class CatalogueApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task InvalidCreateIsRejectedBeforeStoreAndTransaction()
    {
        var store = new FakeCatalogueStore();
        var service = new CatalogueService(store);
        var result = await service.CreateProductAsync(new ProductDraft(Guid.Empty, " ", "Name", Guid.NewGuid(), Money.Zero, 10m, true, true, false, []));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("product", result.Issues[0].Field);
        Assert.AreEqual(0, store.CreateProductCalls);
    }

    [TestMethod]
    public async Task InvalidOrderingAndRequiredOptionsReturnFieldValidation()
    {
        var store = new FakeCatalogueStore();
        var service = new CatalogueService(store);
        var category = Guid.NewGuid();
        var draft = new ProductDraft(Guid.Empty, "P1", "Product", category, Money.Zero, 10m, true, true, true,
            [new OptionGroupDraft(Guid.Empty, "Required", SelectionMode.Single, true, null, null, 0, [])]);

        var result = await service.CreateProductAsync(draft);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("options", result.Issues[0].Field);
        Assert.AreEqual(0, store.CreateProductCalls);
    }

    [TestMethod]
    public async Task ValidAggregateIsForwardedWithoutReplacingDraftIds()
    {
        var store = new FakeCatalogueStore { CreatedProductId = Guid.NewGuid() };
        var service = new CatalogueService(store);
        var product = Guid.NewGuid();
        var draft = new ProductDraft(product, "P1", "Product", Guid.NewGuid(), Money.FromCents(125), 20m, true, false, false, []);

        var result = await service.UpdateProductAsync(product, draft);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual(1, store.UpdateProductCalls);
        Assert.AreEqual(product, store.LastUpdateId);
        Assert.AreEqual(product, store.LastDraft.Id);
    }

    [TestMethod]
    public async Task SettingsServiceValidatesBeforeDelegatingAndForwardsValidUpdate()
    {
        var store = new FakeSettingsStore();
        var service = new BusinessSettingsService(store);
        var invalid = BusinessSettings.Defaults(Now) with { PickupDiscountRate = 1.1m };
        Assert.IsFalse((await service.UpdateAsync(invalid)).Succeeded);
        Assert.AreEqual(0, store.UpdateCalls);

        var valid = BusinessSettings.Defaults(Now) with { PickupDiscountRate = 0.125m };
        Assert.IsTrue((await service.UpdateAsync(valid)).Succeeded);
        Assert.AreEqual(1, store.UpdateCalls);
        Assert.AreEqual(valid, store.LastSettings);
    }

    [TestMethod]
    public async Task CategoryMutationsAndInvalidUpdateStayInApplicationBoundary()
    {
        var store = new FakeCatalogueStore();
        var service = new CatalogueService(store);
        Assert.IsTrue((await service.CreateCategoryAsync(" Plats ")).Succeeded);
        Assert.IsTrue((await service.RenameCategoryAsync(Guid.NewGuid(), "Entrées")).Succeeded);
        var invalid = await service.UpdateProductAsync(Guid.NewGuid(), new ProductDraft(Guid.Empty, " ", "N", Guid.NewGuid(), Money.Zero, 10m, true, true, false, []));
        Assert.IsFalse(invalid.Succeeded);
        Assert.AreEqual(0, store.UpdateProductCalls);
    }

    [TestMethod]
    public void ValidationIssuesExposeStableCodesForPresentation()
    {
        var issue = new ValidationIssue("price", "Product price cannot be negative.");
        Assert.AreEqual("price-negative", issue.StableCode);
    }

    [TestMethod]
    public async Task CompleteAggregateCreateUpdateAndBoundaryCommandsAreMapped()
    {
        var store = new FakeCatalogueStore { CreatedProductId = Guid.NewGuid() }; var service = new CatalogueService(store);
        var category = Guid.NewGuid();
        var draft = new ProductDraft(Guid.Empty, "P", "Product", category, Money.FromCents(125), 20m, true, false, true,
            [new OptionGroupDraft(Guid.Empty, "Extras", SelectionMode.Multi, false, 0, 2, 0, [new OptionDraft(Guid.Empty, "Plus", Money.FromCents(-25), true, 1)])]);
        var created = await service.CreateProductAsync(draft); Assert.IsTrue(created.Succeeded); Assert.AreEqual(draft.Name, store.LastDraft.Name); Assert.AreEqual(1, store.CreateProductCalls); var createdId = created.Value;
        var update = await service.UpdateProductAsync(createdId, draft with { Id = createdId, Code = "P2" }); Assert.IsTrue(update.Succeeded); Assert.AreEqual(createdId, store.LastDraft.Id);
        Assert.IsTrue((await service.SetProductActiveAsync(createdId, false)).Succeeded); Assert.IsTrue((await service.DeleteProductAsync(createdId)).Succeeded);
        Assert.AreEqual(1, store.SetActiveCalls); Assert.AreEqual(1, store.DeleteCalls);
    }

    [TestMethod]
    public async Task BulkActiveStateRequestReturnsCountsAndSkipsNoOpWithoutStoreWrite()
    {
        var store = new FakeCatalogueStore();
        var service = new CatalogueService(store);
        var active = Guid.NewGuid();
        var inactive = Guid.NewGuid();
        var request = new BulkProductActiveStateRequest(true,
            [new BulkProductActiveStateItem(active, true), new BulkProductActiveStateItem(inactive, false)]);

        var result = await service.BulkSetProductsActiveAsync(request);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual(2, result.Value!.MatchedCount);
        Assert.AreEqual(1, result.Value.ChangedCount);
        Assert.AreEqual(1, store.BulkCalls);

        var noOp = await service.BulkSetProductsActiveAsync(new BulkProductActiveStateRequest(true, [new(active, true)]));
        Assert.IsTrue(noOp.Succeeded, noOp.ErrorMessage);
        Assert.AreEqual(0, noOp.Value!.ChangedCount);
        Assert.AreEqual(1, store.BulkCalls);
    }

    [TestMethod]
    public async Task BulkActiveStateRejectsDuplicateAndInvalidIdsDeterministically()
    {
        var store = new FakeCatalogueStore();
        var service = new CatalogueService(store);
        var id = Guid.NewGuid();

        var duplicate = await service.BulkSetProductsActiveAsync(new BulkProductActiveStateRequest(false, [new(id, true), new(id, true)]));
        Assert.IsFalse(duplicate.Succeeded);
        Assert.AreEqual(ValidationCodes.BulkRequestInvalid, duplicate.Issues[0].StableCode);

        var invalid = await service.BulkSetProductsActiveAsync(new BulkProductActiveStateRequest(false, [new(Guid.Empty, true)]));
        Assert.IsFalse(invalid.Succeeded);
        Assert.AreEqual(ValidationCodes.BulkRequestInvalid, invalid.Issues[0].StableCode);
        Assert.AreEqual(0, store.BulkCalls);
    }

    private sealed class FakeCatalogueStore : ICatalogueStore
    {
        public int CreateProductCalls { get; private set; }
        public int UpdateProductCalls { get; private set; }
        public int SetActiveCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int BulkCalls { get; private set; }
        public Guid LastUpdateId { get; private set; }
        public ProductDraft LastDraft { get; private set; } = null!;
        public Guid CreatedProductId { get; init; } = Guid.NewGuid();

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<ProductDraft?>(null);
        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(Guid.NewGuid(), name)));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(new(categoryId, name)));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) { CreateProductCalls++; LastDraft = draft; return Task.FromResult(OperationResult<Guid>.Success(CreatedProductId)); }
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) { UpdateProductCalls++; LastUpdateId = productId; LastDraft = draft; return Task.FromResult(OperationResult.Success()); }
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) { SetActiveCalls++; return Task.FromResult(OperationResult.Success()); }
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) { BulkCalls++; return Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, request.Items.Count(item => item.ExpectedIsActive != request.TargetIsActive)))); }
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) { DeleteCalls++; return Task.FromResult(OperationResult.Success()); }
    }

    private sealed class FakeSettingsStore : IBusinessSettingsStore
    {
        public int UpdateCalls { get; private set; }
        public BusinessSettings LastSettings { get; private set; } = default!;
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(Now));
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) { UpdateCalls++; LastSettings = settings; return Task.FromResult(OperationResult.Success()); }
    }
}
