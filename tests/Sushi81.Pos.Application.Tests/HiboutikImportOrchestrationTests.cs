using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
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
        var product = Product(Guid.NewGuid(), "AA1", Money.FromCents(1000));
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
        Assert.AreEqual(900L, result.CommittedOrder.TotalTtc.Cents);
        Assert.AreEqual(1, store.SaveCalls);
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

    private static OrderEntryService CreateOrderService(IOrderEntryCatalogueQueries catalogue, IOrderStore store, IWriteAuthorityGuard guard) =>
        new(catalogue, new FakeSettingsStore(), store, new RecordingDispatcher(), new DeterministicIds(), new FixedClock(), guard, new RecordingNotifier());

    private static NewOrderDraft Draft(ProductAggregate product) =>
        new([OrderLineDraft.Create(product) with { CategoryName = "Plats" }], FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), null, null, null, false);

    private static ProductAggregate Product(Guid id, string code, Money price) =>
        ProductAggregate.Empty(new Product(id, code, code, Guid.NewGuid(), price, 10m, true, true, false, default, default));

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

    private static OrderEntryProduct Entry(ProductAggregate product) => new(product, "Plats");

    private sealed class FakeSettingsStore : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UtcNow) with { PickupDiscountRate = 0.1m, PickupDiscountMinTotalTtc = Money.Zero });
        public Task<OperationResult> UpdateAsync(BusinessSettings updated, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class RecordingOrderStore : IOrderStore
    {
        public int SaveCalls { get; private set; }
        private OrderSnapshot? snapshot;
        public Task SaveAsync(OrderSnapshot value, CancellationToken cancellationToken = default) { SaveCalls++; snapshot = value; return Task.CompletedTask; }
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);
    }

    private sealed class RecordingDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
