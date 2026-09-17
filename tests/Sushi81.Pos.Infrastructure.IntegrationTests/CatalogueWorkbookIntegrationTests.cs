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
    }

    private static async Task<byte[]> WriteAsync(CatalogueWorkbookExport model)
    {
        await using var stream = new MemoryStream();
        await new ClosedXmlCatalogueWorkbookGateway().WriteAsync(model, stream);
        return stream.ToArray();
    }
}
