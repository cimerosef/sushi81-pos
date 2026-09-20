using ClosedXML.Excel;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Infrastructure.Export;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M11Wp2WorkbookGatewayTests
{
    private static readonly string[] ExpectedSheetNames = ["Meta", "Orders", "OrderLines", "TaxBreakdown"];

    [TestMethod]
    public async Task WritesTheFixedFourSheetContractWithNativeValuesAndCancelShape()
    {
        var payload = SamplePayload();
        var gateway = new ClosedXmlGestionExportWorkbookGateway();
        await using var stream = new MemoryStream();

        await gateway.WriteAsync(payload, stream);
        stream.Position = 0;
        await gateway.ValidateAsync(stream, payload);

        stream.Position = 0;
        using var workbook = new XLWorkbook(stream);
        CollectionAssert.AreEqual(
            ExpectedSheetNames,
            workbook.Worksheets.Select(sheet => sheet.Name).ToArray());

        var meta = workbook.Worksheet("Meta");
        Assert.AreEqual(XLDataType.DateTime, meta.Cell(4, 2).DataType);
        Assert.AreEqual(XLDataType.Number, meta.Cell(8, 2).DataType);
        Assert.AreEqual(XLDataType.DateTime, meta.Cell(7, 2).DataType);

        var orders = workbook.Worksheet("Orders");
        Assert.AreEqual(XLDataType.DateTime, orders.Cell(2, 4).DataType);
        Assert.AreEqual(XLDataType.DateTime, orders.Cell(2, 5).DataType);
        Assert.AreEqual(XLDataType.TimeSpan, orders.Cell(2, 6).DataType);
        Assert.AreEqual(XLDataType.Number, orders.Cell(2, 9).DataType);
        Assert.IsTrue(orders.Cell(2, 12).IsEmpty(), "Null optional text must be a blank cell.");

        var lines = workbook.Worksheet("OrderLines");
        Assert.AreEqual("CREATE", lines.Cell(2, 1).GetString());
        Assert.AreEqual(XLDataType.Number, lines.Cell(2, 7).DataType);

        var taxes = workbook.Worksheet("TaxBreakdown");
        Assert.AreEqual(XLDataType.Number, taxes.Cell(2, 3).DataType);
        Assert.AreEqual("CREATE", taxes.Cell(2, 1).GetString());
        Assert.AreEqual(3, orders.LastRowUsed()!.RowNumber());
        Assert.AreEqual(2, lines.LastRowUsed()!.RowNumber());
        Assert.AreEqual(2, taxes.LastRowUsed()!.RowNumber());
    }

    [TestMethod]
    public async Task ValidationRejectsWrongHeaderActionAndNativeType()
    {
        var payload = SamplePayload();
        var gateway = new ClosedXmlGestionExportWorkbookGateway();
        await using var stream = new MemoryStream();
        await gateway.WriteAsync(payload, stream);

        await using var headerStream = Mutate(stream, workbook => workbook.Worksheet("Orders").Cell(1, 1).Value = "TranslatedAction");
        await Assert.ThrowsAsync<InvalidDataException>(() => gateway.ValidateAsync(headerStream, payload));

        await using var actionStream = new MemoryStream();
        await gateway.WriteAsync(payload, actionStream);
        await using var invalidActionStream = Mutate(actionStream, workbook => workbook.Worksheet("Orders").Cell(2, 1).Value = "create");
        await Assert.ThrowsAsync<InvalidDataException>(() => gateway.ValidateAsync(invalidActionStream, payload));

        await using var typeStream = new MemoryStream();
        await gateway.WriteAsync(payload, typeStream);
        await using var invalidTypeStream = Mutate(typeStream, workbook => workbook.Worksheet("Orders").Cell(2, 9).Value = "10.00");
        await Assert.ThrowsAsync<InvalidDataException>(() => gateway.ValidateAsync(invalidTypeStream, payload));
    }

    private static ExportBatchPayload SamplePayload()
    {
        var orderId = Guid.Parse("31000000-0000-0000-0000-000000000001");
        var line = new ExportLinePayload(orderId, 1, "P-001", "Maki", 2, 500, 50, 1100, 10m, "Sauce: Spicy");
        var tax = new ExportTaxPayload(orderId, 10m, 1000, 100, 1100);
        var positive = new ExportOrderPayload(
            ExportAction.Create,
            orderId,
            "CLOSED",
            new DateTimeOffset(2026, 9, 20, 8, 30, 0, TimeSpan.Zero),
            new DateOnly(2026, 9, 20),
            new TimeOnly(12, 15),
            new DateOnly(2026, 9, 20),
            "RETRAIT",
            1100,
            1100,
            0,
            null,
            "1 rue Test",
            "synthetic",
            [line],
            [tax]);
        var cancelledId = Guid.Parse("31000000-0000-0000-0000-000000000002");
        var cancelled = positive with
        {
            OrderId = cancelledId,
            Action = ExportAction.Cancel,
            OrderStatus = "CANCELLED",
            Lines = [],
            TaxBreakdown = []
        };
        return new ExportBatchPayload(
            new ExportBatchMeta(
                "1.0",
                Guid.Parse("31000000-0000-0000-0000-000000000099"),
                new DateTimeOffset(2026, 9, 20, 9, 0, 0, TimeSpan.Zero),
                "test-version",
                null,
                new DateOnly(2026, 9, 20),
                2,
                1,
                1),
            [positive, cancelled]);
    }

    private static MemoryStream Mutate(Stream source, Action<XLWorkbook> mutation)
    {
        source.Position = 0;
        using var workbook = new XLWorkbook(source);
        mutation(workbook);
        var result = new MemoryStream();
        workbook.SaveAs(result);
        result.Position = 0;
        return result;
    }
}
