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

public enum PrintReceiptBlockKind
{
    LegacyText,
    BusinessName,
    Identity,
    LegalIdentity,
    Heading,
    Reference,
    Timestamp,
    Ticket,
    Marker,
    Separator,
    LabelValue,
    CustomerInfo,
    Item,
    Option,
    ItemAmount,
    Tax,
    Total,
    Payment,
    PaymentConfirmation,
    Footer
}

public sealed record PrintReceiptItem(
    string QuantityText,
    string Description,
    string UnitPriceText,
    string LineTotalText);

public sealed record PrintReceiptOption(
    string Description,
    string AmountText);

/// <summary>
/// Printer-independent receipt content. Text is kept as semantic values; alignment,
/// wrapping, font choice and pagination belong to the infrastructure print boundary.
/// </summary>
public sealed record PrintReceiptBlock(
    PrintReceiptBlockKind Kind,
    string Text,
    string? SecondaryText = null,
    string? TertiaryText = null,
    string? AtomicGroup = null)
{
    public PrintReceiptItem? Item { get; init; }

    public PrintReceiptOption? Option { get; init; }
}

public sealed record PrintReceiptContent(IReadOnlyList<PrintReceiptBlock> Blocks)
{
    public static PrintReceiptContent FromLegacyText(string text) =>
        new([new(PrintReceiptBlockKind.LegacyText, text ?? string.Empty)]);

