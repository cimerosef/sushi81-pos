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
        var queries = new SnapshotQueries(
            [
                new CatalogueWorkbookProduct(
                    productB, "B", "Beta", "Plats", "P", Money.FromCents(200), 20m, false, true, true,
                    [new CatalogueWorkbookOptionGroup(
                        group, productB, "B", "Beta", "Extras", SelectionMode.Single, true, null, null, 0,
                        [new CatalogueWorkbookOption(option, group, "B", "Beta", "Extras", "Inactive choice", Money.FromCents(25), false, 0)])]),
                new CatalogueWorkbookProduct(
                    productA, "A", "Alpha", "Plats", "P", Money.FromCents(100), 10m, true, false, false, [])
            ]);
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
        Assert.AreEqual(1, queries.SnapshotCalls);
    }

    [TestMethod]
    public void ApplicationContractDoesNotReferenceClosedXml()
    {
        Assert.IsFalse(typeof(CatalogueWorkbookService).Assembly.GetReferencedAssemblies()
            .Any(reference => string.Equals(reference.Name, "ClosedXML", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ProductionSnapshotSeamAvoidsIndependentPerProductLookups()
    {
        var snapshot = new CatalogueWorkbookProduct(
            Guid.NewGuid(), "P", "Product", "Plats", "PL", Money.FromCents(100), 20m, true, false, false, []);
        var queries = new SnapshotQueries([snapshot]);
        var gateway = new RecordingGateway();

        await new CatalogueWorkbookService(queries, gateway).ExportAsync(new MemoryStream());

        Assert.AreEqual(1, queries.SnapshotCalls);
        Assert.AreEqual(snapshot, gateway.Model!.Products.Single());
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

    private sealed class SnapshotQueries(IReadOnlyList<CatalogueWorkbookProduct> snapshot) : ICatalogueWorkbookSnapshotQueries
    {
        public int SnapshotCalls { get; private set; }

        public Task<IReadOnlyList<CatalogueWorkbookProduct>> ReadCatalogueWorkbookSnapshotAsync(CancellationToken cancellationToken = default)
        {
            SnapshotCalls++;
            return Task.FromResult(snapshot);
        }
    }
}
