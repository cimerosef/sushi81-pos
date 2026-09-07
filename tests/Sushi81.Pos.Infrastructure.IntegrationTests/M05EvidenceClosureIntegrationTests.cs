using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M05EvidenceClosureIntegrationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 8, 31);

    [TestMethod]
    public async Task PopulatedV4ToV5BackfillPreservesSnapshotsAndStableReferencesAcrossRestart()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await InitializeV4Async(factory, clock);

        var first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var third = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var fourth = Guid.Parse("00000000-0000-0000-0000-000000000004");
        var v4Orders = new[]
        {
            (first, new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, 2), "0611111111", "first comment"),
            (second, new DateTimeOffset(2026, 8, 30, 8, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, 3), "0622222222", "second comment"),
            (third, new DateTimeOffset(2026, 8, 30, 9, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, 4), "0633333333", "third comment"),
            (fourth, new DateTimeOffset(2026, 8, 31, 10, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, 5), "0644444444", "future planned date")
        };
        foreach (var row in v4Orders)
            await InsertV4OrderAsync(factory, row.Item1, row.Item2, row.Item3, row.Item4, row.Item5, row.Item1 == first);

        var before = await ReadV4EvidenceAsync(factory, v4Orders.Select(row => row.Item1));
        var snapshots = new RecordingSnapshotService();
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshots).InitializeAsync();

        Assert.AreEqual(5L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.HasCount(1, snapshots.Changes);
        foreach (var row in v4Orders)
        {
            var expectedSequence = row.Item1 == first ? "20260830-001" : row.Item1 == second ? "20260830-002" : row.Item1 == third ? "20260830-003" : "20260831-001";
            Assert.AreEqual(expectedSequence, await TextAsync(factory, "SELECT order_reference FROM orders WHERE order_id=$id;", row.Item1.ToString()));
            Assert.AreEqual(0L, await ScalarAsync(factory, $"SELECT card_payment_ttc_cents + cash_payment_ttc_cents FROM orders WHERE order_id='{row.Item1}';"));
        }

        var after = await ReadV4EvidenceAsync(factory, v4Orders.Select(row => row.Item1));
        CollectionAssert.AreEqual(before, after);
        Assert.AreEqual(4L, await ScalarAsync(factory, "SELECT next_sequence FROM order_reference_sequences WHERE business_date='2026-08-30';"));
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT next_sequence FROM order_reference_sequences WHERE business_date='2026-08-31';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_items WHERE order_id='" + first + "';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_item_adjustments WHERE order_item_id='10000000-0000-0000-0000-000000000001';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM order_tax_breakdown WHERE order_id='" + first + "';"));

        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshots).InitializeAsync();
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);
        var continued = Snapshot(Guid.Parse("00000000-0000-0000-0000-000000000005"), new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, 6));
        await store.SaveAsync(continued);
        var reloaded = await store.GetByIdAsync(continued.Id);
        Assert.IsNotNull(reloaded);
        Assert.AreEqual("20260830-004", reloaded!.Reference);
        Assert.AreEqual("2026-09-05", await TextAsync(factory, "SELECT planned_fulfilment_date FROM orders WHERE order_id=$id;", fourth.ToString()));
    }

    [TestMethod]
    public async Task PopulatedV4FailedV5MigrationRollsBackAndRealRetryPreservesData()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await InitializeV4Async(factory, clock);
        var orderId = Guid.Parse("00000000-0000-0000-0000-000000000011");
        await InsertV4OrderAsync(factory, orderId, new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero), new DateOnly(2026, 9, 1), "0655555555", "retry me", false);

        var broken = M01Migrations.All.Concat(M03Migrations.All).Concat(M04Migrations.All).Concat([
            new SqliteMigration(5, "broken-populated-v5", M05Migrations.Migration.Sql, FailPostApplyAsync)]).ToArray();
        var snapshots = new RecordingSnapshotService();
        await Assert.ThrowsAsync<DatabaseMigrationException>(() => new SqliteMigrationRunner(factory, broken, clock, snapshots).InitializeAsync());

        Assert.AreEqual(4L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE order_id='" + orderId + "';"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM pragma_table_info('orders') WHERE name='order_reference';"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('payment_adjustments','order_reference_sequences');"));

        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshots).InitializeAsync();
        Assert.AreEqual(5L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual("20260831-001", await TextAsync(factory, "SELECT order_reference FROM orders WHERE order_id=$id;", orderId.ToString()));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE telephone='0655555555' AND comment='retry me';"));
    }

    [TestMethod]
    public async Task NewReferenceAndExistingSnapshotFailuresRollbackAtRealSqliteBoundary()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var first = Snapshot(Guid.Parse("10000000-0000-0000-0000-000000000001"), clock.UtcNow, BusinessDate);
        var second = Snapshot(Guid.Parse("10000000-0000-0000-0000-000000000002"), clock.UtcNow.AddMinutes(1), BusinessDate);
        var stableStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), clock: clock);
        await stableStore.SaveAsync(first);
        var failingNewStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), stage => stage == "order" ? new InvalidOperationException("new parent failure") : null, clock: clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingNewStore.SaveAsync(second));

        Assert.IsNull(await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(second.Id));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT next_sequence FROM order_reference_sequences WHERE business_date='2026-08-31';"));
        await stableStore.SaveAsync(second);
        Assert.AreEqual("20260831-002", (await stableStore.GetByIdAsync(second.Id))!.Reference);

        var before = await stableStore.GetByIdAsync(first.Id);
        Assert.IsNotNull(before);
        var changed = before! with
        {
            Telephone = "0699999999",
            Comment = "changed after parent update",
            Items = [before.Items[0] with
            {
                ProductName = "Changed snapshot",
                Quantity = 2,
                Adjustments = [before.Items[0].Adjustments[0] with { Label = "Changed adjustment" }]
            }],
            TaxBreakdown = [before.TaxBreakdown[0] with { TaxableTtc = Money.FromCents(2200) }]
        };
        var failingExistingStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), stage => stage == "order-after-parent" ? new InvalidOperationException("existing parent failure") : null, clock: clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failingExistingStore.SaveAsync(changed));
        var reloaded = await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(first.Id);
        AssertSnapshotEqual(before, reloaded);
    }

    [TestMethod]
    public async Task PaymentFailuresRollbackSingleAndTwoChannelLedgersAndSignedCorrectionIsExact()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var order = Snapshot(Guid.Parse("20000000-0000-0000-0000-000000000001"), clock.UtcNow, BusinessDate, total: 1000);
        var stableStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), clock: clock);
        await stableStore.SaveAsync(order);
        var before = await stableStore.GetByIdAsync(order.Id);

        var singleFail = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), stage => stage == "payment" ? new InvalidOperationException("single payment failure") : null, clock: clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => singleFail.SaveLifecycleAsync(order with { CardPaymentTtc = Money.FromCents(300) }, [Payment(order.Id, PaymentBucket.Card, 300, 1)]));
        AssertSnapshotEqual(before, await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(order.Id));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments;"));

        var betweenFail = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), stage => stage == "payment-after-1" ? new InvalidOperationException("between payment failure") : null, clock: clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => betweenFail.SaveLifecycleAsync(order with { CardPaymentTtc = Money.FromCents(300), CashPaymentTtc = Money.FromCents(200) }, [Payment(order.Id, PaymentBucket.Card, 300, 2), Payment(order.Id, PaymentBucket.Cash, 200, 3)]));
        AssertSnapshotEqual(before, await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(order.Id));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments;"));

        await stableStore.SaveLifecycleAsync(order with { CardPaymentTtc = Money.FromCents(500) }, [Payment(order.Id, PaymentBucket.Card, 500, 4)]);
        var corrected = order with { CardPaymentTtc = Money.FromCents(200) };
        await stableStore.SaveLifecycleAsync(corrected, [Payment(order.Id, PaymentBucket.Card, -300, 5)]);
        var afterCorrection = await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(order.Id);
        Assert.AreEqual(200L, afterCorrection!.CardPaymentTtc.Cents);
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments WHERE order_id='" + order.Id + "';"));
        Assert.AreEqual(-300L, await ScalarAsync(factory, "SELECT delta_cents FROM payment_adjustments WHERE delta_cents < 0;"));

        var dualOrder = Snapshot(Guid.Parse("20000000-0000-0000-0000-000000000002"), clock.UtcNow, BusinessDate, total: 1000);
        await stableStore.SaveAsync(dualOrder);
        using var lifecycle = new OrderLifecycleService(stableStore, new DeterministicIds(), clock);
        var dualCurrent = await stableStore.GetByIdAsync(dualOrder.Id);
        var dual = await lifecycle.SaveModificationAsync(dualCurrent! with { CardPaymentTtc = Money.FromCents(300), CashPaymentTtc = Money.FromCents(200) });
        Assert.IsTrue(dual.Succeeded, string.Join("; ", dual.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments WHERE order_id='" + dualOrder.Id + "';"));
        var noChange = await lifecycle.SaveModificationAsync(dual.Snapshot!);
        Assert.IsTrue(noChange.Succeeded);
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments WHERE order_id='" + dualOrder.Id + "';"));
    }

    [TestMethod]
    public async Task LifecycleCommitAndStateTransitionFailuresLeaveFreshReloadUnchanged()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var order = Snapshot(Guid.Parse("21000000-0000-0000-0000-000000000001"), clock.UtcNow, BusinessDate, total: 1000);
        var normalStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), clock: clock);
        await normalStore.SaveAsync(order);
        await normalStore.SaveLifecycleAsync(order with { CardPaymentTtc = Money.FromCents(1000) }, [Payment(order.Id, PaymentBucket.Card, 1000, 6)]);

        var closeFailStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory, () => new InvalidOperationException("close commit failure")), clock: clock);
        using var closeService = new OrderLifecycleService(closeFailStore, new DeterministicIds(), clock);
        var close = await closeService.CloseAsync(order.Id);
        Assert.IsFalse(close.Succeeded);
        Assert.AreEqual(OrderStatus.Open, (await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(order.Id))!.Status);

        var closed = await new OrderLifecycleService(normalStore, new DeterministicIds(), clock).CloseAsync(order.Id);
        Assert.IsTrue(closed.Succeeded);
        var closedBefore = await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(order.Id);
        var reopenFailStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), stage => stage == "order-after-parent" ? new InvalidOperationException("reopen parent failure") : null, clock: clock);
        using var reopenService = new OrderLifecycleService(reopenFailStore, new DeterministicIds(), clock);
        var reopen = await reopenService.SaveModificationAsync(closedBefore! with { TotalTtc = Money.FromCents(900) });
        Assert.IsFalse(reopen.Succeeded);
        AssertSnapshotEqual(closedBefore, await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(order.Id));

        var cancelFailStore = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory, () => new InvalidOperationException("cancel commit failure")), clock: clock);
        using var cancelService = new OrderLifecycleService(cancelFailStore, new DeterministicIds(), clock);
        var cancel = await cancelService.CancelAsync(order.Id);
        Assert.IsFalse(cancel.Succeeded);
        var afterCancelFailure = await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(order.Id);
        AssertSnapshotEqual(closedBefore, afterCancelFailure);
    }

    [TestMethod]
    public async Task PaymentValidationQueriesAndOperationalSummariesUseExactCentsAndSources()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), clock: clock);
        var today = Snapshot(Guid.Parse("22000000-0000-0000-0000-000000000001"), clock.UtcNow, BusinessDate, total: 1000, telephone: "0610101010", comment: "today comment");
        var future = Snapshot(Guid.Parse("22000000-0000-0000-0000-000000000002"), clock.UtcNow, BusinessDate.AddDays(2), total: 2000, telephone: "0620202020", comment: "future comment");
        var overdue = Snapshot(Guid.Parse("22000000-0000-0000-0000-000000000003"), clock.UtcNow, BusinessDate.AddDays(-1), total: 3000, telephone: "0630303030", comment: "overdue comment");
        var closed = Snapshot(Guid.Parse("22000000-0000-0000-0000-000000000004"), clock.UtcNow, BusinessDate.AddDays(-2), total: 4000, status: OrderStatus.Closed, telephone: "0640404040", comment: "closed settled");
        var cancelled = Snapshot(Guid.Parse("22000000-0000-0000-0000-000000000005"), clock.UtcNow, BusinessDate.AddDays(3), total: 5000, status: OrderStatus.Cancelled, telephone: "0650505050", comment: "cancelled searchable");
        var dueAdvance = Snapshot(Guid.Parse("22000000-0000-0000-0000-000000000007"), clock.UtcNow, BusinessDate, total: 7000, status: OrderStatus.Closed, telephone: "0670707070", comment: "sticky due advance") with { AdvanceOrderMarker = true };
        foreach (var snapshot in new[] { today, future, overdue, closed, cancelled, dueAdvance }) await store.SaveAsync(snapshot);
        await store.SaveLifecycleAsync(today with { CardPaymentTtc = Money.FromCents(400) }, [Payment(today.Id, PaymentBucket.Card, 400, 7)]);
        await store.SaveLifecycleAsync(future with { CashPaymentTtc = Money.FromCents(500) }, [Payment(future.Id, PaymentBucket.Cash, 500, 8)]);
        await store.SaveLifecycleAsync(overdue with { CardPaymentTtc = Money.FromCents(1000) }, [Payment(overdue.Id, PaymentBucket.Card, 1000, 9, BusinessDate.AddDays(-1))]);
        await store.SaveLifecycleAsync(closed with { CardPaymentTtc = Money.FromCents(4000) }, [Payment(closed.Id, PaymentBucket.Card, 4000, 10)]);
        await store.SaveLifecycleAsync(dueAdvance with { CardPaymentTtc = Money.FromCents(7000) }, [Payment(dueAdvance.Id, PaymentBucket.Card, 7000, 12)]);

        using var negative = new OrderLifecycleService(store, new DeterministicIds(), clock);
        var negativeResult = await negative.SaveModificationAsync(today with { CardPaymentTtc = Money.FromCents(-1) });
        Assert.IsFalse(negativeResult.Succeeded);
        Assert.AreEqual(400L, (await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(today.Id))!.CardPaymentTtc.Cents);
        var negativeCashResult = await negative.SaveModificationAsync(today with { CashPaymentTtc = Money.FromCents(-1) });
        Assert.IsFalse(negativeCashResult.Succeeded);
        Assert.AreEqual(0L, (await new SqliteOrderStore(factory, new SqliteTransactionRunner(factory)).GetByIdAsync(today.Id))!.CashPaymentTtc.Cents);
        var summary = await store.GetOperationalSummaryAsync(BusinessDate);
        Assert.AreEqual(8000L, summary.TurnoverTtc.Cents);
        Assert.AreEqual(11900L, summary.ReceivedTtc.Cents);
        Assert.AreEqual(11400L, summary.ReceivedCardTtc.Cents);
        Assert.AreEqual(500L, summary.ReceivedCashTtc.Cents);
        Assert.AreEqual(1, summary.FutureOrderCount);
        Assert.AreEqual(1, summary.DueTodayAdvanceOrderCount);
        Assert.AreEqual(1, summary.OverdueUnsettledOrderCount);

        var todayReference = await TextAsync(factory, "SELECT order_reference FROM orders WHERE order_id=$id;", today.Id.ToString());
        var cancelledReference = await TextAsync(factory, "SELECT order_reference FROM orders WHERE order_id=$id;", cancelled.Id.ToString());
        foreach (var term in new[] { todayReference, today.Telephone!, today.Comment!, cancelled.Telephone!, cancelled.Comment! })
            Assert.IsTrue((await store.SearchAsync(term)).Any(row => row.Id == today.Id || row.Id == cancelled.Id), term);
        Assert.IsTrue((await store.SearchAsync(todayReference[6..])).Any(row => row.Id == today.Id), "A reference substring must match.");
        Assert.IsTrue((await store.SearchAsync("0610")).Any(row => row.Id == today.Id), "A local-format telephone fragment must match.");
        Assert.IsTrue((await store.SearchAsync("06 10")).Any(row => row.Id == today.Id), "Telephone spacing must not affect matching.");
        Assert.IsTrue((await store.SearchAsync("6101")).Any(row => row.Id == today.Id), "A telephone fragment without the leading zero must match.");
        Assert.IsTrue((await store.SearchAsync("+33 6 10")).Any(row => row.Id == today.Id), "An international telephone fragment must match local storage.");
        var projectedToday = (await store.SearchAsync(todayReference)).Single(row => row.Id == today.Id);
        Assert.AreEqual(today.DeliveryAddress, projectedToday.DeliveryAddress);
        Assert.AreEqual(today.Comment, projectedToday.Comment);
        var datedToday = (await store.ListByPlannedDateAsync(BusinessDate)).Single(row => row.Id == today.Id);
        Assert.AreEqual(today.DeliveryAddress, datedToday.DeliveryAddress);
        Assert.AreEqual(today.Comment, datedToday.Comment);
        Assert.HasCount(1, await store.SearchAsync(cancelledReference));
        var literalComment = today with { Comment = "literal %_ marker" };
        await store.SaveLifecycleAsync(literalComment, []);
        var literalMatches = await store.SearchAsync("%_");
        Assert.HasCount(1, literalMatches);
        Assert.AreEqual(today.Id, literalMatches[0].Id, "LIKE metacharacters must be treated literally.");
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments WHERE effective_business_date='2026-08-31' AND order_id='" + overdue.Id + "';"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments WHERE effective_business_date='2026-08-30' AND order_id='" + overdue.Id + "';"));

        var hiboutik = Snapshot(Guid.Parse("22000000-0000-0000-0000-000000000006"), clock.UtcNow, BusinessDate, total: 6000, source: OrderSourceType.HiboutikPaste, telephone: "0660606060", comment: "Hiboutik source") with { Reference = "20260831-999" };
        await store.SaveAsync(hiboutik);
        await store.SaveLifecycleAsync(hiboutik with { CashPaymentTtc = Money.FromCents(6000) }, [Payment(hiboutik.Id, PaymentBucket.Cash, 6000, 11)]);
        var afterHiboutik = await store.GetOperationalSummaryAsync(BusinessDate);
        Assert.AreEqual(summary.TurnoverTtc, afterHiboutik.TurnoverTtc);
        Assert.AreEqual(summary.ReceivedTtc, afterHiboutik.ReceivedTtc);
    }

    [TestMethod]
    public async Task OptionalPlannedTimePersistsForBothFulfilmentModesAndKeepsDateSemantics()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var product = new OrderEntryProduct(new ProductAggregate(
            new Product(Guid.Parse("23000000-0000-0000-0000-000000000001"), "P-OPTIONAL-TIME", "Plat", Guid.NewGuid(), Money.FromCents(4000), 10m, true, true, false, default, default),
            [], new Dictionary<Guid, IReadOnlyList<ProductOption>>()), "Plats");
        var settings = new TestSettingsStore(BusinessSettings.Defaults(clock.UtcNow) with { DeliveryMinMerchandiseTotalTtc = Money.Zero });
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), clock: clock);
        using var service = new OrderEntryService(new SingleEntryCatalogue(product), settings, store, new NoopDispatcher(), new DeterministicIds(), clock);

        foreach (var mode in new[] { FulfilmentMode.Retrait, FulfilmentMode.Livraison })
        {
            var result = await service.ConfirmNewOrderAsync(new NewOrderDraft(
                [new OrderLineDraft(Guid.Empty, product.Aggregate, [], [], 1, product.CategoryName)],
                mode, BusinessDate.AddDays(2), null, null, null, null, false));

            Assert.IsTrue(result.Succeeded, string.Join(";", result.Issues.Select(issue => issue.Message)));
            Assert.IsNull(result.CommittedOrder!.PlannedFulfilmentTime);
            var reloaded = await store.GetByIdAsync(result.CommittedOrder.Id);
            Assert.IsNotNull(reloaded);
            Assert.IsNull(reloaded!.PlannedFulfilmentTime);
            Assert.IsTrue(reloaded.AdvanceOrderMarker);
        }

        var selected = await service.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, product.Aggregate, [], [], 1, product.CategoryName)],
            FulfilmentMode.Retrait, BusinessDate.AddDays(2), new TimeOnly(18, 25), null, null, null, false));
        Assert.IsTrue(selected.Succeeded, string.Join(";", selected.Issues.Select(issue => issue.Message)));
        Assert.AreEqual(new TimeOnly(18, 25), (await store.GetByIdAsync(selected.CommittedOrder!.Id))!.PlannedFulfilmentTime);
        Assert.AreEqual(3, (await store.GetOperationalSummaryAsync(BusinessDate)).FutureOrderCount);
    }

    [TestMethod]
    public async Task RealApplicationSqliteMutationNotifierSchedulerAndRecoverySnapshotShareOneSeam()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var runner = new SqliteTransactionRunner(factory);
        var ids = new DeterministicIds();
        var snapshotService = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await using var scheduler = new DebouncedRecoveryScheduler(snapshotService, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<DebouncedRecoveryScheduler>.Instance);
        using var notifier = await DurableChangeNotifier.CreateAsync(paths, clock, scheduler, Microsoft.Extensions.Logging.Abstractions.NullLogger<DurableChangeNotifier>.Instance);
        var guard = new TestWriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var catalogueStore = new SqliteCatalogueStore(factory, runner, ids, clock);
        var settingsStore = new SqliteBusinessSettingsStore(factory, runner, clock);
        var catalogue = new CatalogueService(catalogueStore, guard, notifier);
        var settings = new BusinessSettingsService(settingsStore, guard, notifier);

        var category = (await catalogue.CreateCategoryAsync("Recovery Plats")).Value!;
        var productId = (await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "RECOVERY-1", "Recovery Product", category.Id, Money.FromCents(1250), 10m, true, true, false, []))).Value!;
        var orderStore = new SqliteOrderStore(factory, runner, idGenerator: ids, clock: clock);
        var entryCatalogue = new OrderEntryCatalogueService(catalogueStore);
        var product = (await entryCatalogue.GetActiveProductAsync(productId))!;
        using var entry = new OrderEntryService(entryCatalogue, settingsStore, orderStore, new NoopDispatcher(), ids, clock, guard, notifier);
        var order = await entry.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, product.Aggregate, [], [], 1, product.CategoryName)],
            FulfilmentMode.Retrait, BusinessDate.AddDays(1), new TimeOnly(18, 0), null, null, null, false));
        Assert.IsTrue(order.Succeeded, string.Join(";", order.Issues.Select(issue => issue.Message)));

        await scheduler.FlushAsync();
        var latest = await LatestVerifiedSnapshotAsync(paths);
        Assert.IsNotNull(latest);
        await using (var snapshotConnection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(latest!.Value.DatabasePath))
        {
            Assert.AreEqual(1L, await ScalarAsync(snapshotConnection, "SELECT COUNT(*) FROM categories WHERE name='Recovery Plats';"));
            Assert.AreEqual(1L, await ScalarAsync(snapshotConnection, "SELECT COUNT(*) FROM products WHERE code='RECOVERY-1';"));
            Assert.AreEqual(1L, await ScalarAsync(snapshotConnection, "SELECT COUNT(*) FROM orders WHERE order_id='" + order.CommittedOrder!.Id + "';"));
        }

        var committedSequence = latest.Value.Metadata.DurableChangeSequence;
        guard.State = WriteAuthorityState.NonAuthoritativeReadOnly;
        var blocked = await catalogue.CreateCategoryAsync("Must Not Persist");
        Assert.IsFalse(blocked.Succeeded);
        guard.State = WriteAuthorityState.Authoritative;
        var currentSettings = await settingsStore.GetAsync();
        Assert.IsTrue((await settings.UpdateAsync(currentSettings)).Succeeded);
        await scheduler.FlushAsync();
        var afterNoOpAndBlocked = await LatestVerifiedSnapshotAsync(paths);
        Assert.AreEqual(committedSequence, afterNoOpAndBlocked!.Value.Metadata.DurableChangeSequence);
    }

    private static async Task<SqliteConnectionFactory> InitializeProductionAsync(TestPaths paths, IBusinessClock clock)
    {
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        return factory;
    }

    private static async Task InitializeV4Async(SqliteConnectionFactory factory, IBusinessClock clock) =>
        await new SqliteMigrationRunner(factory, M01Migrations.All.Concat(M03Migrations.All).Concat(M04Migrations.All), clock).InitializeAsync();

    private static async Task InsertV4OrderAsync(SqliteConnectionFactory factory, Guid id, DateTimeOffset createdAt, DateOnly plannedDate, string telephone, string comment, bool withChildren)
    {
        var itemId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var adjustmentId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var taxId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        await ExecuteAsync(factory, """
            INSERT INTO orders(order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents)
            VALUES($id,'POS','OPEN',$created,$updated,NULL,NULL,'RETRAIT',$planned,'11:00:00.0000000',0,$telephone,'12 rue v4',$comment,1250,0,0,NULL,0);
            """, ("$id", id.ToString()), ("$created", createdAt.ToString("O", CultureInfo.InvariantCulture)), ("$updated", createdAt.ToString("O", CultureInfo.InvariantCulture)), ("$planned", plannedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$telephone", telephone), ("$comment", comment));
        if (!withChildren) return;
        await ExecuteAsync(factory, "INSERT INTO order_items(order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents) VALUES($item,$order,0,NULL,'V4-P','V4 product','V4 category',1000,'10',1,1,1000,1250);", ("$item", itemId.ToString()), ("$order", id.ToString()));
        await ExecuteAsync(factory, "INSERT INTO order_item_adjustments(order_item_adjustment_id,order_item_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate) VALUES($adjustment,$item,0,'CUSTOM_ADJUSTMENT',NULL,'V4 group','V4 adjustment',250,'10');", ("$adjustment", adjustmentId.ToString()), ("$item", itemId.ToString()));
        await ExecuteAsync(factory, "INSERT INTO order_tax_breakdown(order_tax_breakdown_id,order_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents) VALUES($tax,$order,'10',1250,113);", ("$tax", taxId.ToString()), ("$order", id.ToString()));
    }

    private static async Task<OrderRowEvidence[]> ReadV4EvidenceAsync(SqliteConnectionFactory factory, IEnumerable<Guid> ids)
    {
        var result = new List<OrderRowEvidence>();
        foreach (var id in ids)
        {
            await using var connection = await factory.OpenLiveConnectionAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents FROM orders WHERE order_id=$id;";
            command.Parameters.AddWithValue("$id", id.ToString());
            await using var reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            result.Add(new(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetString(7), reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetInt64(10), reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13), reader.GetInt64(14), reader.GetInt64(15), reader.GetInt64(16), reader.IsDBNull(17) ? null : reader.GetString(17), reader.GetInt64(18)));
        }
        return result.ToArray();
    }

    private static OrderSnapshot Snapshot(Guid id, DateTimeOffset createdAt, DateOnly plannedDate, long total = 1250, OrderStatus status = OrderStatus.Open, OrderSourceType source = OrderSourceType.Pos, string telephone = "0612345678", string comment = "synthetic") => new(
        id, source, status, createdAt, createdAt, status == OrderStatus.Closed ? createdAt : null, status == OrderStatus.Cancelled ? createdAt : null,
        FulfilmentMode.Retrait, plannedDate, new TimeOnly(11, 0), plannedDate > BusinessDate, telephone, "12 rue des Tests", comment, Money.FromCents(total), false, false, null, Money.Zero,
        [new(ChildId(id, 10), 0, Guid.Parse("30000000-0000-0000-0000-000000000001"), "P", "Plat", "Plats", Money.FromCents(total), 10m, true, 1, Money.FromCents(total), Money.FromCents(total), [new(ChildId(id, 11), 0, OrderAdjustmentKind.CustomAdjustment, null, "g", "A", Money.FromCents(0), 10m)])],
        [new(10m, Money.FromCents(total), Money.FromCents(0), ChildId(id, 12))]);

    private static Guid ChildId(Guid parent, int offset) => Guid.Parse($"30000000-0000-0000-0000-{(long.Parse(parent.ToString()[24..], CultureInfo.InvariantCulture) + offset):D12}");

    private static async Task FailPostApplyAsync(SqliteConnection connection, SqliteTransaction transaction, IBusinessClock clock, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM table_that_does_not_exist;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PaymentAdjustment Payment(Guid orderId, PaymentBucket bucket, long delta, int id, DateOnly? date = null)
    {
        var effectiveDate = date ?? BusinessDate;
        var effectiveAt = new DateTimeOffset(effectiveDate.ToDateTime(new TimeOnly(0, 0)), TimeSpan.Zero);
        return new(Guid.Parse($"40000000-0000-0000-0000-{id:D12}"), orderId, bucket, Money.FromCents(delta), effectiveAt, new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));
    }

    private static void AssertSnapshotEqual(OrderSnapshot? expected, OrderSnapshot? actual)
    {
        Assert.IsNotNull(expected);
        Assert.IsNotNull(actual);
        Assert.AreEqual(expected!.Id, actual!.Id);
        Assert.AreEqual(expected.SourceType, actual.SourceType);
        Assert.AreEqual(expected.Status, actual.Status);
        Assert.AreEqual(expected.CreatedAt, actual.CreatedAt);
        Assert.AreEqual(expected.UpdatedAt, actual.UpdatedAt);
        Assert.AreEqual(expected.ClosedAt, actual.ClosedAt);
        Assert.AreEqual(expected.CancelledAt, actual.CancelledAt);
        Assert.AreEqual(expected.Fulfilment, actual.Fulfilment);
        Assert.AreEqual(expected.PlannedFulfilmentDate, actual.PlannedFulfilmentDate);
        Assert.AreEqual(expected.PlannedFulfilmentTime, actual.PlannedFulfilmentTime);
        Assert.AreEqual(expected.AdvanceOrderMarker, actual.AdvanceOrderMarker);
        Assert.AreEqual(expected.Telephone, actual.Telephone);
        Assert.AreEqual(expected.DeliveryAddress, actual.DeliveryAddress);
        Assert.AreEqual(expected.Comment, actual.Comment);
        Assert.AreEqual(expected.TotalTtc, actual.TotalTtc);
        Assert.AreEqual(expected.ManualTotalOverrideActive, actual.ManualTotalOverrideActive);
        Assert.AreEqual(expected.PickupDiscountApplied, actual.PickupDiscountApplied);
        Assert.AreEqual(expected.PickupDiscountRate, actual.PickupDiscountRate);
        Assert.AreEqual(expected.DeliveryFeeTtc, actual.DeliveryFeeTtc);
        Assert.AreEqual(expected.Reference, actual.Reference);
        Assert.AreEqual(expected.CardPaymentTtc, actual.CardPaymentTtc);
        Assert.AreEqual(expected.CashPaymentTtc, actual.CashPaymentTtc);
        Assert.HasCount(expected.Items.Count, actual.Items);
        for (var i = 0; i < expected.Items.Count; i++)
        {
            var left = expected.Items[i]; var right = actual.Items[i];
            Assert.AreEqual(left with { Adjustments = [] }, right with { Adjustments = [] });
            CollectionAssert.AreEqual(left.Adjustments.ToArray(), right.Adjustments.ToArray());
        }
        CollectionAssert.AreEqual(expected.TaxBreakdown.ToArray(), actual.TaxBreakdown.ToArray());
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string> TextAsync(SqliteConnectionFactory factory, string sql, string id)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql; command.Parameters.AddWithValue("$id", id);
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<(string DatabasePath, RecoverySnapshotMetadata Metadata)?> LatestVerifiedSnapshotAsync(TestPaths paths)
    {
        var candidates = new List<(string DatabasePath, RecoverySnapshotMetadata Metadata)>();
        foreach (var directory in Directory.EnumerateDirectories(paths.RecoveryDirectory, "recovery-*"))
        {
            try
            {
                var databasePath = Path.Combine(directory, "snapshot.db");
                var metadata = await SqliteLocalRecoverySnapshotService.VerifyAsync(databasePath, Path.Combine(directory, "metadata.json"));
                candidates.Add((databasePath, metadata));
            }
            catch (RecoverySnapshotValidationException) { }
        }

        return candidates.OrderByDescending(candidate => candidate.Metadata.DurableChangeSequence).FirstOrDefault();
    }

    private static async Task ExecuteAsync(SqliteConnectionFactory factory, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record OrderRowEvidence(string Id, string Source, string Status, string Created, string Updated, string? Closed, string? Cancelled, string Fulfilment, string PlannedDate, string? PlannedTime, long Advance, string? Telephone, string? Address, string? Comment, long Total, long Manual, long Discount, string? Rate, long Fee);

    private sealed class RecordingSnapshotService : ILocalRecoverySnapshotService
    {
        public List<DurableChange> Changes { get; } = [];
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.FromResult(new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1));
        }
    }

    private sealed class SingleEntryCatalogue(OrderEntryProduct product) : IOrderEntryCatalogueQueries
    {
        public Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CategorySummary>>([new(product.Aggregate.Product.CategoryId, product.CategoryName)]);
        public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, Guid? categoryId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProductSummary>>([new(product.Aggregate.Product.Id, product.Aggregate.Product.Code, product.Aggregate.Product.Name, product.Aggregate.Product.CategoryId, product.CategoryName, product.Aggregate.Product.PriceTtc, product.Aggregate.Product.VatRate, true, true, false)]);
        public Task<OrderEntryProduct?> GetActiveProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult<OrderEntryProduct?>(productId == product.Aggregate.Product.Id ? product : null);
    }

    private sealed class NoopDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => BusinessDateStatic;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
        private static DateOnly BusinessDateStatic => new(2026, 8, 31);
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"50000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M05.Evidence", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data"); RecoveryDirectory = Path.Combine(RootDirectory, "Recovery"); CacheDirectory = Path.Combine(RootDirectory, "Cache"); LogsDirectory = Path.Combine(RootDirectory, "Logs"); ConfigDirectory = Path.Combine(RootDirectory, "Config"); TempDirectory = Path.Combine(RootDirectory, "Temp");
            EnsureInitialized();
        }
        public string RootDirectory { get; } public string DataDirectory { get; } public string RecoveryDirectory { get; } public string CacheDirectory { get; } public string LogsDirectory { get; } public string ConfigDirectory { get; } public string TempDirectory { get; } public string LiveDatabasePath => Path.Combine(DataDirectory, "live.db");
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
