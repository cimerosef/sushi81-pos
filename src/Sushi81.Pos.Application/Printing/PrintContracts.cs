using System.Globalization;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Printing;

public enum PrintDocumentKind
{
    Kitchen,
    Customer
}

public enum PrintIntent
{
    InitialAutomatic,
    InitialRetry,
    ExplicitReprint
}

public enum PrintOutcomeStatus
{
    Succeeded,
    QueueUnavailable,
    GenerationFailed,
    SubmissionFailed,
    AmbiguousSubmission,
    OrderNotFound,
    Unsupported
}

public sealed record OrderPrintDocument(
    PrintDocumentKind Kind,
    PrintIntent Intent,
    Guid OrderId,
    string Reference,
    string Text,
    bool IsFuture,
    bool IsCancelled)
{
    public string QueueRole => Kind == PrintDocumentKind.Kitchen ? "Kitchen" : "Customer";
}

public sealed record PrintDocumentResult(
    PrintDocumentKind Kind,
    PrintOutcomeStatus Status,
    string OperatorMessage,
    OrderPrintDocument? Document = null)
{
    public bool Succeeded => Status == PrintOutcomeStatus.Succeeded;

    public bool IsKnownFailure => Status is
        PrintOutcomeStatus.QueueUnavailable
        or PrintOutcomeStatus.GenerationFailed
        or PrintOutcomeStatus.SubmissionFailed;

    public static PrintDocumentResult Success(OrderPrintDocument document) => new(document.Kind, PrintOutcomeStatus.Succeeded, "Printed.", document);
}

public sealed record PrintDispatchResult(IReadOnlyList<PrintDocumentResult> Documents)
{
    public bool Succeeded => Documents.Count == 2 && Documents.All(document => document.Succeeded);
    public bool AnyAttempted => Documents.Count > 0;
    public IReadOnlyList<ValidationIssue> Issues => Documents
        .Where(document => !document.Succeeded)
        .Select(document => new ValidationIssue(document.Kind == PrintDocumentKind.Kitchen ? "kitchen-print" : "customer-print", document.OperatorMessage, ValidationCodes.Generic))
        .ToArray();

    public static PrintDispatchResult From(params PrintDocumentResult[] documents) => new(documents);
}

/// <summary>Extended M08 seam; the legacy dispatcher method remains for M04-compatible fakes.</summary>
public interface IOrderPrintOutcomeDispatcher : Sushi81.Pos.Application.OrderEntry.IOrderPrintDispatcher
{
    Task<PrintDispatchResult> DispatchInitialAsync(OrderSnapshot committedOrder, PrintIntent intent = PrintIntent.InitialAutomatic, CancellationToken cancellationToken = default);
    Task<PrintDocumentResult> PrintDocumentAsync(OrderSnapshot committedOrder, PrintDocumentKind kind, PrintIntent intent = PrintIntent.ExplicitReprint, CancellationToken cancellationToken = default);
}

public interface IOrderPrintApplicationService
{
    Task<PrintDocumentResult> ReprintAsync(Guid orderId, PrintDocumentKind kind, CancellationToken cancellationToken = default);

    Task<PrintDocumentResult> RetryInitialAsync(Guid orderId, PrintDocumentKind kind, CancellationToken cancellationToken = default);
}

public interface IPrintDocumentSubmitter
{
    Task<PrintOutcomeStatus> SubmitAsync(OrderPrintDocument document, string? configuredQueueId, string? configuredQueueName, CancellationToken cancellationToken = default);
}

public sealed record PrintQueueInfo(string Id, string Name);

