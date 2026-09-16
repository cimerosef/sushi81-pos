using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class PostM09HiboutikDashboardIntegrationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 17);

    [TestMethod]
    public async Task HiboutikDailyPaymentSummaryUsesSignedEffectiveDateDeltasAndExcludesCancelledAndPosOrders()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);
        using var service = new OrderLifecycleService(store, new DeterministicIds(), clock);

        var hiboutikOpen = Snapshot(Guid.Parse("61000000-0000-0000-0000-000000000001"), OrderSourceType.HiboutikPaste, OrderStatus.Open, total: 9000);
        var hiboutikClosed = Snapshot(Guid.Parse("61000000-0000-0000-0000-000000000002"), OrderSourceType.HiboutikPaste, OrderStatus.Closed, total: 8000);
        var hiboutikCancelled = Snapshot(Guid.Parse("61000000-0000-0000-0000-000000000003"), OrderSourceType.HiboutikPaste, OrderStatus.Cancelled, total: 7000);
        var pos = Snapshot(Guid.Parse("61000000-0000-0000-0000-000000000004"), OrderSourceType.Pos, OrderStatus.Open, total: 1300);

        foreach (var order in new[] { hiboutikOpen, hiboutikClosed, hiboutikCancelled, pos }) await store.SaveAsync(order);

        await store.SaveLifecycleAsync(hiboutikOpen with { CardPaymentTtc = Money.FromCents(1050), CashPaymentTtc = Money.FromCents(800) },
            [Payment(hiboutikOpen.Id, PaymentBucket.Card, 1250, 1), Payment(hiboutikOpen.Id, PaymentBucket.Cash, 800, 2), Payment(hiboutikOpen.Id, PaymentBucket.Card, -200, 3), Payment(hiboutikOpen.Id, PaymentBucket.Card, 777, 4, BusinessDate.AddDays(-1))]);
        await store.SaveLifecycleAsync(hiboutikClosed with { CardPaymentTtc = Money.FromCents(300), CashPaymentTtc = Money.FromCents(400) },
            [Payment(hiboutikClosed.Id, PaymentBucket.Card, 300, 5), Payment(hiboutikClosed.Id, PaymentBucket.Cash, 400, 6)]);
        await store.SaveLifecycleAsync(hiboutikCancelled with { CardPaymentTtc = Money.FromCents(999), CashPaymentTtc = Money.FromCents(999) },
            [Payment(hiboutikCancelled.Id, PaymentBucket.Card, 999, 7), Payment(hiboutikCancelled.Id, PaymentBucket.Cash, 999, 8)]);
        await store.SaveLifecycleAsync(pos with { CardPaymentTtc = Money.FromCents(700), CashPaymentTtc = Money.FromCents(600) },
            [Payment(pos.Id, PaymentBucket.Card, 700, 9), Payment(pos.Id, PaymentBucket.Cash, 600, 10)]);

        var summary = await service.GetOperationalSummaryAsync(BusinessDate);

        Assert.AreEqual(Money.FromCents(1300), summary.TurnoverTtc, "The ordinary turnover remains POS-originated only.");
        Assert.AreEqual(Money.FromCents(1300), summary.ReceivedTtc, "The ordinary received total remains POS-originated only.");
        Assert.AreEqual(Money.FromCents(700), summary.ReceivedCardTtc);
        Assert.AreEqual(Money.FromCents(600), summary.ReceivedCashTtc);
        Assert.AreEqual(Money.FromCents(1350), summary.HiboutikReceivedCardTtc, "Same-day signed CB deltas from open and closed Hiboutik orders only.");
        Assert.AreEqual(Money.FromCents(1200), summary.HiboutikReceivedCashTtc);
        Assert.AreEqual(2L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments WHERE order_id=$id;", ("$id", (object)hiboutikCancelled.Id.ToString())), "Cancelled adjustment rows remain retained.");
        Assert.AreEqual(7L, await ScalarAsync(factory, "SELECT COUNT(*) FROM payment_adjustments WHERE effective_business_date=$date AND order_id IN ($open,$closed,$cancelled);", ("$date", (object)BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$open", (object)hiboutikOpen.Id.ToString()), ("$closed", (object)hiboutikClosed.Id.ToString()), ("$cancelled", (object)hiboutikCancelled.Id.ToString())));
    }

    [TestMethod]
    public async Task SqliteHiboutikDailyPaymentSummaryReturnsExactZeroWhenNoMatchingRows()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);

        var summary = await store.GetOperationalSummaryAsync(BusinessDate);

        Assert.AreEqual(Money.Zero, summary.HiboutikReceivedCardTtc, "The real SQLite aggregation must return exact zero when no Hiboutik rows match the requested date.");
        Assert.AreEqual(Money.Zero, summary.HiboutikReceivedCashTtc, "The real SQLite aggregation must return exact zero when no Hiboutik rows match the requested date.");
        Assert.AreEqual(Money.Zero, summary.TurnoverTtc, "The ordinary summary remains zero for an empty synthetic database.");
        Assert.AreEqual(Money.Zero, summary.ReceivedTtc);
        Assert.AreEqual(Money.Zero, summary.ReceivedCardTtc);
        Assert.AreEqual(Money.Zero, summary.ReceivedCashTtc);
    }

    private static OrderSnapshot Snapshot(Guid id, OrderSourceType source, OrderStatus status, long total) => new(
        id, source, status, new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 17, 8, 0, 0, TimeSpan.Zero),
        status == OrderStatus.Closed ? new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero) : null,
        status == OrderStatus.Cancelled ? new DateTimeOffset(2026, 9, 17, 9, 0, 0, TimeSpan.Zero) : null,
        FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), false, "06 12 34 56 78", null, "synthetic post-M09 dashboard",
        Money.FromCents(total), false, false, null, Money.Zero,
        [new(id, 0, Guid.NewGuid(), "P-POST-M09", "Produit synthétique", "Tests", Money.FromCents(total), 10m, true, 1, Money.FromCents(total), Money.FromCents(total), [])], []);

    private static PaymentAdjustment Payment(Guid orderId, PaymentBucket bucket, long delta, int id, DateOnly? date = null)
    {
        var effectiveDate = date ?? BusinessDate;
        var effectiveAt = new DateTimeOffset(effectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new(Guid.Parse($"62000000-0000-0000-0000-{id:D12}"), orderId, bucket, Money.FromCents(delta), effectiveAt, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero));
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => PostM09HiboutikDashboardIntegrationTests.BusinessDate;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"63000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.PostM09.HiboutikDashboard.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path);
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath { get; }
        public void EnsureInitialized() { }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