    public string ToDiagnosticText() => string.Join(
        Environment.NewLine,
        Blocks.Select(block => block.Kind == PrintReceiptBlockKind.Separator
            ? block.Text
            : string.Join(" ", new[] { block.Text, block.SecondaryText, block.TertiaryText }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()))));
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
    /// <summary>Structured content used by the physical printer renderer.</summary>
    public PrintReceiptContent Content { get; init; } = PrintReceiptContent.FromLegacyText(Text);

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
        .Select(document => new ValidationIssue(
            document.Kind == PrintDocumentKind.Kitchen ? "kitchen-print" : "customer-print",
            document.OperatorMessage,
            document.Status == PrintOutcomeStatus.AmbiguousSubmission ? ValidationCodes.PrintAmbiguous : ValidationCodes.Generic))
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
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public OrderPrintDocument Create(OrderSnapshot order, ReceiptIdentity identity, PrintDocumentKind kind, PrintIntent intent)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(identity);
        if (!identity.IsComplete) throw new ArgumentException("Receipt identity is incomplete.", nameof(identity));
        var content = kind == PrintDocumentKind.Kitchen
            ? BuildKitchen(order, intent)
            : BuildCustomer(order, identity, intent);
        return new(kind, intent, order.Id, Reference(order), content.ToDiagnosticText(), IsFuture(order), order.Status == OrderStatus.Cancelled)
        {
            Content = content
        };
    }

    private bool IsFuture(OrderSnapshot order) => order.PlannedFulfilmentDate > clock.BusinessDate;

    private static string Reference(OrderSnapshot order) => string.IsNullOrWhiteSpace(order.Reference) ? order.Id.ToString("N")[..8] : order.Reference;

    private PrintReceiptContent BuildKitchen(OrderSnapshot order, PrintIntent intent)
    {
        var blocks = new List<PrintReceiptBlock>
        {
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***", AtomicGroup: "kitchen-header"),
            new(PrintReceiptBlockKind.LabelValue, "Cmd", Reference(order), AtomicGroup: "kitchen-header"),
            new(PrintReceiptBlockKind.LabelValue, "Heure", FormatTime(order.CreatedAt), AtomicGroup: "kitchen-header")
        };
        AddMarkings(blocks, order, PrintDocumentKind.Kitchen, intent);
        blocks.Add(new(PrintReceiptBlockKind.Separator, "-"));
        AddLabelValue(blocks, "Mode", order.Fulfilment == FulfilmentMode.Retrait ? "Retrait" : "Livraison");
        AddLabelValue(blocks, IsFuture(order) ? "FUTURE" : "Prévu", $"{FormatDate(order.PlannedFulfilmentDate)} {FormatTime(order.PlannedFulfilmentTime)}");
        AddLabelValue(blocks, "Tél", order.Telephone);
        AddLabelValue(blocks, "Adresse", order.DeliveryAddress);
        AddLabelValue(blocks, "Note", order.Comment);
        blocks.Add(new(PrintReceiptBlockKind.Separator, "-"));
        foreach (var item in order.Items.OrderBy(item => item.Position))
        {
            blocks.Add(new(PrintReceiptBlockKind.Item, $"{item.Quantity}x {item.ProductCode}", item.ProductName));
            foreach (var adjustment in item.Adjustments.OrderBy(adjustment => adjustment.DisplayOrder))
                blocks.Add(new(PrintReceiptBlockKind.Option, adjustment.Label, FormatMoney(adjustment.AdjustmentTtcPerUnit)));
        }
        blocks.Add(new(PrintReceiptBlockKind.Separator, "-"));
        blocks.Add(new(PrintReceiptBlockKind.Total, "TOTAL", $"{FormatMoney(order.TotalTtc)} EUR", AtomicGroup: "kitchen-total"));
        return new(blocks);
    }

    private PrintReceiptContent BuildCustomer(OrderSnapshot order, ReceiptIdentity identity, PrintIntent intent)
    {
        var blocks = new List<PrintReceiptBlock>
        {
            new(PrintReceiptBlockKind.BusinessName, identity.BusinessName, AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.Identity, identity.AddressLine1, AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.Identity, identity.AddressLine2, AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.LegalIdentity, $"{identity.Siret} {identity.VatNumber} {identity.ActivityCode}", AtomicGroup: "customer-identity"),
            new(PrintReceiptBlockKind.Ticket, Reference(order), FormatDateTime(order.CreatedAt), AtomicGroup: "customer-ticket")
        };
        AddMarkings(blocks, order, PrintDocumentKind.Customer, intent);
        blocks.Add(new(PrintReceiptBlockKind.Separator, "-"));
        AddCustomerInfo(blocks, "Mode", order.Fulfilment == FulfilmentMode.Retrait ? "Retrait" : "Livraison");
        AddCustomerInfo(blocks, IsFuture(order) ? "FUTURE" : "Prévu", $"{FormatDate(order.PlannedFulfilmentDate)} {FormatTime(order.PlannedFulfilmentTime)}");
        AddCustomerInfo(blocks, "Tél", order.Telephone);
        AddCustomerInfo(blocks, "Adresse", order.DeliveryAddress);
        blocks.Add(new(PrintReceiptBlockKind.Separator, "-"));
        foreach (var item in order.Items.OrderBy(item => item.Position))
        {
            blocks.Add(new(PrintReceiptBlockKind.Item, $"{item.Quantity} x {item.ProductCode}", item.ProductName)
            {
                Item = new(
                    $"{item.Quantity}x",
                    $"{item.ProductCode} {item.ProductName}",
                    FormatMoney(item.ProductBasePriceTtc),
                    item.Quantity > 1 ? FormatMoney(item.ExtendedBaseTtc) : string.Empty)
            });
            foreach (var adjustment in item.Adjustments.OrderBy(adjustment => adjustment.DisplayOrder))
                blocks.Add(new(PrintReceiptBlockKind.Option, adjustment.Label, FormatMoney(adjustment.AdjustmentTtcPerUnit))
                {
                    Option = new(adjustment.Label, FormatMoney(adjustment.AdjustmentTtcPerUnit))
                });
        }
        blocks.Add(new(PrintReceiptBlockKind.Separator, "-"));
        var totalHt = order.TaxBreakdown.Aggregate(Money.Zero, (total, tax) => total + tax.TaxableTtc - tax.IncludedVatTtc);
        blocks.Add(new(PrintReceiptBlockKind.Tax, "Total HT", $"{FormatMoney(totalHt)} EUR", AtomicGroup: "customer-tax"));
        foreach (var tax in order.TaxBreakdown.OrderBy(tax => tax.VatRate))
        {
            var net = tax.TaxableTtc - tax.IncludedVatTtc;
            blocks.Add(new(PrintReceiptBlockKind.Tax, $"TVA {tax.VatRate:0.#}%", $"{FormatMoney(tax.IncludedVatTtc)} EUR", $"base {FormatMoney(net)} EUR", "customer-tax"));
        }
        blocks.Add(new(PrintReceiptBlockKind.Separator, "-"));
        blocks.Add(new(PrintReceiptBlockKind.Total, "Total EUR", FormatMoney(order.TotalTtc), AtomicGroup: "customer-total"));
        AddSettledPayments(blocks, order);
        blocks.Add(new(PrintReceiptBlockKind.Footer, "Merci de votre visite !", AtomicGroup: "customer-footer"));
        blocks.Add(new(PrintReceiptBlockKind.Footer, "www.sushi81.fr", AtomicGroup: "customer-footer"));
        return new(blocks);
    }

    private static void AddMarkings(List<PrintReceiptBlock> blocks, OrderSnapshot order, PrintDocumentKind kind, PrintIntent intent)
    {
        if (order.Status == OrderStatus.Cancelled) blocks.Add(new(PrintReceiptBlockKind.Marker, "ANNULÉ"));
        if (intent == PrintIntent.ExplicitReprint)
            blocks.Add(new(PrintReceiptBlockKind.Marker, kind == PrintDocumentKind.Kitchen ? "RÉIMPRESSION" : "DUPLICATA"));
    }

    private static void AddLabelValue(List<PrintReceiptBlock> blocks, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) blocks.Add(new(PrintReceiptBlockKind.LabelValue, label, value.Trim()));
    }

    private static void AddCustomerInfo(List<PrintReceiptBlock> blocks, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) blocks.Add(new(PrintReceiptBlockKind.CustomerInfo, label, value.Trim()));
    }

    private static void AddSettledPayments(List<PrintReceiptBlock> blocks, OrderSnapshot order)
    {
        var payment = OrderPaymentState.From(order);
        if (!payment.IsExactlyReconciled) return;

        var modes = new List<string>(2);
        if (order.CardPaymentTtc > Money.Zero)
        {
            blocks.Add(new(PrintReceiptBlockKind.Payment, "CB", $"{FormatMoney(order.CardPaymentTtc)} EUR", AtomicGroup: "customer-payment"));
            modes.Add("CB");
        }
        if (order.CashPaymentTtc > Money.Zero)
        {
            blocks.Add(new(PrintReceiptBlockKind.Payment, "Espèce", $"{FormatMoney(order.CashPaymentTtc)} EUR", AtomicGroup: "customer-payment"));
            modes.Add("Espèce");
        }
        if (modes.Count > 0)
            blocks.Add(new(PrintReceiptBlockKind.PaymentConfirmation, "Payé en", string.Join(" + ", modes) + " TVA incluse", AtomicGroup: "customer-payment"));
    }

    private static string FormatMoney(Money money) => money.Euros.ToString("0.00", CultureInfo.InvariantCulture);
    private static string FormatDate(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string FormatTime(TimeOnly? value) => value?.ToString("HH\\:mm", CultureInfo.InvariantCulture) ?? "—";
    private static string FormatTime(DateTimeOffset value) => value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
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
