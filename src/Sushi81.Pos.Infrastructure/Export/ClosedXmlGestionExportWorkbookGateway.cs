using System.Globalization;
using ClosedXML.Excel;
using Sushi81.Pos.Application.Export;

namespace Sushi81.Pos.Infrastructure.Export;

/// <summary>
/// ClosedXML-only implementation of the stable four-sheet Gestion export contract.
/// No ClosedXML type crosses the Application boundary.
/// </summary>
public sealed class ClosedXmlGestionExportWorkbookGateway : IExportWorkbookGateway
{
    public const string SchemaVersion = "1.0";

    public static readonly string[] SheetNames = ["Meta", "Orders", "OrderLines", "TaxBreakdown"];

    public static readonly string[] OrderHeaders =
    [
        "Action", "OrderId", "OrderStatus", "CreatedAt", "FulfilmentDate", "FulfilmentTime",
        "SettlementDate", "FulfilmentMode", "TotalTTC", "CBTotal", "EspeceTotal", "Telephone",
        "Address", "Comment"
    ];

    public static readonly string[] LineHeaders =
    [
        "Action", "OrderId", "LineNo", "ProductCode", "ProductName", "Quantity", "UnitBaseTTC",
        "OptionAdjustmentTTC", "LineTTC", "VATRate", "OptionsSummary"
    ];

    public static readonly string[] TaxHeaders =
    ["Action", "OrderId", "VATRate", "TaxableHT", "VATAmount", "TTC"];

