using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Archive;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Export;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M13GestionExportCompactionIntegrationTests
{
    [TestMethod]
    public async Task MigrationTenPreservesPopulatedM11M12DatabaseAndAddsProofLedger()
    {
        using var paths = new TestPaths();
        var clock = new TestClock();
        var factory = new SqliteConnectionFactory(paths);
        var beforeM13 = ProductionMigrations.All.Where(migration => migration.Version < 10).ToArray();
        await new SqliteMigrationRunner(factory, beforeM13, clock).InitializeAsync();
        var id = Guid.Parse("53000000-0000-0000-0000-000000000001");
        var runner = new SqliteTransactionRunner(factory);
        var orders = new SqliteOrderStore(factory, runner, idGenerator: new DeterministicIds(), clock: clock);
        await orders.SaveLifecycleAsync(ClosedOrder(id, 2026, "M13 migration preservation"), [Payment(id, 2026)]);
        await ExecuteAsync(factory, """
            INSERT INTO annual_archive_completions(archive_year,archive_format_version,archive_schema_version,archive_file_name,archive_order_count,archive_sha256,completed_at_utc)
            VALUES(2025,'M12-WP1-1','1','sushi81-archive-2025.db',1,$sha,'2026-01-01T00:00:00.0000000+00:00');
            """, ("$sha", new string('A', 64)));

        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, new FixedSnapshotService()).InitializeAsync();

        Assert.AreEqual(10L, await ScalarAsync(factory, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", id.ToString("D")));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM annual_archive_completions WHERE archive_year=2025 AND archive_sha256=$sha;", new string('A', 64)));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='annual_archive_order_proofs';"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM annual_archive_order_proofs;"));
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [TestMethod]
    public async Task ExpiredArchivedSuccessPrunesAsWholeBatchAndSecondRunIsNoOp()
    {
        using var fixture = await Fixture.CreateAsync();
        var id = Guid.Parse("53100000-0000-0000-0000-000000000001");
        var batch = await fixture.AddSuccessfulOrderAsync(id, 2026, "eligible archived order", fixture.Clock.UtcNow.AddDays(-30));
        await fixture.ArchiveService.FinalizeNextArchiveAsync();

        var before = await ReadLedgerStateAsync(fixture.Factory);
        var first = await fixture.CreateCompactor().CompactAsync();

        Assert.AreEqual(1, first.SuccessfulBatchesExamined);
        Assert.AreEqual(1, first.BatchesPruned);
        Assert.AreEqual(0, first.BatchesRetained);
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", batch.ToString("D")));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batch_orders WHERE batch_id=$batch;", batch.ToString("D")));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_emissions WHERE batch_id=$batch;", batch.ToString("D")));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_order_proofs WHERE order_id=$id;", id.ToString("D")));
        Assert.AreNotEqual(before, await ReadLedgerStateAsync(fixture.Factory));

        var stateAfterFirst = await ReadLedgerStateAsync(fixture.Factory);
        var second = await fixture.CreateCompactor().CompactAsync();
        Assert.AreEqual(0, second.SuccessfulBatchesExamined);
        Assert.AreEqual(0, second.BatchesPruned);
        Assert.AreEqual(0, second.BatchesRetained);
        Assert.AreEqual(stateAfterFirst, await ReadLedgerStateAsync(fixture.Factory));
    }

    [TestMethod]
    public async Task RetentionBoundaryAndPreparedDependencyAreConservative()
    {
        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("53200000-0000-0000-0000-000000000001");
            var batch = await fixture.AddSuccessfulOrderAsync(id, 2026, "one second inside retention", fixture.Clock.UtcNow.AddDays(-30).AddSeconds(1));
            await fixture.ArchiveService.FinalizeNextArchiveAsync();
            var result = await fixture.CreateCompactor().CompactAsync();
            Assert.AreEqual(0, result.BatchesPruned);
            Assert.AreEqual(1, result.BatchesRetained);
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", batch.ToString("D")));
        }

        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("53200000-0000-0000-0000-000000000003");
            var batch = await fixture.AddSuccessfulOrderAsync(id, 2027, "old live-order history", fixture.Clock.UtcNow.AddDays(-45));
            var result = await fixture.CreateCompactor().CompactAsync();
            Assert.AreEqual(0, result.BatchesPruned);
            Assert.AreEqual(1, result.BatchesRetained);
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", batch.ToString("D")));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", id.ToString("D")));
        }

        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("53200000-0000-0000-0000-000000000002");
            var batch = await fixture.AddSuccessfulOrderAsync(id, 2026, "prepared dependency", fixture.Clock.UtcNow.AddDays(-45));
            var updated = ClosedOrder(id, 2026, "prepared dependency changed") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(-1) };
            await fixture.OrderStore.SaveLifecycleAsync(updated, []);
            await fixture.ArchiveService.FinalizeNextArchiveAsync();
            await ExecuteAsync(fixture.Factory, "UPDATE export_batches SET generated_at_utc=$old WHERE status='PREPARED';", ("$old", Format(fixture.Clock.UtcNow.AddDays(-60))));

            var result = await fixture.CreateCompactor().CompactAsync();

            Assert.AreEqual(1, result.BatchesRetained);
            Assert.AreEqual(0, result.BatchesPruned);
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch AND status='SUCCESS';", batch.ToString("D")));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED' AND completed_at_utc IS NULL;"));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batch_orders o JOIN export_batches b ON b.batch_id=o.batch_id WHERE o.order_id=$id AND b.status='PREPARED';", id.ToString("D")));
        }
    }

    [TestMethod]
    public async Task LiveMissingProofMalformedProofAndMixedBatchRemainDurable()
    {
        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("53300000-0000-0000-0000-000000000001");
            var batch = await fixture.AddSuccessfulOrderAsync(id, 2026, "deleted without proof", fixture.Clock.UtcNow.AddDays(-45));
            await fixture.ArchiveService.FinalizeNextArchiveAsync();
            await ExecuteAsync(fixture.Factory, "DELETE FROM annual_archive_order_proofs WHERE order_id=$id;", ("$id", id.ToString("D")));
            var result = await fixture.CreateCompactor().CompactAsync();
            Assert.AreEqual(1, result.BatchesRetained);
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", batch.ToString("D")));
        }

        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("53300000-0000-0000-0000-000000000002");
            var batch = await fixture.AddSuccessfulOrderAsync(id, 2026, "malformed proof", fixture.Clock.UtcNow.AddDays(-45));
            await fixture.ArchiveService.FinalizeNextArchiveAsync();
            await ExecuteAsync(fixture.Factory, "UPDATE annual_archive_order_proofs SET archive_completed_at_utc='not-a-date' WHERE order_id=$id;", ("$id", id.ToString("D")));
            var result = await fixture.CreateCompactor().CompactAsync();
            Assert.AreEqual(1, result.BatchesRetained);
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", batch.ToString("D")));
        }

        using (var fixture = await Fixture.CreateAsync())
        {
            var archivedId = Guid.Parse("53300000-0000-0000-0000-000000000003");
            var liveId = Guid.Parse("53300000-0000-0000-0000-000000000004");
            var batch = await fixture.AddSuccessfulOrdersAsync(
                [(archivedId, 2026, "mixed archived order"), (liveId, 2027, "mixed live order")],
                fixture.Clock.UtcNow.AddDays(-45));
            await fixture.ArchiveService.FinalizeNextArchiveAsync();
            var result = await fixture.CreateCompactor().CompactAsync();
            Assert.AreEqual(1, result.BatchesRetained);
            Assert.AreEqual(0, result.BatchesPruned);
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", batch.ToString("D")));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM orders WHERE order_id=$id;", liveId.ToString("D")));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM annual_archive_order_proofs WHERE order_id=$id;", archivedId.ToString("D")));
        }
    }

    [TestMethod]
    public async Task LiveOrderSuccessRemainsAndSelectionIsUnchangedByLegalCompaction()
    {
        using var fixture = await Fixture.CreateAsync();
        var archivedId = Guid.Parse("53400000-0000-0000-0000-000000000001");
        var liveId = Guid.Parse("53400000-0000-0000-0000-000000000002");
        var archivedBatch = await fixture.AddSuccessfulOrderAsync(archivedId, 2026, "archived-only batch", fixture.Clock.UtcNow.AddDays(-45));
        await fixture.AddSuccessfulOrderAsync(liveId, 2027, "live baseline", fixture.Clock.UtcNow.AddDays(-45));
        await fixture.OrderStore.SaveLifecycleAsync(
            ClosedOrder(liveId, 2027, "live changed after export") with { UpdatedAt = fixture.Clock.UtcNow.AddMinutes(-2) },
            []);
        await fixture.ArchiveService.FinalizeNextArchiveAsync();

        var before = await fixture.ExportService.SelectAsync(new ExportSelectionOptions());
        Assert.HasCount(1, before.Actions);
        Assert.AreEqual(liveId, before.Actions.Single().OrderId);
        Assert.AreEqual(ExportAction.Update, before.Actions.Single().Action);
        var beforeFingerprint = SelectionFingerprint(before);

        var compacted = await fixture.CreateCompactor().CompactAsync();
        var after = await fixture.ExportService.SelectAsync(new ExportSelectionOptions());

        Assert.AreEqual(1, compacted.BatchesPruned);
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", archivedBatch.ToString("D")));
        Assert.AreEqual(beforeFingerprint, SelectionFingerprint(after));
        Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [TestMethod]
    public async Task CompactionFailureRollsBackLedgerAndProofAndNonAuthoritativeDeviceCannotCompact()
    {
        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("53500000-0000-0000-0000-000000000001");
            await fixture.AddSuccessfulOrderAsync(id, 2026, "rollback candidate", fixture.Clock.UtcNow.AddDays(-45));
            await fixture.ArchiveService.FinalizeNextArchiveAsync();
            var before = await ReadLedgerStateAsync(fixture.Factory);
            var failing = fixture.CreateCompactor(stage => stage == "after-emissions-delete-before-batch-delete" ? new InvalidOperationException("injected compaction failure") : null);

            await Assert.ThrowsAsync<InvalidOperationException>(() => failing.CompactAsync());

            Assert.AreEqual(before, await ReadLedgerStateAsync(fixture.Factory));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE status='SUCCESS';"));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_emissions;"));
            Assert.AreEqual(0L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        }

        using (var fixture = await Fixture.CreateAsync())
        {
            var id = Guid.Parse("53500000-0000-0000-0000-000000000002");
            var batch = await fixture.AddSuccessfulOrderAsync(id, 2026, "authority guard candidate", fixture.Clock.UtcNow.AddDays(-45));
            await fixture.ArchiveService.FinalizeNextArchiveAsync();
            var before = await ReadLedgerStateAsync(fixture.Factory);
            fixture.SetAuthorityState(WriteAuthorityState.NonAuthoritativeReadOnly);

            await Assert.ThrowsAsync<WriteAuthorityException>(() => fixture.CreateCompactor().CompactAsync());

            Assert.AreEqual(before, await ReadLedgerStateAsync(fixture.Factory));
            Assert.AreEqual(1L, await ScalarAsync(fixture.Factory, "SELECT COUNT(*) FROM export_batches WHERE batch_id=$batch;", batch.ToString("D")));
        }
    }

    private static string SelectionFingerprint(ExportSelectionResult result) =>
        string.Join("\n", result.Actions.Select(ExportPayloadSerializer.SerializeAction))
        + "\n--diagnostics--\n"
        + string.Join("\n", result.Diagnostics.Select(item => $"{item.OrderId:D}|{item.Severity}|{item.Code}|{item.Message}"));

    private static async Task<string> ReadLedgerStateAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        var output = new StringBuilder();
        foreach (var (table, order) in new[]
        {
            ("export_batches", "batch_id"),
            ("export_batch_orders", "batch_id,order_id"),
            ("export_emissions", "emission_id"),
            ("annual_archive_completions", "archive_year"),
            ("annual_archive_order_proofs", "order_id")
        })
        {
            output.Append(table).Append(':');
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table} ORDER BY {order};";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                output.Append('|');
                for (var index = 0; index < reader.FieldCount; index++)
                {
                    if (index > 0) output.Append(',');
                    output.Append(reader.IsDBNull(index) ? "<NULL>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture));
                }
            }
            output.AppendLine();
        }
        return output.ToString();
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql, string? parameter = null)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (parameter is not null) command.Parameters.AddWithValue(sql.Contains("$batch", StringComparison.Ordinal) ? "$batch" : sql.Contains("$sha", StringComparison.Ordinal) ? "$sha" : "$id", parameter);
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnectionFactory factory, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static OrderSnapshot ClosedOrder(Guid id, int year, string comment) => new(
        id,
        OrderSourceType.Pos,
        OrderStatus.Closed,
        new DateTimeOffset(year, 12, 1, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(year, 12, 31, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(year, 12, 31, 18, 0, 0, TimeSpan.Zero),
        null,
        FulfilmentMode.Retrait,
        new DateOnly(year, 12, 31),
        new TimeOnly(12, 0),
        false,
        null,
        null,
        comment,
        Money.FromCents(1000),
        false,
        false,
        null,
        Money.Zero,
        [new(Guid.NewGuid(), 0, Guid.NewGuid(), "P-M13", "M13 produit", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])],
        [new(10m, Money.FromCents(1000), Money.FromCents(91), Guid.NewGuid())])
    {
        Reference = $"M13-{id.ToString("N")[..8]}",
        CardPaymentTtc = Money.FromCents(1000),
        CashPaymentTtc = Money.Zero,
        SourceTotalTtc = Money.FromCents(1000)
    };

    private static PaymentAdjustment Payment(Guid orderId, int year) => new(
        Guid.NewGuid(),
        orderId,
        PaymentBucket.Card,
        Money.FromCents(1000),
        new DateTimeOffset(year, 12, 31, 18, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(year, 12, 31, 18, 1, 0, TimeSpan.Zero));

    private sealed class Fixture(TestPaths paths, TestClock clock, SqliteConnectionFactory factory, WriteAuthorityGuard authority, SqliteTransactionRunner runner, SqliteOrderStore orderStore, SqliteGestionExportStore exportStore, GestionExportService exportService, SqliteAnnualArchiveFinalizationService archiveService) : IDisposable
    {
        public TestPaths Paths { get; } = paths;
        public TestClock Clock { get; } = clock;
        public SqliteConnectionFactory Factory { get; } = factory;
        public SqliteOrderStore OrderStore { get; } = orderStore;
        public SqliteGestionExportStore ExportStore { get; } = exportStore;
        public GestionExportService ExportService { get; } = exportService;
        public SqliteAnnualArchiveFinalizationService ArchiveService { get; } = archiveService;
        private WriteAuthorityGuard Authority { get; } = authority;
        private SqliteTransactionRunner Runner { get; } = runner;

        public static async Task<Fixture> CreateAsync(WriteAuthorityState authorityState = WriteAuthorityState.Authoritative)
        {
            var paths = new TestPaths();
            var clock = new TestClock();
            var factory = new SqliteConnectionFactory(paths);
            await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
            var runner = new SqliteTransactionRunner(factory);
            var authority = new WriteAuthorityGuard(authorityState);
            var ids = new DeterministicIds();
            var orderStore = new SqliteOrderStore(factory, runner, idGenerator: ids, clock: clock);
            var exportStore = new SqliteGestionExportStore(factory, orderStore, runner, ids);
            var exportService = new GestionExportService(exportStore, exportStore, clock, authority, new NoOpDurableChangeNotifier(), ids);
            var archiveService = new SqliteAnnualArchiveFinalizationService(paths, clock, authority, factory, runner, exportStore, ids, new NoOpDurableChangeNotifier());
            return new Fixture(paths, clock, factory, authority, runner, orderStore, exportStore, exportService, archiveService);
        }

        public async Task<Guid> AddSuccessfulOrderAsync(Guid orderId, int year, string comment, DateTimeOffset completedAtUtc)
        {
            return await AddSuccessfulOrdersAsync([(orderId, year, comment)], completedAtUtc);
        }

        public async Task<Guid> AddSuccessfulOrdersAsync((Guid Id, int Year, string Comment)[] orders, DateTimeOffset completedAtUtc)
        {
            foreach (var (id, year, comment) in orders)
                await OrderStore.SaveLifecycleAsync(ClosedOrder(id, year, comment), [Payment(id, year)]);
            var prepared = (await ExportService.PrepareBatchAsync(new ExportSelectionOptions(), "m13-wp1-test")).Batch
                ?? throw new AssertFailedException("The synthetic orders did not produce an M11 batch.");
            await ExportService.MarkBatchSucceededAsync(prepared, completedAtUtc);
            return prepared.Payload.Meta.BatchId;
        }

        public SqliteGestionExportCompactionService CreateCompactor(Func<string, Exception?>? failureInjector = null) =>
            new(Factory, Runner, Authority, Clock, failureInjector);

        public void SetAuthorityState(WriteAuthorityState state) => Authority.SetState(state);

        public void Dispose()
        {
            Authority.Dispose();
            Paths.Dispose();
        }
    }

    private sealed class TestClock : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; } = new(2027, 2, 1, 8, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2027, 2, 1);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int count;
        public Guid NewId() => Guid.Parse($"53600000-0000-0000-0000-{Interlocked.Increment(ref count):D12}");
    }

    private sealed class FixedSnapshotService : ILocalRecoverySnapshotService
    {
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoverySnapshotResult("snapshot.db", "snapshot.json", "synthetic", DateTimeOffset.UtcNow, change.Sequence, 10));
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M13.WP1.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(path);
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LogsDirectory { get; }
        public string LiveDatabasePath { get; }
        public string ArchiveDirectory => Path.Combine(RootDirectory, "Archive");
        public void EnsureInitialized() { }
        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
