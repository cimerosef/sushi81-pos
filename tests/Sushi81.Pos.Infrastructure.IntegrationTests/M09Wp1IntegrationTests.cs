using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M09Wp1IntegrationTests
{
    private static readonly DateOnly BusinessDate = new(2026, 9, 14);

    [TestMethod]
    public async Task M08DatabaseMigrationAddsNullableSourceTotalWithoutChangingExistingOrders()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        var preM09 = M01Migrations.All.Concat(M03Migrations.All).Concat(M04Migrations.All).Concat(M05Migrations.All).Concat(M08Migrations.All).ToArray();
        await new SqliteMigrationRunner(factory, preM09, clock).InitializeAsync();

        var existing = Snapshot(Guid.Parse("51000000-0000-0000-0000-000000000001"), total: 1250);
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);
        await store.SaveAsync(existing);

        var snapshots = new RecordingSnapshotService();
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshots).InitializeAsync();

        Assert.AreEqual(8L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.HasCount(1, snapshots.Changes);
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM pragma_table_info('orders') WHERE name='source_total_ttc_cents';"));
        Assert.IsNull((await store.GetByIdAsync(existing.Id))!.SourceTotalTtc);
        Assert.AreEqual(existing.TotalTtc, (await store.GetByIdAsync(existing.Id))!.TotalTtc);
        Assert.IsNull(await NullableLongAsync(factory, "SELECT source_total_ttc_cents FROM orders WHERE order_id=$id;", existing.Id.ToString()));
    }

    [TestMethod]
    public async Task SourceTotalRoundTripsAsCentPreciseReferenceWithoutChangingAuthoritativeTotal()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);
        var order = Snapshot(Guid.Parse("52000000-0000-0000-0000-000000000001"), total: 3231, source: OrderSourceType.HiboutikPaste) with
        {
            SourceTotalTtc = Money.FromCents(3590)
        };

        await store.SaveAsync(order);
        var reloaded = await store.GetByIdAsync(order.Id);

        Assert.IsNotNull(reloaded);
        Assert.AreEqual(Money.FromCents(3590), reloaded!.SourceTotalTtc);
        Assert.AreEqual(Money.FromCents(3231), reloaded.TotalTtc);
        Assert.AreEqual(3590L, await ScalarAsync(factory, "SELECT source_total_ttc_cents FROM orders WHERE order_id='52000000-0000-0000-0000-000000000001';"));
        Assert.AreEqual(3231L, await ScalarAsync(factory, "SELECT total_ttc_cents FROM orders WHERE order_id='52000000-0000-0000-0000-000000000001';"));
    }

    [TestMethod]
    public async Task BrowserAndSearchRowsExposePassiveHiboutikSourceEvidence()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var store = new SqliteOrderStore(factory, new SqliteTransactionRunner(factory), idGenerator: new DeterministicIds(), clock: clock);
        var order = Snapshot(Guid.Parse("57000000-0000-0000-0000-000000000001"), total: 3231, source: OrderSourceType.HiboutikPaste) with { SourceTotalTtc = Money.FromCents(3590) };

        await store.SaveAsync(order);
        var byDate = (await store.ListByPlannedDateAsync(BusinessDate)).Single();
        var searched = (await store.SearchAsync("synthetic M09 WP1")).Single();

        foreach (var row in new[] { byDate, searched })
        {
            Assert.AreEqual(OrderSourceType.HiboutikPaste, row.SourceType);
            Assert.AreEqual(Money.FromCents(3590), row.SourceTotalTtc);
            Assert.AreEqual(Money.FromCents(3231), row.TotalTtc);
        }
    }

    [TestMethod]
    public async Task ExactActiveProductCodeLookupUsesCurrentCatalogueIdentityOnly()
    {
        using var paths = new TestPaths();
        var clock = new FixedClock();
        var factory = await InitializeProductionAsync(paths, clock);
        var ids = new DeterministicIds();
        var catalogue = new SqliteCatalogueStore(factory, new SqliteTransactionRunner(factory), ids, clock);
        var category = (await catalogue.CreateCategoryAsync("M09 Plats")).Value!;
        var activeId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "HIB-EXACT-1", "Saumon", category.Id, Money.FromCents(1250), 10m, true, true, false, []))).Value!;
        var inactiveId = (await catalogue.CreateProductAsync(new ProductDraft(Guid.Empty, "HIB-INACTIVE-1", "Inactive", category.Id, Money.FromCents(950), 10m, false, true, false, []))).Value!;
        var entryCatalogue = new OrderEntryCatalogueService(catalogue);

        var exact = await entryCatalogue.GetActiveProductByCodeAsync("  hib-exact-1  ");
        var inactive = await entryCatalogue.GetActiveProductByCodeAsync("HIB-INACTIVE-1");
        var name = await entryCatalogue.GetActiveProductByCodeAsync("Saumon");
        var prefix = await entryCatalogue.GetActiveProductByCodeAsync("HIB-EXACT");
        var missing = await entryCatalogue.GetActiveProductByCodeAsync(null);

        Assert.IsNotNull(exact);
        Assert.AreEqual(activeId, exact!.Aggregate.Product.Id);
        Assert.AreEqual("HIB-EXACT-1", exact.Aggregate.Product.Code);
        Assert.IsNull(inactive);
        Assert.IsNull(name);
        Assert.IsNull(prefix);
        Assert.IsNull(missing);
        Assert.AreNotEqual(activeId, inactiveId);
    }

    private static async Task<SqliteConnectionFactory> InitializeProductionAsync(TestPaths paths, IBusinessClock clock)
    {
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        return factory;
    }

    private static OrderSnapshot Snapshot(Guid id, long total, OrderSourceType source = OrderSourceType.Pos) => new(
        id, source, OrderStatus.Open, new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 14, 8, 0, 0, TimeSpan.Zero),
        null, null, FulfilmentMode.Retrait, BusinessDate, new TimeOnly(11, 0), false, "06 12 34 56 78", null, "synthetic M09 WP1",
        Money.FromCents(total), false, false, null, Money.Zero,
        [new(Guid.Parse("53000000-0000-0000-0000-000000000001"), 0, Guid.Parse("54000000-0000-0000-0000-000000000001"), "P-M09", "Produit M09", "Plats", Money.FromCents(total), 10m, true, 1, Money.FromCents(total), Money.FromCents(total), [])],
        [new(10m, Money.FromCents(total), Money.Zero, Guid.Parse("55000000-0000-0000-0000-000000000001"))]);

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<long?> NullableLongAsync(SqliteConnectionFactory factory, string sql, string id)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private sealed class RecordingSnapshotService : ILocalRecoverySnapshotService
    {
        public List<DurableChange> Changes { get; } = [];
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.FromResult(new RecoverySnapshotResult("synthetic.db", "synthetic.json", "sha256:synthetic", change.CommittedAtUtc, change.Sequence, 7));
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => M09Wp1IntegrationTests.BusinessDate;
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"56000000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M09.WP1.Tests", Guid.NewGuid().ToString("N"));
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