    public Task WriteAsync(
        ExportBatchPayload payload,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePayloadShape(payload);

        using var workbook = new XLWorkbook();
        var meta = workbook.Worksheets.Add("Meta");
        var orders = workbook.Worksheets.Add("Orders");
        var lines = workbook.Worksheets.Add("OrderLines");
        var taxes = workbook.Worksheets.Add("TaxBreakdown");

        WriteMeta(meta, payload.Meta);
        WriteOrders(orders, payload.Orders);
        WriteLines(lines, payload.Orders);
        WriteTaxes(taxes, payload.Orders);

        foreach (var sheet in workbook.Worksheets)
        {
            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Fill.BackgroundColor = XLColor.LightGray;
            sheet.SheetView.FreezeRows(1);
            sheet.Columns().AdjustToContents();
        }

        cancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(destination);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async Task ValidateAsync(
        Stream source,
        ExportBatchPayload expectedPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(expectedPayload);
        ValidatePayloadShape(expectedPayload);
        cancellationToken.ThrowIfCancellationRequested();

        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        using var workbook = new XLWorkbook(buffer);
        cancellationToken.ThrowIfCancellationRequested();

        if (workbook.Worksheets.Count != SheetNames.Length)
            throw new InvalidDataException("The export workbook must contain exactly four worksheets.");
        for (var i = 0; i < SheetNames.Length; i++)
            if (!string.Equals(workbook.Worksheet(i + 1).Name, SheetNames[i], StringComparison.Ordinal))
                throw new InvalidDataException("The export workbook worksheet order or names are invalid.");

        ValidateMeta(workbook.Worksheet("Meta"), expectedPayload.Meta);
        ValidateOrders(workbook.Worksheet("Orders"), expectedPayload.Orders);
        ValidateLines(workbook.Worksheet("OrderLines"), expectedPayload.Orders);
        ValidateTaxes(workbook.Worksheet("TaxBreakdown"), expectedPayload.Orders);
    }

    private static void WriteMeta(IXLWorksheet sheet, ExportBatchMeta meta)
    {
        WriteHeader(sheet, ["Key", "Value"]);
        var keys = new[]
        {
            "SchemaVersion", "BatchId", "GeneratedAt", "AppVersion", "FilterStartDate", "FilterEndDate",
            "OrderCount", "OrderLineCount", "TaxBreakdownCount"
        };
        for (var i = 0; i < keys.Length; i++)
        {
            sheet.Cell(i + 2, 1).Value = keys[i];
            switch (keys[i])
            {
                case "SchemaVersion": sheet.Cell(i + 2, 2).Value = meta.SchemaVersion; break;
                case "BatchId": sheet.Cell(i + 2, 2).Value = meta.BatchId.ToString("D"); break;
                case "GeneratedAt": SetDateTime(sheet.Cell(i + 2, 2), meta.GeneratedAt.UtcDateTime); break;
                case "AppVersion": sheet.Cell(i + 2, 2).Value = meta.AppVersion; break;
                case "FilterStartDate": if (meta.FilterStartDate is { } start) SetDate(sheet.Cell(i + 2, 2), start); break;
                case "FilterEndDate": if (meta.FilterEndDate is { } end) SetDate(sheet.Cell(i + 2, 2), end); break;
                case "OrderCount": sheet.Cell(i + 2, 2).Value = meta.OrderCount; break;
                case "OrderLineCount": sheet.Cell(i + 2, 2).Value = meta.OrderLineCount; break;
                case "TaxBreakdownCount": sheet.Cell(i + 2, 2).Value = meta.TaxBreakdownCount; break;
            }
        }

        sheet.Cell(4, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        sheet.Range(6, 2, 7, 2).Style.DateFormat.Format = "yyyy-mm-dd";
    }

    private static void WriteOrders(IXLWorksheet sheet, IReadOnlyList<ExportOrderPayload> orders)
    {
        WriteHeader(sheet, OrderHeaders);
        var row = 2;
        foreach (var order in orders)
        {
            sheet.Cell(row, 1).Value = ActionName(order.Action);
            sheet.Cell(row, 2).Value = order.OrderId.ToString("D");
            sheet.Cell(row, 3).Value = order.OrderStatus;
            SetDateTime(sheet.Cell(row, 4), order.CreatedAt.UtcDateTime);
            SetDate(sheet.Cell(row, 5), order.FulfilmentDate);
            if (order.FulfilmentTime is { } time) SetTime(sheet.Cell(row, 6), time);
            SetDate(sheet.Cell(row, 7), order.SettlementDate);
            sheet.Cell(row, 8).Value = order.FulfilmentMode;
            SetMoney(sheet.Cell(row, 9), order.TotalTtcCents);
            SetMoney(sheet.Cell(row, 10), order.CardTotalCents);
            SetMoney(sheet.Cell(row, 11), order.CashTotalCents);
            SetText(sheet.Cell(row, 12), order.Telephone);
            SetText(sheet.Cell(row, 13), order.Address);
            SetText(sheet.Cell(row, 14), order.Comment);
            row++;
        }

        sheet.Column(4).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        sheet.Column(5).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Column(6).Style.DateFormat.Format = "hh:mm";
        sheet.Column(7).Style.DateFormat.Format = "yyyy-mm-dd";
        sheet.Columns(9, 11).Style.NumberFormat.Format = "0.00";
    }

    private static void WriteLines(IXLWorksheet sheet, IReadOnlyList<ExportOrderPayload> orders)
    {
        WriteHeader(sheet, LineHeaders);
        var row = 2;
        foreach (var order in orders.Where(order => order.Action is ExportAction.Create or ExportAction.Update))
        foreach (var line in order.Lines)
        {
            sheet.Cell(row, 1).Value = ActionName(order.Action);
            sheet.Cell(row, 2).Value = line.OrderId.ToString("D");
            sheet.Cell(row, 3).Value = line.LineNo;
            sheet.Cell(row, 4).Value = line.ProductCode;
            sheet.Cell(row, 5).Value = line.ProductName;
            sheet.Cell(row, 6).Value = line.Quantity;
            SetMoney(sheet.Cell(row, 7), line.UnitBaseTtcCents);
            SetMoney(sheet.Cell(row, 8), line.OptionAdjustmentTtcCents);
            SetMoney(sheet.Cell(row, 9), line.LineTtcCents);
            sheet.Cell(row, 10).Value = line.VatRate;
            sheet.Cell(row, 11).Value = line.OptionsSummary;
            row++;
        }

        sheet.Columns(7, 9).Style.NumberFormat.Format = "0.00";
        sheet.Column(10).Style.NumberFormat.Format = "0.00";
    }

    private static void WriteTaxes(IXLWorksheet sheet, IReadOnlyList<ExportOrderPayload> orders)
    {
        WriteHeader(sheet, TaxHeaders);
        var row = 2;
        foreach (var order in orders.Where(order => order.Action is ExportAction.Create or ExportAction.Update))
        foreach (var tax in order.TaxBreakdown)
        {
            sheet.Cell(row, 1).Value = ActionName(order.Action);
            sheet.Cell(row, 2).Value = tax.OrderId.ToString("D");
            sheet.Cell(row, 3).Value = tax.VatRate;
            SetMoney(sheet.Cell(row, 4), tax.TaxableHtCents);
            SetMoney(sheet.Cell(row, 5), tax.VatAmountCents);
            SetMoney(sheet.Cell(row, 6), tax.TtcCents);
            row++;
        }

        sheet.Column(3).Style.NumberFormat.Format = "0.00";
        sheet.Columns(4, 6).Style.NumberFormat.Format = "0.00";
    }

    private static void ValidateMeta(IXLWorksheet sheet, ExportBatchMeta expected)
    {
        ValidateHeaders(sheet, ["Key", "Value"]);
        var actual = new Dictionary<string, IXLCell>(StringComparer.Ordinal);
        for (var row = 2; row <= 10; row++)
        {
            var key = RequireText(sheet.Cell(row, 1), sheet.Name, row, "Key");
            if (!actual.TryAdd(key, sheet.Cell(row, 2)))
                throw new InvalidDataException($"The Meta worksheet contains duplicate key '{key}'.");
        }

        var keys = new[]
        {
            "SchemaVersion", "BatchId", "GeneratedAt", "AppVersion", "FilterStartDate", "FilterEndDate",
            "OrderCount", "OrderLineCount", "TaxBreakdownCount"
        };
        if (actual.Count != keys.Length || keys.Any(key => !actual.ContainsKey(key)))
            throw new InvalidDataException("The Meta worksheet does not match the immutable batch metadata.");

        RequireTextValue(actual["SchemaVersion"], expected.SchemaVersion, sheet.Name, 2, "SchemaVersion");
        RequireTextValue(actual["BatchId"], expected.BatchId.ToString("D"), sheet.Name, 3, "BatchId");
        RequireDateTime(actual["GeneratedAt"], expected.GeneratedAt.UtcDateTime, sheet.Name, 4, "GeneratedAt");
        RequireTextValue(actual["AppVersion"], expected.AppVersion, sheet.Name, 5, "AppVersion");
        if (expected.FilterStartDate is { } start) RequireDate(actual["FilterStartDate"], start, sheet.Name, 6, "FilterStartDate");
        else RequireBlank(actual["FilterStartDate"], sheet.Name, 6, "FilterStartDate");
        if (expected.FilterEndDate is { } end) RequireDate(actual["FilterEndDate"], end, sheet.Name, 7, "FilterEndDate");
        else RequireBlank(actual["FilterEndDate"], sheet.Name, 7, "FilterEndDate");
        RequireNumber(actual["OrderCount"], expected.OrderCount, sheet.Name, 8, "OrderCount");
        RequireNumber(actual["OrderLineCount"], expected.OrderLineCount, sheet.Name, 9, "OrderLineCount");
        RequireNumber(actual["TaxBreakdownCount"], expected.TaxBreakdownCount, sheet.Name, 10, "TaxBreakdownCount");
    }

    private static void ValidateOrders(IXLWorksheet sheet, IReadOnlyList<ExportOrderPayload> expected)
    {
        ValidateHeaders(sheet, OrderHeaders);
        EnsureNoExtraRows(sheet, expected.Count, OrderHeaders.Length);
        for (var index = 0; index < expected.Count; index++)
        {
            var row = index + 2;
            var order = expected[index];
            RequireAction(sheet.Cell(row, 1), order.Action, sheet.Name, row);
            RequireTextValue(sheet.Cell(row, 2), order.OrderId.ToString("D"), sheet.Name, row, "OrderId");
            RequireTextValue(sheet.Cell(row, 3), order.OrderStatus, sheet.Name, row, "OrderStatus");
            RequireDateTime(sheet.Cell(row, 4), order.CreatedAt.UtcDateTime, sheet.Name, row, "CreatedAt");
            RequireDate(sheet.Cell(row, 5), order.FulfilmentDate, sheet.Name, row, "FulfilmentDate");
            if (order.FulfilmentTime is { } time) RequireTime(sheet.Cell(row, 6), time, sheet.Name, row, "FulfilmentTime");
            else RequireBlank(sheet.Cell(row, 6), sheet.Name, row, "FulfilmentTime");
            RequireDate(sheet.Cell(row, 7), order.SettlementDate, sheet.Name, row, "SettlementDate");
            RequireTextValue(sheet.Cell(row, 8), order.FulfilmentMode, sheet.Name, row, "FulfilmentMode");
            RequireMoney(sheet.Cell(row, 9), order.TotalTtcCents, sheet.Name, row, "TotalTTC");
            RequireMoney(sheet.Cell(row, 10), order.CardTotalCents, sheet.Name, row, "CBTotal");
            RequireMoney(sheet.Cell(row, 11), order.CashTotalCents, sheet.Name, row, "EspeceTotal");
            RequireOptionalText(sheet.Cell(row, 12), order.Telephone, sheet.Name, row, "Telephone");
            RequireOptionalText(sheet.Cell(row, 13), order.Address, sheet.Name, row, "Address");
            RequireOptionalText(sheet.Cell(row, 14), order.Comment, sheet.Name, row, "Comment");
        }
    }

    private static void ValidateLines(IXLWorksheet sheet, IReadOnlyList<ExportOrderPayload> orders)
    {
        ValidateHeaders(sheet, LineHeaders);
        var expected = orders.Where(order => order.Action is ExportAction.Create or ExportAction.Update)
            .SelectMany(order => order.Lines.Select(line => (Order: order, Line: line)))
            .ToArray();
        EnsureNoExtraRows(sheet, expected.Length, LineHeaders.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var row = index + 2;
            var item = expected[index];
            RequireAction(sheet.Cell(row, 1), item.Order.Action, sheet.Name, row);
            RequireTextValue(sheet.Cell(row, 2), item.Line.OrderId.ToString("D"), sheet.Name, row, "OrderId");
            RequireNumber(sheet.Cell(row, 3), item.Line.LineNo, sheet.Name, row, "LineNo");
            RequireTextValue(sheet.Cell(row, 4), item.Line.ProductCode, sheet.Name, row, "ProductCode");
            RequireTextValue(sheet.Cell(row, 5), item.Line.ProductName, sheet.Name, row, "ProductName");
            RequireNumber(sheet.Cell(row, 6), item.Line.Quantity, sheet.Name, row, "Quantity");
            RequireMoney(sheet.Cell(row, 7), item.Line.UnitBaseTtcCents, sheet.Name, row, "UnitBaseTTC");
            RequireMoney(sheet.Cell(row, 8), item.Line.OptionAdjustmentTtcCents, sheet.Name, row, "OptionAdjustmentTTC");
            RequireMoney(sheet.Cell(row, 9), item.Line.LineTtcCents, sheet.Name, row, "LineTTC");
            RequireNumber(sheet.Cell(row, 10), item.Line.VatRate, sheet.Name, row, "VATRate");
            RequireTextValueAllowEmpty(sheet.Cell(row, 11), item.Line.OptionsSummary, sheet.Name, row, "OptionsSummary");
        }
    }

    private static void ValidateTaxes(IXLWorksheet sheet, IReadOnlyList<ExportOrderPayload> orders)
    {
        ValidateHeaders(sheet, TaxHeaders);
        var expected = orders.Where(order => order.Action is ExportAction.Create or ExportAction.Update)
            .SelectMany(order => order.TaxBreakdown.Select(tax => (Order: order, Tax: tax)))
            .ToArray();
        EnsureNoExtraRows(sheet, expected.Length, TaxHeaders.Length);
        for (var index = 0; index < expected.Length; index++)
        {
            var row = index + 2;
            var item = expected[index];
            RequireAction(sheet.Cell(row, 1), item.Order.Action, sheet.Name, row);
            RequireTextValue(sheet.Cell(row, 2), item.Tax.OrderId.ToString("D"), sheet.Name, row, "OrderId");
            RequireNumber(sheet.Cell(row, 3), item.Tax.VatRate, sheet.Name, row, "VATRate");
            RequireMoney(sheet.Cell(row, 4), item.Tax.TaxableHtCents, sheet.Name, row, "TaxableHT");
            RequireMoney(sheet.Cell(row, 5), item.Tax.VatAmountCents, sheet.Name, row, "VATAmount");
            RequireMoney(sheet.Cell(row, 6), item.Tax.TtcCents, sheet.Name, row, "TTC");
        }
    }

    private static void ValidatePayloadShape(ExportBatchPayload payload)
    {
        if (payload.Meta is null || payload.Orders is null || payload.Meta.SchemaVersion != SchemaVersion)
            throw new InvalidDataException("The export payload has an unsupported schema or missing metadata.");
        if (payload.Meta.BatchId == Guid.Empty || payload.Meta.OrderCount != payload.Orders.Count
            || payload.Meta.OrderLineCount != payload.Orders.Sum(order => order.Lines.Count)
            || payload.Meta.TaxBreakdownCount != payload.Orders.Sum(order => order.TaxBreakdown.Count))
            throw new InvalidDataException("The export payload metadata counts do not match its rows.");
        var ids = new HashSet<Guid>();
        foreach (var order in payload.Orders)
        {
            if (order.OrderId == Guid.Empty || !ids.Add(order.OrderId))
                throw new InvalidDataException("The export payload contains duplicate or empty order identifiers.");
            if (order.Action == ExportAction.Cancel && (order.Lines.Count != 0 || order.TaxBreakdown.Count != 0))
                throw new InvalidDataException("A CANCEL payload cannot contain child rows.");
            if (order.Lines.Any(line => line.OrderId != order.OrderId) || order.TaxBreakdown.Any(tax => tax.OrderId != order.OrderId))
                throw new InvalidDataException("The export payload contains a child row for another order.");
        }
    }

    private static void ValidateHeaders(IXLWorksheet sheet, string[] expected)
    {
        for (var column = 0; column < expected.Length; column++)
            if (!string.Equals(sheet.Cell(1, column + 1).GetString(), expected[column], StringComparison.Ordinal))
                throw new InvalidDataException($"The {sheet.Name} worksheet has an invalid header at column {column + 1}.");
        if (!sheet.Cell(1, expected.Length + 1).IsEmpty())
            throw new InvalidDataException($"The {sheet.Name} worksheet contains unexpected headers.");
    }

    private static void EnsureNoExtraRows(IXLWorksheet sheet, int expectedDataRows, int businessColumns)
    {
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        if (last > expectedDataRows + 1)
            throw new InvalidDataException($"The {sheet.Name} worksheet contains unexpected data rows.");
        for (var row = expectedDataRows + 2; row <= last; row++)
            if (Enumerable.Range(1, businessColumns).Any(column => !sheet.Cell(row, column).IsEmpty()))
                throw new InvalidDataException($"The {sheet.Name} worksheet contains unexpected data rows.");
    }

    private static void WriteHeader(IXLWorksheet sheet, string[] headers)
    {
        for (var column = 0; column < headers.Length; column++) sheet.Cell(1, column + 1).Value = headers[column];
    }

    private static string ActionName(ExportAction action) => action switch
    {
        ExportAction.Create => "CREATE",
        ExportAction.Update => "UPDATE",
        ExportAction.Cancel => "CANCEL",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static void SetText(IXLCell cell, string? value)
    {
        if (string.IsNullOrEmpty(value)) cell.Clear(XLClearOptions.Contents);
        else cell.Value = value;
    }

    private static void SetDateTime(IXLCell cell, DateTime value) => cell.Value = value;
    private static void SetDate(IXLCell cell, DateOnly value) => cell.Value = value.ToDateTime(TimeOnly.MinValue);
    private static void SetTime(IXLCell cell, TimeOnly value) => cell.Value = value.ToTimeSpan();
    private static void SetMoney(IXLCell cell, long cents) => cell.Value = (decimal)cents / 100m;

    private static string RequireText(IXLCell cell, string sheet, int row, string field)
    {
        if (cell.IsEmpty() || cell.DataType != XLDataType.Text || string.IsNullOrWhiteSpace(cell.GetString()))
            throw new InvalidDataException($"{sheet}!{field}{row} must be nonblank text.");
        return cell.GetString();
    }

    private static void RequireTextValue(IXLCell cell, string expected, string sheet, int row, string field)
    {
        if (RequireText(cell, sheet, row, field) != expected)
            throw new InvalidDataException($"{sheet}!{field}{row} does not match the immutable payload.");
    }

    private static void RequireTextValueAllowEmpty(IXLCell cell, string expected, string sheet, int row, string field)
    {
        if (string.IsNullOrEmpty(expected) && cell.IsEmpty())
            return;
        if (cell.IsEmpty() || cell.DataType != XLDataType.Text || cell.GetString() != expected)
            throw new InvalidDataException($"{sheet}!{field}{row} does not match the immutable payload text.");
    }

    private static void RequireOptionalText(IXLCell cell, string? expected, string sheet, int row, string field)
    {
        if (string.IsNullOrEmpty(expected)) RequireBlank(cell, sheet, row, field);
        else RequireTextValue(cell, expected, sheet, row, field);
    }

    private static void RequireBlank(IXLCell cell, string sheet, int row, string field)
    {
        if (!cell.IsEmpty()) throw new InvalidDataException($"{sheet}!{field}{row} must be blank.");
    }

    private static void RequireAction(IXLCell cell, ExportAction action, string sheet, int row) =>
        RequireTextValue(cell, ActionName(action), sheet, row, "Action");

    private static void RequireNumber(IXLCell cell, decimal expected, string sheet, int row, string field)
    {
        if (cell.DataType != XLDataType.Number || cell.GetValue<decimal>() != expected)
            throw new InvalidDataException($"{sheet}!{field}{row} must be the expected native number.");
    }

    private static void RequireMoney(IXLCell cell, long expectedCents, string sheet, int row, string field) =>
        RequireNumber(cell, (decimal)expectedCents / 100m, sheet, row, field);

    private static void RequireDateTime(IXLCell cell, DateTime expected, string sheet, int row, string field)
    {
        if (cell.DataType != XLDataType.DateTime || cell.GetValue<DateTime>() != expected)
            throw new InvalidDataException($"{sheet}!{field}{row} must be the expected native datetime.");
    }

    private static void RequireDate(IXLCell cell, DateOnly expected, string sheet, int row, string field) =>
        RequireDateTime(cell, expected.ToDateTime(TimeOnly.MinValue), sheet, row, field);

    private static void RequireTime(IXLCell cell, TimeOnly expected, string sheet, int row, string field)
    {
        if (cell.DataType != XLDataType.TimeSpan || cell.GetValue<TimeSpan>() != expected.ToTimeSpan())
            throw new InvalidDataException($"{sheet}!{field}{row} must be the expected native time.");
    }
}
