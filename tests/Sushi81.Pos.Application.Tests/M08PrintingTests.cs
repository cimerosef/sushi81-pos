using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class M08PrintingTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 15, 0, TimeSpan.Zero);
    private static readonly DateOnly BusinessDate = new(2026, 9, 12);

    [TestMethod]
    public void KitchenDocumentIsDeterministicAndUsesCommittedHistoricalFacts()
    {
        var order = CreateOrder(BusinessDate.AddDays(2), OrderStatus.Open);
        var factory = new OrderPrintDocumentFactory(new FixedClock());

        var document = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Kitchen, PrintIntent.ExplicitReprint);

        StringAssert.Contains(document.Text, "*** CUISINE ***");
        StringAssert.Contains(document.Text, "RÉIMPRESSION");
        StringAssert.Contains(document.Text, "FUTURE : 14/09/2026 18:30");
        StringAssert.Contains(document.Text, "1x P-001 Plat historique");
        StringAssert.Contains(document.Text, "Option sauvegardée");
        StringAssert.Contains(document.Text, "Note persistée");
        StringAssert.Contains(document.Text, "TOTAL : 12.50 EUR");
        Assert.IsTrue(document.IsFuture);
        Assert.IsFalse(document.Text.Contains("Catalogue actuel", StringComparison.Ordinal));
    }

    [TestMethod]
    public void CustomerDocumentContainsApprovedIdentityLatestPaymentAndDuplicateMarking()
    {
        var order = CreateOrder(BusinessDate, OrderStatus.Open) with
        {
            CardPaymentTtc = Money.FromCents(700),
            CashPaymentTtc = Money.FromCents(550)
        };
        var factory = new OrderPrintDocumentFactory(new FixedClock());

        var document = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.ExplicitReprint);

        StringAssert.Contains(document.Text, "Sushi 81");
        StringAssert.Contains(document.Text, "90805211100014");
        StringAssert.Contains(document.Text, "FR03908052111");
        StringAssert.Contains(document.Text, "DUPLICATA");
        StringAssert.Contains(document.Text, "CB       7.00 EUR");
        StringAssert.Contains(document.Text, "Espèce   5.50 EUR");
        StringAssert.Contains(document.Text, "TVA 10%");
    }

    [TestMethod]
    public void CancelledDocumentsKeepCancellationAndReprintMarkings()
    {
        var order = CreateOrder(BusinessDate, OrderStatus.Cancelled);
        var factory = new OrderPrintDocumentFactory(new FixedClock());

        var kitchen = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Kitchen, PrintIntent.ExplicitReprint);
        var customer = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.ExplicitReprint);

        StringAssert.Contains(kitchen.Text, "ANNULÉ");
        StringAssert.Contains(kitchen.Text, "RÉIMPRESSION");
        StringAssert.Contains(customer.Text, "ANNULÉ");
        StringAssert.Contains(customer.Text, "DUPLICATA");
    }

    [TestMethod]
    public async Task ReprintServiceReloadsLatestCommittedOrderBeforeRendering()
    {
        var latest = CreateOrder(BusinessDate, OrderStatus.Open) with { Comment = "Dernier état committé" };
        var dispatcher = new RecordingOutcomeDispatcher();
        var service = new OrderPrintApplicationService(new RecordingOrderStore(latest), dispatcher);

        var result = await service.ReprintAsync(latest.Id, PrintDocumentKind.Customer);

        Assert.IsTrue(result.Succeeded);
        Assert.AreSame(latest, dispatcher.LastOrder);
        Assert.AreEqual(PrintIntent.ExplicitReprint, dispatcher.LastIntent);
        Assert.AreEqual(PrintDocumentKind.Customer, dispatcher.LastKind);
    }

    private static OrderSnapshot CreateOrder(DateOnly plannedDate, OrderStatus status) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            OrderSourceType.Pos,
            status,
            Now,
            Now,
            status == OrderStatus.Closed ? Now : null,
            status == OrderStatus.Cancelled ? Now : null,
            FulfilmentMode.Livraison,
            plannedDate,
            new TimeOnly(18, 30),
            plannedDate > BusinessDate,
            "06 12 34 56 78",
            "12 rue de l’adresse",
            "Note persistée",
            Money.FromCents(1250),
            false,
            false,
            null,
            Money.Zero,
            [
                new(
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    0,
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "P-001",
                    "Plat historique",
                    "Plats",
                    Money.FromCents(1000),
                    10m,
                    true,
                    1,
                    Money.FromCents(1000),
                    Money.FromCents(1250),
                    [new(Guid.Parse("44444444-4444-4444-4444-444444444444"), 0, OrderAdjustmentKind.PredefinedOption, Guid.NewGuid(), "Options", "Option sauvegardée", Money.FromCents(250), 10m)])
            ],
            [new(10m, Money.FromCents(1250), Money.FromCents(113))])
        {
            Reference = "20260912-001"
        };

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => Now;
        public DateOnly BusinessDate => M08PrintingTests.BusinessDate;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingOrderStore(OrderSnapshot latest) : IOrderStore
    {
        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(latest);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
    }

    private sealed class RecordingOutcomeDispatcher : IOrderPrintOutcomeDispatcher
    {
        public OrderSnapshot? LastOrder { get; private set; }
        public PrintDocumentKind LastKind { get; private set; }
        public PrintIntent LastIntent { get; private set; }
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PrintDispatchResult> DispatchInitialAsync(OrderSnapshot committedOrder, PrintIntent intent = PrintIntent.InitialAutomatic, CancellationToken cancellationToken = default) =>
            Task.FromResult(PrintDispatchResult.From());
        public Task<PrintDocumentResult> PrintDocumentAsync(OrderSnapshot committedOrder, PrintDocumentKind kind, PrintIntent intent = PrintIntent.ExplicitReprint, CancellationToken cancellationToken = default)
        {
            LastOrder = committedOrder;
            LastKind = kind;
            LastIntent = intent;
            return Task.FromResult(PrintDocumentResult.Success(new(kind, intent, committedOrder.Id, committedOrder.Reference, "synthetic", false, false)));
        }
    }
}