public interface IPrintQueueCatalog
{
    Task<IReadOnlyList<PrintQueueInfo>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed class OrderPrintDocumentFactory(IBusinessClock clock)
{
    private const int KitchenWidth = 42;
    private const int CustomerWidth = 32;
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public OrderPrintDocument Create(OrderSnapshot order, ReceiptIdentity identity, PrintDocumentKind kind, PrintIntent intent)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsComplete) throw new ArgumentException("Receipt identity is incomplete.", nameof(identity));
        return kind == PrintDocumentKind.Kitchen
            ? new(kind, intent, order.Id, Reference(order), RenderKitchen(order, intent), IsFuture(order), order.Status == OrderStatus.Cancelled)
            : new(kind, intent, order.Id, Reference(order), RenderCustomer(order, identity, intent), IsFuture(order), order.Status == OrderStatus.Cancelled);
    }

    private bool IsFuture(OrderSnapshot order) => order.PlannedFulfilmentDate > clock.BusinessDate;

    private static string Reference(OrderSnapshot order) => string.IsNullOrWhiteSpace(order.Reference) ? order.Id.ToString("N")[..8] : order.Reference;

    private string RenderKitchen(OrderSnapshot order, PrintIntent intent)
    {
        var lines = new List<string> { Center("*** CUISINE ***", KitchenWidth), Center(Reference(order), KitchenWidth), $"Heure : {FormatDateTime(order.CreatedAt)}" };
        AddMarkings(lines, order, intent, KitchenWidth);
        lines.Add(Dashes(KitchenWidth));
        AddOptional(lines, $"Mode : {(order.Fulfilment == FulfilmentMode.Retrait ? "Retrait" : "Livraison")}", KitchenWidth);
        if (IsFuture(order)) AddOptional(lines, $"FUTURE : {FormatDate(order.PlannedFulfilmentDate)} {FormatTime(order.PlannedFulfilmentTime)}", KitchenWidth);
        else AddOptional(lines, $"Prévu : {FormatDate(order.PlannedFulfilmentDate)} {FormatTime(order.PlannedFulfilmentTime)}", KitchenWidth);
        AddWrapped(lines, "Tél : ", order.Telephone, KitchenWidth);
        AddWrapped(lines, "Adresse : ", order.DeliveryAddress, KitchenWidth);
        AddWrapped(lines, "Note : ", order.Comment, KitchenWidth);
        lines.Add(Dashes(KitchenWidth));
        foreach (var item in order.Items.OrderBy(item => item.Position))
        {
            AddWrapped(lines, string.Empty, $"{item.Quantity}x {item.ProductCode} {item.ProductName}", KitchenWidth);
            foreach (var adjustment in item.Adjustments.OrderBy(adjustment => adjustment.DisplayOrder))
                AddWrapped(lines, "  - ", $"{adjustment.Label} ({FormatMoney(adjustment.AdjustmentTtcPerUnit)})", KitchenWidth);
        }
        lines.Add(Dashes(KitchenWidth));
        lines.Add(Center($"TOTAL : {FormatMoney(order.TotalTtc)} EUR", KitchenWidth));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private string RenderCustomer(OrderSnapshot order, ReceiptIdentity identity, PrintIntent intent)
    {
        var lines = new List<string>
        {
            Center(identity.BusinessName, CustomerWidth),
            Center(identity.AddressLine1, CustomerWidth),
            Center(identity.AddressLine2, CustomerWidth),
            Center($"SIRET {identity.Siret}", CustomerWidth),
            Center($"TVA {identity.VatNumber}", CustomerWidth),
            Center($"APE {identity.ActivityCode}", CustomerWidth),
            string.Empty,
            Center(Reference(order), CustomerWidth),
            Center(FormatDateTime(order.CreatedAt), CustomerWidth)
        };
        AddMarkings(lines, order, intent, CustomerWidth);
        lines.Add(Dashes(CustomerWidth));
        AddOptional(lines, $"Mode : {(order.Fulfilment == FulfilmentMode.Retrait ? "Retrait" : "Livraison")}", CustomerWidth);
        AddOptional(lines, $"{(IsFuture(order) ? "FUTURE" : "Prévu")} : {FormatDate(order.PlannedFulfilmentDate)} {FormatTime(order.PlannedFulfilmentTime)}", CustomerWidth);
        AddWrapped(lines, "Tél : ", order.Telephone, CustomerWidth);
        AddWrapped(lines, "Adresse : ", order.DeliveryAddress, CustomerWidth);
        lines.Add(Dashes(CustomerWidth));
        foreach (var item in order.Items.OrderBy(item => item.Position))
        {
            AddWrapped(lines, string.Empty, $"{item.Quantity} x {item.ProductCode} {item.ProductName}", CustomerWidth);
            AddWrapped(lines, "  ", FormatMoney(item.CalculatedLineTotalTtc) + " EUR", CustomerWidth);
            foreach (var adjustment in item.Adjustments.OrderBy(adjustment => adjustment.DisplayOrder))
                AddWrapped(lines, "  - ", $"{adjustment.Label} {FormatMoney(adjustment.AdjustmentTtcPerUnit)}", CustomerWidth);
        }
        lines.Add(Dashes(CustomerWidth));
        foreach (var tax in order.TaxBreakdown.OrderBy(tax => tax.VatRate))
        {
            var net = tax.TaxableTtc - tax.IncludedVatTtc;
            lines.Add($"HT {tax.VatRate:0.#}% {FormatMoney(net)} EUR");
            lines.Add($"TVA {tax.VatRate:0.#}% {FormatMoney(tax.IncludedVatTtc)} EUR");
        }
        lines.Add(Dashes(CustomerWidth));
        lines.Add(Center($"Total EUR {FormatMoney(order.TotalTtc)}", CustomerWidth));
        lines.Add($"CB       {FormatMoney(order.CardPaymentTtc)} EUR");
        lines.Add($"Espèce   {FormatMoney(order.CashPaymentTtc)} EUR");
        lines.Add(string.Empty);
        lines.Add(Center("Sushi81 POS", CustomerWidth));
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static void AddMarkings(List<string> lines, OrderSnapshot order, PrintIntent intent, int width)
    {
        if (order.Status == OrderStatus.Cancelled) lines.Add(Center("ANNULÉ", width));
        if (intent == PrintIntent.ExplicitReprint)
            lines.Add(Center(width == KitchenWidth ? "RÉIMPRESSION" : "DUPLICATA", width));
    }

    private static void AddOptional(List<string> lines, string value, int width) => AddWrapped(lines, string.Empty, value, width);

    private static void AddWrapped(List<string> lines, string prefix, string? value, int width)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var firstPrefix = prefix ?? string.Empty;
        var available = Math.Max(1, width - firstPrefix.Length);
        var chunks = Wrap(value.Trim(), available);
        if (chunks.Count == 0) return;
        lines.Add(firstPrefix + chunks[0]);
        var continuation = new string(' ', firstPrefix.Length);
        foreach (var chunk in chunks.Skip(1)) lines.Add(continuation + chunk);
    }

    public static IReadOnlyList<string> Wrap(string value, int width)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        var result = new List<string>();
        foreach (var paragraph in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var remaining = paragraph.Trim();
            if (remaining.Length == 0) { result.Add(string.Empty); continue; }
            while (remaining.Length > width)
            {
                var cut = remaining.LastIndexOf(' ', width - 1);
                if (cut <= 0) cut = width;
                result.Add(remaining[..cut].TrimEnd());
                remaining = remaining[cut..].TrimStart();
            }
            result.Add(remaining);
        }
        return result;
    }

    private static string Center(string value, int width)
    {
        if (value.Length >= width) return value;
        var left = (width - value.Length) / 2;
        return new string(' ', left) + value;
    }

    private static string Dashes(int width) => new('-', width);
    private static string FormatMoney(Money money) => money.Euros.ToString("0.00", CultureInfo.InvariantCulture);
    private static string FormatDate(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string FormatTime(TimeOnly? value) => value?.ToString("HH\\:mm", CultureInfo.InvariantCulture) ?? "—";
    private static string FormatDateTime(DateTimeOffset value) => value.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}

