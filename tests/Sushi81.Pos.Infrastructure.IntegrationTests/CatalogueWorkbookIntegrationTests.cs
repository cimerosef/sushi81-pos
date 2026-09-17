using System.IO.Compression;
using ClosedXML.Excel;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Catalogue;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class CatalogueWorkbookIntegrationTests
{
    private static readonly string[] ExpectedSheetNames = ["Products", "OptionGroups", "Options", "__Sushi81Meta"];

    [TestMethod]
    public async Task ExportWritesBoundedSheetsTechnicalBindingsAndProtection()
    {
        var productId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var optionId = Guid.NewGuid();
        var model = new CatalogueWorkbookExport(
            [new CatalogueWorkbookProduct(
                productId, "P-01", "Salmon", "Plats", "PL", Money.FromCents(1299), 20m, false, true, true,
                [new CatalogueWorkbookOptionGroup(groupId, productId, "P-01", "Salmon", "Extras", SelectionMode.Multi, false, 0, 2, 0,
                    [new CatalogueWorkbookOption(optionId, groupId, "P-01", "Salmon", "Extras", "Avocado", Money.FromCents(150), false, 0)])])],
            Guid.NewGuid());
        var bytes = await WriteAsync(model);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        CollectionAssert.AreEqual(ExpectedSheetNames, workbook.Worksheets.Select(sheet => sheet.Name).ToArray());
        Assert.AreEqual(XLWorksheetVisibility.VeryHidden, workbook.Worksheet("__Sushi81Meta").Visibility);

        var products = workbook.Worksheet("Products");
        Assert.AreEqual("P-01", products.Cell(2, 1).GetString());
        Assert.AreEqual("PL", products.Cell(2, 4).GetString());
        Assert.AreEqual(12.99m, products.Cell(2, 5).GetValue<decimal>());
        Assert.IsFalse(products.Cell(2, 7).GetBoolean());
        Assert.IsFalse(products.Cell(2, 1).Style.Protection.Locked);
        Assert.IsTrue(products.Cell(2, 10).Style.Protection.Locked);
        Assert.IsTrue(products.Column(10).IsHidden);
        Assert.IsTrue(products.Protection.IsProtected);

        var groups = workbook.Worksheet("OptionGroups");
        Assert.AreEqual("Extras", groups.Cell(2, 3).GetString());
        Assert.AreEqual(groupId.ToString("D"), groups.Cell(2, 12).GetString());
        var options = workbook.Worksheet("Options");
        Assert.AreEqual("Avocado", options.Cell(2, 4).GetString());
        Assert.IsFalse(options.Cell(2, 6).GetBoolean());

        var metadata = workbook.Worksheet("__Sushi81Meta");
        Assert.AreEqual("Worksheet", metadata.Cell(6, 1).GetString());
        Assert.AreEqual("product_code", metadata.Cell(7, 2).GetString());
        Assert.AreEqual(1, metadata.Cell(7, 3).GetValue<int>());
        Assert.IsTrue(metadata.Cell(7, 4).GetBoolean());
        Assert.IsTrue(metadata.Cell(7, 5).GetBoolean());
        Assert.AreEqual("product-code", metadata.Cell(7, 6).GetString());
        Assert.AreEqual("EntityType", metadata.Cell(43, 1).GetString());
        Assert.AreEqual("Product", metadata.Cell(44, 1).GetString());
        Assert.AreEqual("Products", metadata.Cell(44, 5).GetString());

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.IsNull(archive.GetEntry("xl/vbaProject.bin"));
    }

    [TestMethod]
    public async Task EmptyExportStillProvidesDeterministicHeadersAndMetadata()
    {
        var bytes = await WriteAsync(new CatalogueWorkbookExport([], Guid.NewGuid()));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.AreEqual("Product Code", workbook.Worksheet("Products").Cell(1, 1).GetString());
        Assert.AreEqual("Option Name", workbook.Worksheet("Options").Cell(1, 4).GetString());
        Assert.AreEqual("ContractVersion", workbook.Worksheet("__Sushi81Meta").Cell(2, 1).GetString());
        Assert.AreEqual("M10-CATALOGUE-1", workbook.Worksheet("__Sushi81Meta").Cell(2, 2).GetString());

        var products = workbook.Worksheet("Products");
        Assert.IsFalse(products.Cell(2, 1).Style.Protection.Locked);
        Assert.IsTrue(products.Cell(2, 10).Style.Protection.Locked);
        Assert.AreEqual(string.Empty, products.Cell(2, 10).GetString());
        products.Row(2).InsertRowsBelow(1);
        Assert.IsFalse(products.Cell(3, 1).Style.Protection.Locked);
        Assert.IsTrue(products.Cell(3, 10).Style.Protection.Locked);
    }

    [TestMethod]
    public async Task SortingFullBoundedRangeKeepsTechnicalBindingsWithBusinessRows()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var bytes = await WriteAsync(new CatalogueWorkbookExport(
            [
                new CatalogueWorkbookProduct(firstId, "B", "Zeta", "Plats", "P", Money.FromCents(100), 20m, true, false, false, []),
                new CatalogueWorkbookProduct(secondId, "A", "Alpha", "Plats", "P", Money.FromCents(200), 20m, true, false, false, [])
            ], Guid.NewGuid()));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var products = workbook.Worksheet("Products");
        products.Range(2, 1, 3, 11).Sort(2, XLSortOrder.Ascending);

        Assert.AreEqual("A", products.Cell(2, 1).GetString());
        Assert.AreEqual($"product:{secondId:N}", products.Cell(2, 10).GetString());
        Assert.AreEqual("B", products.Cell(3, 1).GetString());
        Assert.AreEqual($"product:{firstId:N}", products.Cell(3, 10).GetString());

        products.Row(3).InsertRowsAbove(1);
        Assert.AreEqual("B", products.Cell(4, 1).GetString());
        Assert.AreEqual($"product:{firstId:N}", products.Cell(4, 10).GetString());
        Assert.AreEqual(string.Empty, products.Cell(3, 10).GetString());
    }

    [TestMethod]
    public async Task CanonicalFingerprintsDistinguishDelimiterAndUnicodeValues()
    {
        var first = new CatalogueWorkbookProduct(Guid.NewGuid(), "a|b", "c", "Cat", "P", Money.FromCents(100), 20m, true, false, false, []);
        var second = first with { ProductId = Guid.NewGuid(), Code = "a", Name = "b|c" };
        var bytes = await WriteAsync(new CatalogueWorkbookExport([first, second], Guid.NewGuid()));

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var metadata = workbook.Worksheet("__Sushi81Meta");
        var fingerprintOne = metadata.Cell(44, 7).GetString();
        var fingerprintTwo = metadata.Cell(45, 7).GetString();
        Assert.AreNotEqual(fingerprintOne, fingerprintTwo);
    }

    private static async Task<byte[]> WriteAsync(CatalogueWorkbookExport model)
    {
        await using var stream = new MemoryStream();
        await new ClosedXmlCatalogueWorkbookGateway().WriteAsync(model, stream);
        return stream.ToArray();
    }
}
