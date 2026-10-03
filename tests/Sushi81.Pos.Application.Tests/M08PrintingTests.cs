using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Archive;
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

        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.BusinessName && block.Text == "Sushi 81"));
        Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.LegalIdentity && block.Text == "90805211100014 FR03908052111 5610C"));
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
    public void PreProductionPrintsAndReprintsAreMarkedWhileProductionOutputStaysUnmarked()
    {
        var order = CreateOrder(BusinessDate, OrderStatus.Open);
        var productionFactory = new OrderPrintDocumentFactory(new FixedClock(), DeploymentProfile.Production);
        var preProductionFactory = new OrderPrintDocumentFactory(new FixedClock(), DeploymentProfile.PreProduction);

        foreach (var kind in new[] { PrintDocumentKind.Kitchen, PrintDocumentKind.Customer })
        foreach (var intent in new[] { PrintIntent.InitialAutomatic, PrintIntent.ExplicitReprint })
        {
            var production = productionFactory.Create(order, ReceiptIdentity.Default, kind, intent);
            var preProduction = preProductionFactory.Create(order, ReceiptIdentity.Default, kind, intent);

            Assert.IsFalse(production.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "*** PREPROD ***"));
            Assert.IsTrue(preProduction.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "*** PREPROD ***"));
        }
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
    public void UnsettledCustomerReceiptOmitsPaymentRowsAndPaidConfirmation()
    {
        var order = CreateOrder(BusinessDate, OrderStatus.Open);
        var document = new OrderPrintDocumentFactory(new FixedClock()).Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.InitialAutomatic);

        Assert.IsFalse(document.Content.Blocks.Any(block => block.Kind is PrintReceiptBlockKind.Payment or PrintReceiptBlockKind.PaymentConfirmation));
        Assert.IsFalse(document.Content.ToDiagnosticText().Contains("Payé en", StringComparison.Ordinal));
    }

    [TestMethod]
    public void SettledCustomerReceiptShowsOnlyPositiveCommittedMethodsAndTruthfulConfirmation()
    {
        var factory = new OrderPrintDocumentFactory(new FixedClock());
        var cardOnly = factory.Create(
            CreateOrder(BusinessDate, OrderStatus.Open) with { CardPaymentTtc = Money.FromCents(1250) },
            ReceiptIdentity.Default,
            PrintDocumentKind.Customer,
            PrintIntent.InitialAutomatic);
        var mixed = factory.Create(
            CreateOrder(BusinessDate, OrderStatus.Open) with
            {
                CardPaymentTtc = Money.FromCents(700),
                CashPaymentTtc = Money.FromCents(550)
            },
            ReceiptIdentity.Default,
            PrintDocumentKind.Customer,
            PrintIntent.InitialAutomatic);

        Assert.IsTrue(cardOnly.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "CB" && block.SecondaryText == "12.50 EUR"));
        Assert.IsFalse(cardOnly.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "Espèce"));
        Assert.IsTrue(cardOnly.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.PaymentConfirmation && block.SecondaryText == "CB TVA incluse"));
        Assert.IsTrue(mixed.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "CB"));
        Assert.IsTrue(mixed.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "Espèce"));
        Assert.IsTrue(mixed.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.PaymentConfirmation && block.SecondaryText == "CB + Espèce TVA incluse"));
    }

    [TestMethod]
    public void AppliedPickupDiscountUsesCommittedLineSnapshotsAcrossPaymentAndPrintVariants()
    {
        var factory = new OrderPrintDocumentFactory(new FixedClock(), DeploymentProfile.PreProduction);
        foreach (var status in new[] { OrderStatus.Open, OrderStatus.Cancelled })
        foreach (var intent in new[] { PrintIntent.InitialAutomatic, PrintIntent.InitialRetry, PrintIntent.ExplicitReprint })
        foreach (var payment in new[] { (Card: 0L, Cash: 0L), (Card: 2367L, Cash: 0L), (Card: 0L, Cash: 2367L), (Card: 1200L, Cash: 1167L) })
        {
            var order = CreateDiscountedOrder(status) with
            {
                CardPaymentTtc = Money.FromCents(payment.Card),
                CashPaymentTtc = Money.FromCents(payment.Cash)
            };
            var document = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, intent);
            var remise = document.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Discount);

            Assert.AreEqual("Remise", remise.Text);
            Assert.AreEqual("-2.63 EUR", remise.SecondaryText);
            Assert.AreEqual("23.67", document.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Total).SecondaryText);
            Assert.AreEqual(payment.Card > 0, document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "CB"));
            Assert.AreEqual(payment.Cash > 0, document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Payment && block.Text == "Espèce"));
            Assert.IsTrue(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "*** PREPROD ***"));
            Assert.AreEqual(status == OrderStatus.Cancelled, document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "ANNULÉ"));
            Assert.AreEqual(intent == PrintIntent.ExplicitReprint, document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Marker && block.Text == "DUPLICATA"));
            Assert.IsFalse(document.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.HandwritingSpace));
        }
    }

    [TestMethod]
    public void PickupDiscountExcludesPositiveOptionsAndManualOverrideDelta()
    {
        var original = CreateDiscountedOrder(OrderStatus.Open);
        var item = original.Items[0] with
        {
            ProductBasePriceTtc = Money.FromCents(1000),
            ExtendedBaseTtc = Money.FromCents(1000),
            CalculatedLineTotalTtc = Money.FromCents(1060),
            Adjustments =
            [
                new(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Retrait option", Money.FromCents(-100), 10m),
                new(Guid.NewGuid(), 1, OrderAdjustmentKind.PredefinedOption, null, null, "Extra", Money.FromCents(250), 10m)
            ]
        };
        var order = original with
        {
            Items = [item],
            TotalTtc = Money.FromCents(999),
            ManualTotalOverrideActive = true
        };
        var document = new OrderPrintDocumentFactory(new FixedClock()).Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.InitialAutomatic);

        Assert.AreEqual("-0.90 EUR", document.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Discount).SecondaryText);
        Assert.AreEqual("9.99", document.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Total).SecondaryText);
        Assert.IsFalse(new OrderPrintDocumentFactory(new FixedClock()).Create(
            order with { PickupDiscountApplied = false, PickupDiscountRate = null }, ReceiptIdentity.Default,
            PrintDocumentKind.Customer, PrintIntent.InitialAutomatic).Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Discount));
    }

    [TestMethod]
    public void PickupDiscountUsesQuantityAndEligibleLinesOnlyWithoutCurrentSettings()
    {
        var original = CreateDiscountedOrder(OrderStatus.Open);
        var eligible = original.Items[0] with
        {
            Quantity = 2,
            ProductBasePriceTtc = Money.FromCents(1000),
            ExtendedBaseTtc = Money.FromCents(2000),
            CalculatedLineTotalTtc = Money.FromCents(2120),
            Adjustments =
            [
                new(Guid.NewGuid(), 0, OrderAdjustmentKind.CustomAdjustment, null, null, "Retrait option", Money.FromCents(-100), 10m),
                new(Guid.NewGuid(), 1, OrderAdjustmentKind.PredefinedOption, null, null, "Extra", Money.FromCents(250), 10m)
            ]
        };
        var ineligible = eligible with
        {
            Id = Guid.NewGuid(),
            Position = 1,
            ProductDiscountEligible = false,
            Quantity = 1,
            ProductBasePriceTtc = Money.FromCents(500),
            ExtendedBaseTtc = Money.FromCents(500),
            CalculatedLineTotalTtc = Money.FromCents(600),
            Adjustments = [new(Guid.NewGuid(), 0, OrderAdjustmentKind.PredefinedOption, null, null, "Extra", Money.FromCents(100), 10m)]
        };
        var order = original with { Items = [eligible, ineligible], TotalTtc = Money.FromCents(2720) };
        var factory = new OrderPrintDocumentFactory(new FixedClock());
        var defaultIdentity = factory.Create(order, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.InitialAutomatic);
        var changedIdentity = factory.Create(order, ReceiptIdentity.Default with { BusinessName = "Synthetic receipt identity" },
            PrintDocumentKind.Customer, PrintIntent.InitialAutomatic);

        // 2 x (10.00 - 1.00) discounted by 10%; the positive 2 x 2.50 and ineligible 6.00 do not reduce it.
        Assert.AreEqual("-1.80 EUR", defaultIdentity.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Discount).SecondaryText);
        Assert.AreEqual("-1.80 EUR", changedIdentity.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Discount).SecondaryText);
        Assert.AreEqual("27.20", defaultIdentity.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Total).SecondaryText);
    }

    [TestMethod]
    public void InconsistentAppliedPickupDiscountFailsGenerationInsteadOfPrintingAnInventedAmount()
    {
        var order = CreateDiscountedOrder(OrderStatus.Open);
        var factory = new OrderPrintDocumentFactory(new FixedClock());
        var invalid = new[]
        {
            order with { PickupDiscountRate = null },
            order with { PickupDiscountRate = 0m },
            order with { PickupDiscountRate = 1.01m },
            order with { Fulfilment = FulfilmentMode.Livraison },
            order with { Items = [order.Items[0] with { ProductDiscountEligible = false }] },
            order with { Items = [order.Items[0] with { ExtendedBaseTtc = Money.FromCents(2629) }] },
            order with { Items = [order.Items[0] with { CalculatedLineTotalTtc = Money.FromCents(2630) }] },
            order with { Items = [order.Items[0] with { CalculatedLineTotalTtc = Money.FromCents(2400) }] }
        };
        foreach (var snapshot in invalid)
            Assert.ThrowsExactly<InvalidOperationException>(() => factory.Create(snapshot, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.InitialAutomatic));
    }

    [TestMethod]
    public void EveryKitchenVariantEndsInExactlyOneSemanticHandwritingSpace()
    {
        foreach (var status in new[] { OrderStatus.Open, OrderStatus.Cancelled })
        foreach (var intent in new[] { PrintIntent.InitialAutomatic, PrintIntent.InitialRetry, PrintIntent.ExplicitReprint })
        foreach (var profile in new[] { DeploymentProfile.Production, DeploymentProfile.PreProduction })
        {
            var document = new OrderPrintDocumentFactory(new FixedClock(), profile).Create(
                CreateOrder(BusinessDate, status), ReceiptIdentity.Default, PrintDocumentKind.Kitchen, intent);
            var blocks = document.Content.Blocks;
            Assert.AreEqual(PrintReceiptBlockKind.Total, blocks[^2].Kind);
            Assert.AreEqual(PrintReceiptBlockKind.HandwritingSpace, blocks[^1].Kind);
            Assert.AreEqual(1, blocks.Count(block => block.Kind == PrintReceiptBlockKind.HandwritingSpace));
            Assert.AreEqual(string.Empty, blocks[^1].Text);
            Assert.IsNull(blocks[^1].SecondaryText);
            Assert.AreEqual(blocks[^2].AtomicGroup, blocks[^1].AtomicGroup);
            Assert.AreEqual(new PrintReceiptContent(blocks.Take(blocks.Count - 1).ToArray()).ToDiagnosticText(), document.Text);
            Assert.IsFalse(document.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal));
        }
    }

    private static OrderSnapshot CreateDiscountedOrder(OrderStatus status)
    {
        var order = CreateOrder(BusinessDate, status);
        return order with
        {
            Fulfilment = FulfilmentMode.Retrait,
            PickupDiscountApplied = true,
            PickupDiscountRate = 0.10m,
            TotalTtc = Money.FromCents(2367),
            Items = [order.Items[0] with
            {
                ProductBasePriceTtc = Money.FromCents(2630),
                ExtendedBaseTtc = Money.FromCents(2630),
                CalculatedLineTotalTtc = Money.FromCents(2367),
                Adjustments = []
            }],
            TaxBreakdown = [new(10m, Money.FromCents(2367), Money.FromCents(215))]
        };
    }

    [TestMethod]
    public void CustomerItemRowsUseQuantityAwareBasePricingWithoutCurrencySuffixes()
    {
        var factory = new OrderPrintDocumentFactory(new FixedClock());
        var quantityOne = factory.Create(CreateOrder(BusinessDate, OrderStatus.Open), ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.InitialAutomatic);
        var quantityOneItem = quantityOne.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Item);
        var quantityOneOption = quantityOne.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Option);

        Assert.IsNotNull(quantityOneItem.Item);
        Assert.AreEqual("10.00", quantityOneItem.Item.UnitPriceText);
        Assert.AreEqual(string.Empty, quantityOneItem.Item.LineTotalText);
        Assert.AreEqual("2.50", quantityOneOption.SecondaryText);
        Assert.IsNotNull(quantityOneOption.Option);
        Assert.AreEqual("2.50", quantityOneOption.Option.AmountText);
        Assert.IsFalse(quantityOneOption.Option.AmountText.Contains("EUR", StringComparison.Ordinal));

        var quantityTwoOrder = CreateOrder(BusinessDate, OrderStatus.Open) with
        {
            TotalTtc = Money.FromCents(2500),
            Items = [CreateOrder(BusinessDate, OrderStatus.Open).Items[0] with
            {
                Quantity = 2,
                ExtendedBaseTtc = Money.FromCents(2000),
                CalculatedLineTotalTtc = Money.FromCents(2500)
            }]
        };
        var quantityTwo = factory.Create(quantityTwoOrder, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.InitialAutomatic);
        var quantityTwoItem = quantityTwo.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Item);

        Assert.IsNotNull(quantityTwoItem.Item);
        Assert.AreEqual("10.00", quantityTwoItem.Item.UnitPriceText);
        Assert.AreEqual("20.00", quantityTwoItem.Item.LineTotalText);
        Assert.AreNotEqual("25.00", quantityTwoItem.Item.LineTotalText);
        Assert.IsFalse(quantityTwoItem.Item.UnitPriceText.Contains("EUR", StringComparison.Ordinal));
        Assert.IsFalse(quantityTwoItem.Item.LineTotalText.Contains("EUR", StringComparison.Ordinal));
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
    public async Task ArchivedReprintServiceUsesTheSuppliedHydratedSnapshotWithoutReloadingLiveState()
    {
        var archived = CreateOrder(BusinessDate, OrderStatus.Closed) with { Comment = "Archived-only historical snapshot" };
        var dispatcher = new RecordingOutcomeDispatcher();
        var service = new ArchivedOrderPrintApplicationService(dispatcher);

        var result = await service.ReprintAsync(archived, PrintDocumentKind.Kitchen);

        Assert.IsTrue(result.Succeeded);
        Assert.AreSame(archived, dispatcher.LastOrder);
        Assert.AreEqual(PrintIntent.ExplicitReprint, dispatcher.LastIntent);
        Assert.AreEqual(PrintDocumentKind.Kitchen, dispatcher.LastKind);
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