public sealed class OrderPrintApplicationService(IOrderStore orders, Sushi81.Pos.Application.OrderEntry.IOrderPrintDispatcher dispatcher) : IOrderPrintApplicationService
{
    private readonly IOrderStore orders = orders ?? throw new ArgumentNullException(nameof(orders));
    private readonly Sushi81.Pos.Application.OrderEntry.IOrderPrintDispatcher dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    public async Task<PrintDocumentResult> ReprintAsync(Guid orderId, PrintDocumentKind kind, CancellationToken cancellationToken = default)
    {
        return await PrintAsync(orderId, kind, PrintIntent.ExplicitReprint, cancellationToken);
    }

    public async Task<PrintDocumentResult> RetryInitialAsync(Guid orderId, PrintDocumentKind kind, CancellationToken cancellationToken = default)
    {
        return await PrintAsync(orderId, kind, PrintIntent.InitialRetry, cancellationToken);
    }

    private async Task<PrintDocumentResult> PrintAsync(Guid orderId, PrintDocumentKind kind, PrintIntent intent, CancellationToken cancellationToken)
    {
        var order = await orders.GetByIdAsync(orderId, cancellationToken);
        if (order is null) return new(kind, PrintOutcomeStatus.OrderNotFound, "The committed order could not be found.");
        if (dispatcher is not IOrderPrintOutcomeDispatcher output)
            return new(kind, PrintOutcomeStatus.Unsupported, "Printing is not configured on this device.");
        return await output.PrintDocumentAsync(order, kind, intent, cancellationToken);
    }
}
