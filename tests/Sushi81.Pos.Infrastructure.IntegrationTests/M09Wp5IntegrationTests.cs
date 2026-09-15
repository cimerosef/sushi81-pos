using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M09Wp5IntegrationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 15);

    [TestMethod]
    public async Task HiboutikCreationCrossesSqliteLifecycleAndPrintsAuthoritativeTotal()
    {
        using var fixture = await TestFixture.CreateAsync();
        var product = await fixture.CreateProductAsync("HIB-WP5-LIFECYCLE", Money.FromCents(3590));
        using var entry = fixture.CreateEntryService();
        var sourceText = "1 x HIB-WP5-LIFECYCLE WP5-RAW-BLOCK-MUST-NOT-PERSIST (99.99)\nTOTAL 35.90";
        var session = await fixture.Importer.StartImportAsync(sourceText);

        Assert.IsTrue(session.CanConfirm);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders;"));
        var draft = session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0),
            telephone: "0612345678", comment: "WP5 lifecycle", pickupDiscountRequested: true).Value!;

        var confirmed = await entry.ConfirmHiboutikImportAsync(session, draft);

        Assert.IsTrue(confirmed.Succeeded, string.Join("; ", confirmed.Issues.Select(issue => issue.Message)));
        Assert.IsTrue(confirmed.OutputSucceeded);
        Assert.IsTrue(fixture.Dispatcher.SawCommittedOrderDuringInitialDispatch);
        Assert.AreEqual(OrderSourceType.HiboutikPaste, confirmed.CommittedOrder!.SourceType);
        Assert.AreEqual(Money.FromCents(3590), confirmed.CommittedOrder.SourceTotalTtc);
        Assert.AreEqual(Money.FromCents(3231), confirmed.CommittedOrder.TotalTtc);

        var overrideSession = await fixture.Importer.StartImportAsync(sourceText);
        var overridden = await entry.ConfirmHiboutikImportAsync(
            overrideSession,
            overrideSession.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 5),
                pickupDiscountRequested: true, manualTotalOverride: Money.FromCents(4000)).Value!);
        Assert.IsTrue(overridden.Succeeded, string.Join("; ", overridden.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(Money.FromCents(4000), overridden.CommittedOrder!.TotalTtc);
        Assert.IsTrue(overridden.CommittedOrder.ManualTotalOverrideActive);
        Assert.AreEqual(Money.FromCents(3590), overridden.CommittedOrder.SourceTotalTtc);

        using var lifecycle = fixture.CreateLifecycleService();
        var paid = await lifecycle.SaveModificationAsync(
            confirmed.CommittedOrder with { Comment = "WP5 lifecycle edited", CardPaymentTtc = confirmed.CommittedOrder.TotalTtc },
            BusinessDate);
        Assert.IsTrue(paid.Succeeded, string.Join("; ", paid.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(Money.FromCents(3590), paid.Snapshot!.SourceTotalTtc);

        var closed = await lifecycle.CloseAsync(confirmed.CommittedOrder.Id);
        Assert.IsTrue(closed.Succeeded, string.Join("; ", closed.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(OrderStatus.Closed, closed.Snapshot!.Status);
        Assert.AreEqual(Money.FromCents(3590), closed.Snapshot.SourceTotalTtc);
        Assert.AreEqual(0L, await fixture.ScalarAsync("""
            SELECT COUNT(*) FROM (
                SELECT comment AS value FROM orders
                UNION ALL SELECT delivery_address FROM orders
                UNION ALL SELECT product_code_snapshot FROM order_items
                UNION ALL SELECT product_name_snapshot FROM order_items
                UNION ALL SELECT category_name_snapshot FROM order_items
                UNION ALL SELECT label_snapshot FROM order_item_adjustments
            ) WHERE value LIKE '%WP5-RAW-BLOCK-MUST-NOT-PERSIST%';
            """));

        var receipt = new OrderPrintDocumentFactory(fixture.Clock).Create(
            closed.Snapshot, ReceiptIdentity.Default, PrintDocumentKind.Customer, PrintIntent.ExplicitReprint);
        var total = receipt.Content.Blocks.Single(block => block.Kind == PrintReceiptBlockKind.Total);
        Assert.AreEqual("32.31", total.SecondaryText);
        Assert.IsTrue(receipt.Content.Blocks.Any(block => block.Item?.UnitPriceText == "35.90"), "The item base price remains visible, but not as the selling total.");
        Assert.IsFalse(receipt.Content.Blocks.Any(block => block.Kind == PrintReceiptBlockKind.Total && block.SecondaryText == "35.90"));

        var reprint = await new OrderPrintApplicationService(fixture.OrderStore, fixture.Dispatcher)
            .ReprintAsync(confirmed.CommittedOrder.Id, PrintDocumentKind.Customer);
        Assert.IsTrue(reprint.Succeeded, reprint.OperatorMessage);
        Assert.AreEqual(PrintIntent.ExplicitReprint, fixture.Dispatcher.LastPrintIntent);
        Assert.AreEqual("WP5 lifecycle edited", fixture.Dispatcher.LastPrintedOrder!.Comment);
    }

    [TestMethod]
    public async Task HiboutikPersistenceFailureRollsBackParentChildrenAndTaxRows()
    {
        using var fixture = await TestFixture.CreateAsync(stage => stage == "item" ? new InvalidOperationException("WP5 injected item failure") : null);
        await fixture.CreateProductAsync("HIB-WP5-FAILURE", Money.FromCents(3590));
        using var entry = fixture.CreateEntryService();
        var session = await fixture.Importer.StartImportAsync("1 x HIB-WP5-FAILURE Failure source (35.90)\nTOTAL 35.90");
        var draft = session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), pickupDiscountRequested: true).Value!;

        var result = await entry.ConfirmHiboutikImportAsync(session, draft);

        Assert.IsFalse(result.PersistenceSucceeded);
        Assert.IsNull(result.CommittedOrder);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM order_items;"));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM order_tax_breakdown;"));
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM payment_adjustments;"));
    }

    [TestMethod]
    public async Task NonAuthoritativeHiboutikConfirmationWritesNothingAndMalformedPasteStaysTransient()
    {
        using var fixture = await TestFixture.CreateAsync();
        await fixture.CreateProductAsync("HIB-WP5-AUTHORITY", Money.FromCents(3590));
        fixture.Guard.State = WriteAuthorityState.NonAuthoritativeReadOnly;
        using var entry = fixture.CreateEntryService();
        var session = await fixture.Importer.StartImportAsync("1 x HIB-WP5-AUTHORITY Source (35.90)\nTOTAL 35.90");

        var blocked = await entry.ConfirmHiboutikImportAsync(
            session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0)).Value!);
        var malformed = await fixture.Importer.StartImportAsync("WP5 unsupported material");

        Assert.IsFalse(blocked.Succeeded);
        Assert.AreEqual(ValidationCodes.AuthorityBlocked, blocked.Issues.Single().StableCode);
        Assert.IsFalse(malformed.CanConfirm);
        Assert.AreEqual(0L, await fixture.ScalarAsync("SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task HiboutikIsExcludedFromPosReportingButRemainsSearchableAndPaid()
    {
        using var fixture = await TestFixture.CreateAsync();
        await fixture.CreateProductAsync("HIB-WP5-REPORTING", Money.FromCents(3590));
        using var entry = fixture.CreateEntryService();
        var session = await fixture.Importer.StartImportAsync("1 x HIB-WP5-REPORTING Report source (35.90)\nTOTAL 35.90");
        var hiboutik = (await entry.ConfirmHiboutikImportAsync(
            session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(12, 0),
                comment: "WP5-HIB-SEARCH", pickupDiscountRequested: true).Value!)).CommittedOrder!;

        using var lifecycle = fixture.CreateLifecycleService();
        var paidHiboutik = await lifecycle.SaveModificationAsync(hiboutik with { CardPaymentTtc = hiboutik.TotalTtc }, BusinessDate);
        Assert.IsTrue(paidHiboutik.Succeeded, string.Join("; ", paidHiboutik.Issues.Select(issue => issue.Message)));

        var ordinary = (await entry.ConfirmNewOrderAsync(new NewOrderDraft(
            [OrderLineDraft.Create((await fixture.GetProductAsync("HIB-WP5-REPORTING")).Aggregate)],
            FulfilmentMode.Retrait, BusinessDate, new TimeOnly(13, 0), null, null, "WP5-POS", false))).CommittedOrder!;
        var paidOrdinary = await lifecycle.SaveModificationAsync(ordinary with { CardPaymentTtc = ordinary.TotalTtc }, BusinessDate);
        Assert.IsTrue(paidOrdinary.Succeeded, string.Join("; ", paidOrdinary.Issues.Select(issue => issue.Message)));

        var summary = await lifecycle.GetOperationalSummaryAsync(BusinessDate);
        var searched = await lifecycle.SearchLiveAsync("WP5-HIB-SEARCH");
        var planned = await lifecycle.BrowseByPlannedDateAsync(BusinessDate);

        Assert.AreEqual(ordinary.TotalTtc, summary.TurnoverTtc);
        Assert.AreEqual(ordinary.TotalTtc, summary.ReceivedTtc);
        Assert.AreEqual(ordinary.TotalTtc, summary.ReceivedCardTtc);
        Assert.AreEqual(Money.Zero, summary.ReceivedCashTtc);
        Assert.AreEqual(hiboutik.Id, searched.Single().Id);
        Assert.IsTrue(planned.Any(row => row.Id == hiboutik.Id));
        Assert.AreEqual(OrderSourceType.HiboutikPaste, planned.Single(row => row.Id == hiboutik.Id).SourceType);
    }

    [TestMethod]
    public async Task InitialPrintFailureLeavesCommittedOrderRetryableAndLatestReprintUsesCurrentState()
    {
        using var fixture = await TestFixture.CreateAsync();
        await fixture.CreateProductAsync("HIB-WP5-PRINT", Money.FromCents(3590));
        using var entry = fixture.CreateEntryService();
        fixture.Dispatcher.FailInitial = true;
        var session = await fixture.Importer.StartImportAsync("1 x HIB-WP5-PRINT Print source (35.90)\nTOTAL 35.90");
        var confirmed = await entry.ConfirmHiboutikImportAsync(
            session, session.TryCreateDraft(FulfilmentMode.Retrait, BusinessDate, new TimeOnly(14, 0), pickupDiscountRequested: true).Value!);

        Assert.IsTrue(confirmed.PersistenceSucceeded);
        Assert.IsFalse(confirmed.DispatchSucceeded);
        Assert.IsNotNull(await fixture.OrderStore.GetByIdAsync(confirmed.PersistedOrderId!.Value));

        fixture.Dispatcher.FailInitial = false;
        var retry = await entry.RetryInitialPrintAsync(confirmed.PersistedOrderId.Value, PrintDocumentKind.Kitchen);
        Assert.IsTrue(retry.Succeeded, retry.OperatorMessage);
        Assert.AreEqual(PrintIntent.InitialRetry, fixture.Dispatcher.LastPrintIntent);

        using var lifecycle = fixture.CreateLifecycleService();
        var edited = await lifecycle.SaveModificationAsync(
            (await fixture.OrderStore.GetByIdAsync(confirmed.PersistedOrderId.Value))! with { Comment = "WP5 newest committed state" });
        Assert.IsTrue(edited.Succeeded, string.Join("; ", edited.Issues.Select(issue => issue.Message)));
        var reprint = await new OrderPrintApplicationService(fixture.OrderStore, fixture.Dispatcher)
            .ReprintAsync(confirmed.PersistedOrderId.Value, PrintDocumentKind.Kitchen);

        Assert.IsTrue(reprint.Succeeded, reprint.OperatorMessage);
        Assert.AreEqual(PrintIntent.ExplicitReprint, fixture.Dispatcher.LastPrintIntent);
        Assert.AreEqual("WP5 newest committed state", fixture.Dispatcher.LastPrintedOrder!.Comment);
        Assert.AreEqual(Money.FromCents(3231), fixture.Dispatcher.LastPrintedOrder.TotalTtc);
    }

    private sealed class TestFixture : IDisposable
    {
        private TestFixture(TestPaths paths, SqliteConnectionFactory factory, FixedClock clock, DeterministicIds ids,
            SqliteCatalogueStore catalogueStore, SqliteOrderStore orderStore, TestSettingsStore settings,
            TestWriteAuthorityGuard guard, RecordingNotifier notifier, RecordingOutputDispatcher dispatcher)
        {
            Paths = paths;
            Factory = factory;
            Clock = clock;
            Ids = ids;
            CatalogueStore = catalogueStore;
            OrderStore = orderStore;
            Settings = settings;
            Guard = guard;
            Notifier = notifier;
            Dispatcher = dispatcher;
            Catalogue = new OrderEntryCatalogueService(catalogueStore);
            Importer = new HiboutikImportOrchestrator(Catalogue, settings);
        }

        public TestPaths Paths { get; }
        public SqliteConnectionFactory Factory { get; }
        public FixedClock Clock { get; }
        public DeterministicIds Ids { get; }
        public SqliteCatalogueStore CatalogueStore { get; }
        public OrderEntryCatalogueService Catalogue { get; }
        public SqliteOrderStore OrderStore { get; }
        public TestSettingsStore Settings { get; }
        public TestWriteAuthorityGuard Guard { get; }
        public RecordingNotifier Notifier { get; }
        public RecordingOutputDispatcher Dispatcher { get; }
        public HiboutikImportOrchestrator Importer { get; }

        public static async Task<TestFixture> CreateAsync(Func<string, Exception?>? failureInjector = null)
        {
            var paths = new TestPaths();
            var clock = new FixedClock();
            var factory = new SqliteConnectionFactory(paths);
            await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
            var ids = new DeterministicIds();
            var catalogue = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock);
            var guard = new TestWriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var notifier = new RecordingNotifier();
            var settings = new TestSettingsStore(BusinessSettings.Defaults(clock.UtcNow) with
            {
                PickupDiscountMinTotalTtc = Money.Zero,
                DeliveryMinMerchandiseTotalTtc = Money.Zero
            });
            var orderStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), failureInjector, ids, clock);
            var dispatcher = new RecordingOutputDispatcher(orderStore, clock);
            return new TestFixture(paths, factory, clock, ids, catalogue, orderStore, settings, guard, notifier, dispatcher);
        }

        public async Task<OrderEntryProduct> CreateProductAsync(string code, Money price)
        {
            var category = (await CatalogueStore.CreateCategoryAsync("Plats WP5")).Value!;
            var id = (await CatalogueStore.CreateProductAsync(new ProductDraft(
                Guid.Empty, code, "Produit WP5", category.Id, price, 10m, true, true, false, []))).Value;
            return (await Catalogue.GetActiveProductAsync(id))!;
        }

        public Task<OrderEntryProduct> GetProductAsync(string code) => GetProductCoreAsync(code);

        private async Task<OrderEntryProduct> GetProductCoreAsync(string code) =>
            (await Catalogue.GetActiveProductByCodeAsync(code))!;

        public OrderEntryService CreateEntryService() =>
            new(Catalogue, Settings, OrderStore, Dispatcher, Ids, Clock, Guard, Notifier);

        public OrderLifecycleService CreateLifecycleService() =>
            new(OrderStore, Ids, Clock, Guard, Notifier, Catalogue, Settings);

        public async Task<long> ScalarAsync(string sql)
        {
            await using var connection = await Factory.OpenLiveConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        public void Dispose() => Paths.Dispose();
    }

    private sealed class TestSettingsStore(BusinessSettings current) : IBusinessSettingsStore
    {
        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(current);
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
    }

    private sealed class TestWriteAuthorityGuard(WriteAuthorityState initialState) : IWriteAuthorityGuard
    {
        public WriteAuthorityState State { get; set; } = initialState;
        public void RequireWriteAuthority()
        {
            if (State != WriteAuthorityState.Authoritative) throw new WriteAuthorityException(State);
        }
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }

    private sealed class RecordingOutputDispatcher(SqliteOrderStore orders, IBusinessClock clock) : IOrderPrintOutcomeDispatcher
    {
        private readonly OrderPrintDocumentFactory factory = new(clock);
        public bool FailInitial { get; set; }
        public bool SawCommittedOrderDuringInitialDispatch { get; private set; }
        public OrderSnapshot? LastPrintedOrder { get; private set; }
        public PrintIntent? LastPrintIntent { get; private set; }

        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<PrintDispatchResult> DispatchInitialAsync(OrderSnapshot committedOrder, PrintIntent intent = PrintIntent.InitialAutomatic, CancellationToken cancellationToken = default)
        {
            SawCommittedOrderDuringInitialDispatch = await orders.GetByIdAsync(committedOrder.Id, cancellationToken) is not null;
            LastPrintedOrder = committedOrder;
            LastPrintIntent = intent;
            var kitchen = factory.Create(committedOrder, ReceiptIdentity.Default, PrintDocumentKind.Kitchen, intent);
            var customer = factory.Create(committedOrder, ReceiptIdentity.Default, PrintDocumentKind.Customer, intent);
            if (FailInitial)
                return PrintDispatchResult.From(new(PrintDocumentKind.Kitchen, PrintOutcomeStatus.SubmissionFailed, "WP5 injected initial print failure", kitchen), PrintDocumentResult.Success(customer));
            return PrintDispatchResult.From(PrintDocumentResult.Success(kitchen), PrintDocumentResult.Success(customer));
        }

        public Task<PrintDocumentResult> PrintDocumentAsync(OrderSnapshot committedOrder, PrintDocumentKind kind, PrintIntent intent = PrintIntent.ExplicitReprint, CancellationToken cancellationToken = default)
        {
            LastPrintedOrder = committedOrder;
            LastPrintIntent = intent;
            return Task.FromResult(PrintDocumentResult.Success(factory.Create(committedOrder, ReceiptIdentity.Default, kind, intent)));
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => M09Wp5IntegrationTests.BusinessDate;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"70000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M09.WP5.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path);
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath => Path.Combine(DataDirectory, "live.db");
        public void EnsureInitialized() { }
        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true);
        }
    }
}
