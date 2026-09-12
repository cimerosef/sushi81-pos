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

        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Heading && block.Text == "*** CUISINE ***"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "RÉIMPRESSION"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.LabelValue && block.Text == "FUTURE" && block.SecondaryText == "14/09/2026 18:30"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Item && block.Text == "1x P-001" && block.SecondaryText == "Plat historique"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Option && block.Text == "Option sauvegardée"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.LabelValue && block.Text == "Note" && block.SecondaryText == "Note persistée"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Total && block.Text == "TOTAL" && block.SecondaryText == "12.50 EUR"));
        Assert.IsTrue(document.IsFuture);
        Assert.IsFalse(document.Content.ToDiagnosticText().Contains("Catalogue actuel", StringComparison.Ordinal));
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

        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Identity && block.Text == "Sushi 81"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Identity && block.Text == "SIRET" && block.SecondaryText == "90805211100014"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Identity && block.Text == "TVA" && block.SecondaryText == "FR03908052111"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "DUPLICATA"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "CB" && block.SecondaryText == "7.00 EUR"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "Espèce" && block.SecondaryText == "5.50 EUR"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Tax && block.Text == "TVA 10%"));
    }

    [TestMethod]
    public void CancelledDocumentsKeepCancellationAndReprintMarkings()
    {
        var order = CreateOrder(BusinessDate, OrderStatus.Cancelled);
        var factory = new OrderPrintDocumentFactory(new FixedClock());

        var kitchen = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Kitchen, PrintIntent.ExplicitReprint);
        var customer = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.ExplicitReprint);

        Assert.IsTrue(kitchen.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "ANNULÉ"));
        Assert.IsTrue(kitchen.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "RÉIMPRESSION"));
        Assert.IsTrue(customer.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "ANNULÉ"));
        Assert.IsTrue(customer.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "DUPLICATA"));
        Assert.AreEqual(1, customer.Content.Blocks.Count(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "ANNULÉ"));
    }

    [TestMethod]
    public void KnownInitialRetryKeepsInitialTicketMarkingWhileExplicitReprintMarksAnAdditionalCopy()
    {
        var order = CreateOrder(BusinessDate, OrderStatus.Open);
        var factory = new OrderPrintDocumentFactory(new FixedClock());

        var initialRetry = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.InitialRetry);
        var explicitReprint = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.ExplicitReprint);

        Assert.IsFalse(initialRetry.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "DUPLICATA"));
        Assert.IsTrue(explicitReprint.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "DUPLICATA"));
    }

    [TestMethod]
    public void AmbiguousSubmissionIsNotKnownFailureAndKeepsAStatusAwareIssue()
    {
        var document = new OrderPrintDocument(
            PrintDocumentKind.Kitchen,
            PrintIntent.InitialAutomatic,
            Guid.NewGuid(),
            "20260912-001",
            "synthetic",
            false,
            false);
        var ambiguous = new PrintDocumentResult(
            PrintDocumentKind.Kitchen,
            PrintOutcomeStatus.AmbiguousSubmission,
            "The kitchen print result is uncertain.",
            document);
        var knownFailure = new PrintDocumentResult(
            PrintDocumentKind.Customer,
            PrintOutcomeStatus.QueueUnavailable,
            "The customer printer is unavailable.",
            document with { Kind = PrintDocumentKind.Customer });

        Assert.IsFalse(ambiguous.IsKnownFailure);
        Assert.IsTrue(knownFailure.IsKnownFailure);
        var issues = PrintDispatchResult.From(ambiguous, knownFailure).Issues;
        Assert.AreEqual(ValidationCodes.PrintAmbiguous, issues.Single(issue => issue.Field == "kitchen-print").StableCode);
        Assert.AreEqual(ValidationCodes.Generic, issues.Single(issue => issue.Field == "customer-print").StableCode);
    }

    [TestMethod]
    public void IndependentInitialOutputMatrixKeepsKitchenAndCustomerFailuresSeparate()
    {
        var vectors = new[]
        {
            (Kitchen: PrintOutcomeStatus.Succeeded, Customer: PrintOutcomeStatus.Succeeded, Succeeded: true, KnownFailures: 0),
            (Kitchen: PrintOutcomeStatus.QueueUnavailable, Customer: PrintOutcomeStatus.Succeeded, Succeeded: false, KnownFailures: 1),
            (Kitchen: PrintOutcomeStatus.Succeeded, Customer: PrintOutcomeStatus.SubmissionFailed, Succeeded: false, KnownFailures: 1),
            (Kitchen: PrintOutcomeStatus.GenerationFailed, Customer: PrintOutcomeStatus.QueueUnavailable, Succeeded: false, KnownFailures: 2),
            (Kitchen: PrintOutcomeStatus.AmbiguousSubmission, Customer: PrintOutcomeStatus.Succeeded, Succeeded: false, KnownFailures: 0)
        };

        foreach (var vector in vectors)
        {
            var kitchen = new PrintDocumentResult(PrintDocumentKind.Kitchen, vector.Kitchen, vector.Kitchen.ToString());
            var customer = new PrintDocumentResult(PrintDocumentKind.Customer, vector.Customer, vector.Customer.ToString());
            var output = PrintDispatchResult.From(kitchen, customer);

            Assert.AreEqual(vector.Succeeded, output.Succeeded, vector.ToString());
            Assert.AreEqual(vector.KnownFailures, output.Documents.Count(document => document.IsKnownFailure), vector.ToString());
            Assert.HasCount(vector.Kitchen == PrintOutcomeStatus.Succeeded ? 0 : 1, output.Documents.Where(document => document.Kind == PrintDocumentKind.Kitchen && !document.Succeeded));
            Assert.HasCount(vector.Customer == PrintOutcomeStatus.Succeeded ? 0 : 1, output.Documents.Where(document => document.Kind == PrintDocumentKind.Customer && !document.Succeeded));
        }
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

    [TestMethod]
    public async Task InitialRetryServiceReloadsLatestCommittedOrderWithInitialIntent()
    {
        var latest = CreateOrder(BusinessDate, OrderStatus.Open);
        var dispatcher = new RecordingOutcomeDispatcher();
        var service = new OrderPrintApplicationService(new RecordingOrderStore(latest), dispatcher);

        var result = await service.RetryInitialAsync(latest.Id, PrintDocumentKind.Kitchen);

        Assert.IsTrue(result.Succeeded);
        Assert.AreSame(latest, dispatcher.LastOrder);
        Assert.AreEqual(PrintIntent.InitialRetry, dispatcher.LastIntent);
        Assert.AreEqual(PrintDocumentKind.Kitchen, dispatcher.LastKind);
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
