using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class CatalogueWorkbookApplicationTests
{
    [TestMethod]
    public async Task ExportBuildsCompleteDeterministicApplicationModel()
    {
        var category = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var productA = Guid.NewGuid();
        var group = Guid.NewGuid();
        var option = Guid.NewGuid();
        var queries = new FakeQueries(
            [
                new ProductSummary(productB, "B", "Beta", category, "Plats", Money.FromCents(200), 20m, false, true, true, "P"),
                new ProductSummary(productA, "A", "Alpha", category, "Plats", Money.FromCents(100), 10m, true, false, false, "P")
            ],
            new Dictionary<Guid, ProductDraft>
            {
                [productB] = new(productB, "B", "Beta", category, Money.FromCents(200), 20m, false, true, true,
                    [new OptionGroupDraft(group, "Extras", SelectionMode.Single, true, null, null, 0,
                        [new OptionDraft(option, "Inactive choice", Money.FromCents(25), false, 0)])]),
                [productA] = new(productA, "A", "Alpha", category, Money.FromCents(100), 10m, true, false, false, [])
            });
        var gateway = new RecordingGateway();

        await new CatalogueWorkbookService(queries, gateway).ExportAsync(new MemoryStream());

        var model = gateway.Model!;
        Assert.HasCount(2, model.Products);
        Assert.AreEqual("A", model.Products[0].Code);
        Assert.AreEqual("B", model.Products[1].Code);
        Assert.AreEqual("P", model.Products[1].CategoryShortCode);
        Assert.IsFalse(model.Products[1].IsActive);
        Assert.AreEqual(group, model.Products[1].OptionGroups[0].OptionGroupId);
        Assert.AreEqual(option, model.Products[1].OptionGroups[0].Options[0].OptionId);
        Assert.IsFalse(model.Products[1].OptionGroups[0].Options[0].IsActive);
        Assert.AreEqual(2, queries.LookupCalls);
    }

    [TestMethod]
    public void ApplicationContractDoesNotReferenceClosedXml()
    {
        Assert.IsFalse(typeof(CatalogueWorkbookService).Assembly.GetReferencedAssemblies()
            .Any(reference => string.Equals(reference.Name, "ClosedXML", StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class RecordingGateway : ICatalogueWorkbookGateway
    {
        public CatalogueWorkbookExport? Model { get; private set; }

        public Task WriteAsync(CatalogueWorkbookExport model, Stream destination, CancellationToken cancellationToken = default)
        {
            Model = model;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeQueries(IReadOnlyList<ProductSummary> products, IReadOnlyDictionary<Guid, ProductDraft> drafts) : ICatalogueQueries
    {
        public int LookupCalls { get; private set; }

        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);

        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(products);

        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            drafts.TryGetValue(productId, out var draft);
            return Task.FromResult(draft);
        }
    }
}
