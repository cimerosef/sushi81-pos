using System.Text.Json;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class HiboutikImportOrchestrationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 31);

    [TestMethod]
    public async Task StartImportUsesExactCodeResolutionAndPreservesOrderedEvidence()
    {
        var first = Product(Guid.NewGuid(), "AA1", Money.FromCents(1000));
        var catalogue = new FakeCatalogue(first);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());

        var session = await orchestrator.StartImportAsync("1 x AA1 Source name (99.99)\n2 x UNKNOWN Other (8.00)\nTOTAL 115.99");

        Assert.AreEqual(11599L, session.SourceTotalTtc!.Value.Cents);
        Assert.HasCount(3, session.Lines);
        Assert.AreEqual(HiboutikImportLineResolution.Resolved, session.Lines[0].Resolution);
        Assert.AreEqual(HiboutikImportLineResolution.Unresolved, session.Lines[1].Resolution);
        Assert.AreEqual(HiboutikImportLineResolution.KnownIgnored, session.Lines[2].Resolution);
        Assert.AreEqual(1, session.Lines[0].Quantity);
        Assert.AreEqual(2, session.Lines[1].Quantity);
        Assert.AreEqual(first.Product.Id, session.Lines[0].Product!.Aggregate.Product.Id);
        Assert.AreEqual(2, catalogue.ExactCodeCalls);
        Assert.AreEqual(0, catalogue.BroadListCalls);
        Assert.IsFalse(session.CanConfirm);
    }

    [TestMethod]
    public async Task KnownIgnoredDeliveryAndFinalTotalNeverBecomeOrderLines()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(3590));
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());

        var session = await orchestrator.StartImportAsync("1 x Livraison (0)\n1 x AA1 Source label (35.90)\nTOTAL 35.90");

        Assert.AreEqual(HiboutikImportLineResolution.KnownIgnored, session.Lines[0].Resolution);
        Assert.AreEqual(HiboutikImportLineResolution.Resolved, session.Lines[1].Resolution);
        Assert.AreEqual(HiboutikImportLineResolution.KnownIgnored, session.Lines[2].Resolution);
        Assert.IsTrue(session.CanConfirm);
        var lines = session.MaterializeOrderLines();
        Assert.HasCount(1, lines);
        Assert.AreEqual(product.Product.Id, lines.Single().Product.Product.Id);
        Assert.AreEqual(3590L, session.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public async Task RepeatedProductRowsRemainSeparateAndOrdered()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(1000));
        var catalogue = new FakeCatalogue(product);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());

        var session = await orchestrator.StartImportAsync("1 x AA1 First source (1.00)\n2 x AA1 Second source (2.00)");

        Assert.HasCount(2, session.Lines);
        Assert.AreEqual(1, session.Lines[0].SourceLineNumber);
        Assert.AreEqual(2, session.Lines[1].SourceLineNumber);
        Assert.AreEqual("1 x AA1 First source (1.00)", session.Lines[0].SourceText);
        Assert.AreEqual("2 x AA1 Second source (2.00)", session.Lines[1].SourceText);
        var lines = session.MaterializeOrderLines();
        Assert.HasCount(2, lines);
        Assert.AreEqual(1, lines[0].Quantity);
        Assert.AreEqual(2, lines[1].Quantity);
        Assert.AreEqual(product.Product.Id, lines[0].Product.Product.Id);
        Assert.AreEqual(product.Product.Id, lines[1].Product.Product.Id);
        Assert.AreEqual(2, catalogue.ExactCodeCalls);
    }

    [TestMethod]
    public async Task UnknownCodeNeverFallsBackToSourceNameGuess()
    {
        var catalogue = new FakeCatalogue(Product(Guid.NewGuid(), "CAT-1", Money.FromCents(1000)));
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());

        var session = await orchestrator.StartImportAsync("1 x UNKNOWN Sushi saumon (5.00)");

        Assert.IsTrue(session.Lines.Single().IsUnresolved);
        Assert.IsNull(session.Lines.Single().Product);
        Assert.AreEqual("UNKNOWN", session.Lines.Single().CandidateCode);
    }

    [TestMethod]
    public async Task ManualResolutionRefetchesCurrentProductAndPreservesReliableQuantity()
    {
        var product = Product(Guid.NewGuid(), "CURRENT", Money.FromCents(1250));
        var catalogue = new FakeCatalogue(product);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("3 x UNKNOWN Source label");

        var result = await orchestrator.ResolveUnresolvedLineAsync(session, 1, product.Product.Id);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual(HiboutikImportLineResolution.Resolved, result.Value!.Lines.Single().Resolution);
        Assert.AreEqual(3, result.Value.Lines.Single().Quantity);
        Assert.AreEqual(product.Product.Id, result.Value.Lines.Single().Product!.Aggregate.Product.Id);
        Assert.AreEqual(1, catalogue.GetByIdCalls);
    }

    [TestMethod]
    public async Task ManualResolutionRequiresPositiveQuantityWhenParserHasNoReliableQuantity()
    {
        var product = Product(Guid.NewGuid(), "CURRENT", Money.FromCents(1250));
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("unstructured source material");

        var result = await orchestrator.ResolveUnresolvedLineAsync(session, 1, product.Product.Id);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.Required, result.Issues.Single().StableCode);
    }

    [TestMethod]
    public async Task FailedManualSelectionLeavesTheUnresolvedStateUnchanged()
    {
        var product = Product(Guid.NewGuid(), "CURRENT", Money.FromCents(1250));
        var missingCatalogue = new FakeCatalogue(product);
        var missingOrchestrator = new HiboutikImportOrchestrator(missingCatalogue, new FakeSettingsStore());
        var missingSession = await missingOrchestrator.StartImportAsync("3 x UNKNOWN Source label");
        var originalMissingLine = missingSession.Lines.Single();

        var missing = await missingOrchestrator.ResolveUnresolvedLineAsync(missingSession, 1, Guid.NewGuid());

        Assert.IsFalse(missing.Succeeded);
        Assert.AreEqual(ValidationCodes.ProductMissing, missing.Issues.Single().StableCode);
        Assert.AreEqual(originalMissingLine, missingSession.Lines.Single());
        Assert.IsNull(missingSession.Lines.Single().Product);
        Assert.AreEqual(HiboutikImportLineResolution.Unresolved, missingSession.Lines.Single().Resolution);
        Assert.AreEqual(3, missingSession.Lines.Single().Quantity);

        var inactive = product with { Product = product.Product with { IsActive = false } };
        var inactiveOrchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(inactive), new FakeSettingsStore());
        var inactiveSession = await inactiveOrchestrator.StartImportAsync("3 x UNKNOWN Source label");
        var originalInactiveLine = inactiveSession.Lines.Single();

        var inactiveResult = await inactiveOrchestrator.ResolveUnresolvedLineAsync(inactiveSession, 1, product.Product.Id);

        Assert.IsFalse(inactiveResult.Succeeded);
        Assert.AreEqual(ValidationCodes.ProductMissing, inactiveResult.Issues.Single().StableCode);
        Assert.AreEqual(originalInactiveLine, inactiveSession.Lines.Single());
        Assert.IsNull(inactiveSession.Lines.Single().Product);
        Assert.AreEqual(HiboutikImportLineResolution.Unresolved, inactiveSession.Lines.Single().Resolution);
    }

    [TestMethod]
    public async Task ExplicitIgnoreClearsBlockerAndMaterializesOnlyResolvedProductLines()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(1000));
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x AA1 Product\noperator note");

        var result = HiboutikImportOrchestrator.IgnoreUnresolvedLine(session, 2);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.IsTrue(result.Value!.CanConfirm);
        var lines = result.Value.MaterializeOrderLines();
        Assert.HasCount(1, lines);
        Assert.AreEqual(product.Product.Id, lines.Single().Product.Product.Id);
        Assert.AreEqual(HiboutikImportLineResolution.ExplicitlyIgnored, result.Value.Lines[1].Resolution);
    }

    [TestMethod]
    public async Task OptionalProductAllowsExplicitEmptyOptionReview()
    {
        var product = ProductWithGroup(Guid.NewGuid(), "OPTIONAL", required: false);
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x OPTIONAL Product");

        Assert.IsFalse(session.CanConfirm);
        var result = await orchestrator.CompleteOptionReviewAsync(session, 1, []);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.IsTrue(result.Value!.CanConfirm);
        Assert.IsTrue(result.Value.Lines.Single().OptionReviewCompleted);
        Assert.IsEmpty(result.Value.Lines.Single().SelectedOptionIds);
    }

    [TestMethod]
    public async Task RequiredProductRejectsEmptyOptionReviewAndAcceptsValidSelection()
    {
        var optionId = Guid.NewGuid();
        var product = ProductWithGroup(Guid.NewGuid(), "REQUIRED", required: true, optionId);
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x REQUIRED Product");

        var rejected = await orchestrator.CompleteOptionReviewAsync(session, 1, []);
        var accepted = await orchestrator.CompleteOptionReviewAsync(session, 1, [optionId]);

        Assert.IsFalse(rejected.Succeeded);
        Assert.IsTrue(accepted.Succeeded, accepted.ErrorMessage);
        Assert.IsTrue(accepted.Value!.CanConfirm);
        CollectionAssert.AreEqual(new[] { optionId }, accepted.Value.Lines.Single().SelectedOptionIds.ToArray());
        var materialized = accepted.Value.MaterializeOrderLines().Single();
        Assert.AreEqual(product.Product.Id, materialized.Product.Product.Id);
        Assert.AreEqual("Plats", materialized.CategoryName);
        Assert.AreEqual(1, materialized.Quantity);
        CollectionAssert.AreEqual(new[] { optionId }, materialized.SelectedOptionIds.ToArray());
    }

    [TestMethod]
    public async Task OptionReviewUsesTheAcceptedQuantityForTheOrdinaryMaterializedLine()
    {
        var optionId = Guid.NewGuid();
        var product = ProductWithGroup(Guid.NewGuid(), "QUANTITY", required: true, optionId);
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x QUANTITY Product");

        var result = await orchestrator.CompleteOptionReviewAsync(session, 1, [optionId], [], quantity: 3);

        Assert.IsTrue(result.Succeeded, result.ErrorMessage);
        Assert.AreEqual(3, result.Value!.Lines.Single().Quantity);
        Assert.AreEqual(3, result.Value.MaterializeOrderLines().Single().Quantity);
    }

    [TestMethod]
    public async Task ProductWithoutOptionsIsReadyAndMaterializesCurrentCategoryAndQuantity()
    {
        var product = Product(Guid.NewGuid(), "NOOPT", Money.FromCents(1275));
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());

        var session = await orchestrator.StartImportAsync("2 x NOOPT Source label");

        Assert.IsTrue(session.CanConfirm);
        Assert.IsTrue(session.Lines.Single().OptionReviewCompleted);
        var materialized = session.MaterializeOrderLines().Single();
        Assert.AreEqual(product.Product.Id, materialized.Product.Product.Id);
        Assert.AreEqual("Plats", materialized.CategoryName);
        Assert.AreEqual(2, materialized.Quantity);
        Assert.IsEmpty(materialized.SelectedOptionIds);
    }

    [TestMethod]
    public async Task IncompleteSessionIsBlockedBeforeOrderWrite()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(1000));
        var store = new RecordingOrderStore();
        using var service = CreateOrderService(new FakeCatalogue(product), store, new TestAuthorityGuard(WriteAuthorityState.Authoritative));
        var orchestrator = new HiboutikImportOrchestrator(new FakeCatalogue(product), new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x UNKNOWN Product");
        var draft = Draft(product);

        var result = await service.ConfirmHiboutikImportAsync(session, draft);

        Assert.IsFalse(result.Succeeded);
        Assert.IsTrue(result.Issues.Any(issue => issue.StableCode == "import-unresolved"));
        Assert.AreEqual(0, store.SaveCalls);
    }

    [TestMethod]
    public async Task HiboutikConfirmationUsesCurrentPricingAndTransfersReferenceTotalOnly()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(3590));
        var catalogue = new FakeCatalogue(product);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x AA1 Source price (35.90)\nTOTAL 35.90");
        var store = new RecordingOrderStore();
        using var service = CreateOrderService(catalogue, store, new TestAuthorityGuard(WriteAuthorityState.Authoritative));
        var draft = session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), pickupDiscountRequested: true).Value!;

        var result = await service.ConfirmHiboutikImportAsync(session, draft);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(OrderSourceType.HiboutikPaste, result.CommittedOrder!.SourceType);
        Assert.AreEqual(3590L, result.CommittedOrder.SourceTotalTtc!.Value.Cents);
        Assert.AreEqual(3231L, result.CommittedOrder.TotalTtc.Cents);
        Assert.AreEqual(3590L, result.CommittedOrder.Items.Single().ProductBasePriceTtc.Cents);
        Assert.AreEqual(3231L, result.CommittedOrder.Items.Single().CalculatedLineTotalTtc.Cents);
        Assert.AreEqual("AA1", result.CommittedOrder.Items.Single().ProductCode);
        Assert.IsNull(result.CommittedOrder.Comment);
        var serializedSnapshot = JsonSerializer.Serialize(result.CommittedOrder);
        Assert.IsFalse(serializedSnapshot.Contains("Source price", StringComparison.Ordinal));
        Assert.AreEqual(1, store.SaveCalls);
    }

    [TestMethod]
    public async Task OrdinaryConfirmationRemainsPosOriginatedWithoutSourceTotal()
    {
        var product = Product(Guid.NewGuid(), "POS-1", Money.FromCents(3590));
        var store = new RecordingOrderStore();
        using var service = CreateOrderService(new FakeCatalogue(product), store, new TestAuthorityGuard(WriteAuthorityState.Authoritative));

        var result = await service.ConfirmNewOrderAsync(Draft(product));

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(OrderSourceType.Pos, result.CommittedOrder!.SourceType);
        Assert.IsNull(result.CommittedOrder.SourceTotalTtc);
        Assert.AreEqual(1, store.SaveCalls);
    }

    [TestMethod]
    public async Task HiboutikConfirmationWithoutReliableParserTotalPersistsNullSourceTotal()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(3590));
        var catalogue = new FakeCatalogue(product);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x AA1 Source label");
        var store = new RecordingOrderStore();
        using var service = CreateOrderService(catalogue, store, new TestAuthorityGuard(WriteAuthorityState.Authoritative));

        var result = await service.ConfirmHiboutikImportAsync(session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0)).Value!);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(OrderSourceType.HiboutikPaste, result.CommittedOrder!.SourceType);
        Assert.IsNull(result.CommittedOrder.SourceTotalTtc);
    }

    [TestMethod]
    public async Task FinalConfirmationRefetchesCurrentCatalogueAndKeepsSourceTotalSeparate()
    {
        var productId = Guid.NewGuid();
        var importedProduct = ProductNamed(productId, "AA1", "Earlier catalogue", Money.FromCents(1000));
        var currentProduct = ProductNamed(productId, "AA1", "Current catalogue", Money.FromCents(2000));
        var catalogue = new MutableCatalogue(importedProduct, currentProduct);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x AA1 Source label\nTOTAL 35.90");
        var store = new RecordingOrderStore();
        using var service = CreateOrderService(catalogue, store, new TestAuthorityGuard(WriteAuthorityState.Authoritative));

        var result = await service.ConfirmHiboutikImportAsync(session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0)).Value!);

        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(3590L, result.CommittedOrder!.SourceTotalTtc!.Value.Cents);
        Assert.AreEqual(2000L, result.CommittedOrder.TotalTtc.Cents);
        Assert.AreEqual("Current catalogue", result.CommittedOrder.Items.Single().ProductName);
        Assert.AreEqual(2000L, result.CommittedOrder.Items.Single().ProductBasePriceTtc.Cents);
        Assert.AreEqual(1, catalogue.GetByIdCalls);
    }

    [TestMethod]
    public async Task HiboutikConfirmationReloadsCommittedSnapshotBeforeInitialDispatch()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(3590));
        var catalogue = new FakeCatalogue(product);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x AA1 Source label\nTOTAL 35.90");
        var store = new RecordingOrderStore();
        var dispatcher = new RecordingOutcomeDispatcher();
        using var service = CreateOrderService(catalogue, store, new TestAuthorityGuard(WriteAuthorityState.Authoritative), dispatcher);

        var result = await service.ConfirmHiboutikImportAsync(session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0)).Value!);

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(result.OutputSucceeded);
        Assert.IsTrue(result.Output!.Succeeded);
        Assert.AreSame(result.CommittedOrder, dispatcher.LastOrder);
        Assert.AreSame(store.Snapshot, dispatcher.LastOrder);
        Assert.AreEqual(OrderSourceType.HiboutikPaste, dispatcher.LastOrder!.SourceType);
        Assert.AreEqual(3590L, dispatcher.LastOrder.SourceTotalTtc!.Value.Cents);
        Assert.AreEqual(1, dispatcher.InitialCalls);
    }

    [TestMethod]
    public async Task OutputFailureAfterSaveLeavesCommittedHiboutikOrderRetrievable()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(3590));
        var catalogue = new FakeCatalogue(product);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x AA1 Source label\nTOTAL 35.90");
        var store = new RecordingOrderStore();
        using var service = CreateOrderService(catalogue, store, new TestAuthorityGuard(WriteAuthorityState.Authoritative), new ThrowingOutcomeDispatcher());

        var result = await service.ConfirmHiboutikImportAsync(session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0)).Value!);
        var reloaded = await service.GetOrderByIdAsync(result.CommittedOrder!.Id);

        Assert.IsTrue(result.PersistenceSucceeded);
        Assert.IsFalse(result.DispatchSucceeded);
        Assert.IsTrue(result.HasOutputFailure);
        Assert.IsTrue(result.Issues.Any(issue => issue.Field == "output"));
        Assert.IsNotNull(reloaded);
        Assert.AreEqual(result.CommittedOrder.Id, reloaded!.Id);
        Assert.AreEqual(OrderSourceType.HiboutikPaste, reloaded.SourceType);
        Assert.AreEqual(3590L, reloaded.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public async Task HiboutikConfirmationRemainsBehindOrdinaryWriteAuthority()
    {
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(1000));
        var catalogue = new FakeCatalogue(product);
        var orchestrator = new HiboutikImportOrchestrator(catalogue, new FakeSettingsStore());
        var session = await orchestrator.StartImportAsync("1 x AA1 Product\nTOTAL 10");
        var store = new RecordingOrderStore();
        using var service = CreateOrderService(catalogue, store, new TestAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly));

        var result = await service.ConfirmHiboutikImportAsync(session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0)).Value!);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(ValidationCodes.AuthorityBlocked, result.Issues.Single().StableCode);
        Assert.AreEqual(0, store.SaveCalls);
    }

    private static OrderEntryService CreateOrderService(
        IOrderEntryCatalogueQueries catalogue,
        IOrderStore store,
        IWriteAuthorityGuard guard,
        IOrderPrintDispatcher? dispatcher = null) =>
        new(catalogue, new FakeSettingsStore(), store, dispatcher ?? new RecordingDispatcher(), new DeterministicIds(), new FixedClock(), guard, new RecordingNotifier());

    private static NewOrderDraft Draft(ProductAggregate product) =>
        new([OrderLineDraft.Create(product) with { CategoryName = "Plats" }], FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), null, null, null, false);

    private static ProductAggregate Product(Guid id, string code, Money price) => ProductNamed(id, code, code, price);

    private static ProductAggregate ProductNamed(Guid id, string code, string name, Money price) =>
        ProductAggregate.Empty(new Product(id, code, name, Guid.NewGuid(), price, 10m, true, true, false, default, default));

    private static ProductAggregate ProductWithGroup(Guid id, string code, bool required, Guid? optionId = null)
    {
        var groupId = Guid.NewGuid();
        var product = new Product(id, code, code, Guid.NewGuid(), Money.FromCents(1000), 10m, true, true, true, default, default);
        var group = new OptionGroup(groupId, id, "Choix", SelectionMode.Single, required, null, null, 0, default, default);
        var option = new ProductOption(optionId ?? Guid.NewGuid(), groupId, "Option", Money.FromCents(50), true, 0, default, default);
        return new ProductAggregate(product, [group], new Dictionary<Guid, IReadOnlyList<ProductOption>> { [groupId] = [option] });
    }

    private sealed class FakeCatalogue(ProductAggregate product) : IOrderEntryCatalogueQueries
    {
        public int ExactCodeCalls { get; private set; }
        public int BroadListCalls { get; private set; }
        public int GetByIdCalls { get; private set; }
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default)
        {
            BroadListCalls++;
            return Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        }
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            GetByIdCalls++;
            return Task.FromResult<OrderEntryProduct?>(productId == product.Product.Id ? Entry(product) : null);
        }
        public Task<OrderEntryProduct?> GetActiveProductByCodeAsync(string? productCode, CancellationToken cancellationToken = default)
        {
            ExactCodeCalls++;
            return Task.FromResult<OrderEntryProduct?>(string.Equals(productCode?.Trim(), product.Product.Code, StringComparison.OrdinalIgnoreCase) ? Entry(product) : null);
        }
    }

    private sealed class MutableCatalogue(ProductAggregate importedProduct, ProductAggregate currentProduct) : IOrderEntryCatalogueQueries
    {
        public ProductAggregate CurrentProduct { get; set; } = currentProduct;
        public int GetByIdCalls { get; private set; }
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default)
        {
            GetByIdCalls++;
            return Task.FromResult<OrderEntryProduct?>(productId == CurrentProduct.Product.Id && CurrentProduct.Product.IsActive ? Entry(CurrentProduct) : null);
        }
        public Task<OrderEntryProduct?> GetActiveProductByCodeAsync(string? productCode, CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderEntryProduct?>(
                importedProduct.Product.IsActive && string.Equals(productCode?.Trim(), importedProduct.Product.Code, StringComparison.OrdinalIgnoreCase)
                    ? Entry(importedProduct)
                    : null);
    }

    private static OrderEntryProduct Entry(ProductAggregate product) => new(product, "Plats");

    private sealed class FakeSettingsStore : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountRate = 0.1m, PickupDiscountMinTotalTtc = Money.Zero });
        public Task<OperationResult> UpdateAsync(BusinessSettings updated, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class RecordingOrderStore : IOrderStore
    {
        public int SaveCalls { get; private set; }
        public OrderSnapshot? Snapshot => snapshot;
        private OrderSnapshot? snapshot;
        public Task SaveAsync(OrderSnapshot value, CancellationToken cancellationToken = default) { SaveCalls++; snapshot = value; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
    }

    private sealed class RecordingDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingOutcomeDispatcher : IOrderPrintOutcomeDispatcher
    {
        public int InitialCalls { get; private set; }
        public OrderSnapshot? LastOrder { get; private set; }
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PrintDispatchResult> DispatchInitialAsync(OrderSnapshot committedOrder, PrintIntent intent = PrintIntent.InitialAutomatic, CancellationToken cancellationToken = default)
        {
            InitialCalls++;
            LastOrder = committedOrder;
            var kitchen = new OrderPrintDocument(PrintDocumentKind.Kitchen, intent, committedOrder.Id, committedOrder.Reference, "synthetic", false, false);
            var customer = kitchen with { Kind = PrintDocumentKind.Customer };
            return Task.FromResult(PrintDispatchResult.From(PrintDocumentResult.Success(kitchen), PrintDocumentResult.Success(customer)));
        }
        public Task<PrintDocumentResult> PrintDocumentAsync(OrderSnapshot committedOrder, PrintDocumentKind kind, PrintIntent intent = PrintIntent.ExplicitReprint, CancellationToken cancellationToken = default) =>
            Task.FromResult(PrintDocumentResult.Success(new(kind, intent, committedOrder.Id, committedOrder.Reference, "synthetic", false, false)));
    }

    private sealed class ThrowingOutcomeDispatcher : IOrderPrintOutcomeDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PrintDispatchResult> DispatchInitialAsync(OrderSnapshot committedOrder, PrintIntent intent = PrintIntent.InitialAutomatic, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic initial print failure");
        public Task<PrintDocumentResult> PrintDocumentAsync(OrderSnapshot committedOrder, PrintDocumentKind kind, PrintIntent intent = PrintIntent.ExplicitReprint, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic print failure");
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestAuthorityGuard : IWriteAuthorityGuard
    {
        private readonly WriteAuthorityState state;
        public TestAuthorityGuard(WriteAuthorityState state) => this.state = state;
        public WriteAuthorityState State => state;
        public void RequireWriteAuthority()
        {
            if (state != WriteAuthorityState.Authoritative) throw new WriteAuthorityException(state);
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => HiboutikImportOrchestrationTests.BusinessDate;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"00000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }
}
