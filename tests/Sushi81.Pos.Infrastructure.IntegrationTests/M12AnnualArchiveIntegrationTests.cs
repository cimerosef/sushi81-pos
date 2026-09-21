using System.Globalization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Archive;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M12AnnualArchiveIntegrationTests
{
    [TestMethod]
    public void TargetYearUsesOnlyPreviousCompleteYearFromFebruary()
    {
        Assert.IsNull(AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2027, 1, 31)));
        Assert.AreEqual(2026, AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2027, 2, 1)));
        Assert.AreEqual(2026, AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2027, 3, 15)));
        Assert.AreEqual(2026, AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2027, 12, 31)));
    }

    [TestMethod]
    public async Task RealLiveReaderUsesBusinessLocalEndYearAndPreservesAllHistoricalFacts()
    {
        using var paths = new TestAppPaths();
        var clock = new TestClock(new DateOnly(2027, 2, 1), TimeZoneInfo.CreateCustomTimeZone("Business+02", TimeSpan.FromHours(2), "Business+02", "Business+02"));
        var factory = await InitializeAsync(paths, clock);
        var exportCountBefore = await ScalarAsync(factory, "SELECT COUNT(*) FROM export_batches;");
        var pos = Guid.NewGuid();
        var hiboutik = Guid.NewGuid();
        var utcBoundaryExcluded = Guid.NewGuid();
        var open = Guid.NewGuid();

        await InsertOrderAsync(factory, pos, "POS", "CLOSED", new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 21, 30, 0, TimeSpan.Zero), null, withChildren: true, reference: "20260101-001");
        await InsertOrderAsync(factory, hiboutik, "HIBOUTIK_PASTE", "CANCELLED", new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero), null, new DateTimeOffset(2026, 12, 31, 21, 30, 0, TimeSpan.Zero), withChildren: false, reference: "20260101-002");
        await InsertOrderAsync(factory, utcBoundaryExcluded, "POS", "CLOSED", new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 22, 30, 0, TimeSpan.Zero), null, withChildren: false, reference: "20270101-001");
        await InsertOrderAsync(factory, open, "POS", "OPEN", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), null, null, withChildren: false, reference: "20240101-001");

        var reader = new SqliteAnnualArchiveEligibilityReader(factory, clock);
        var eligible = await reader.ReadEligibleAsync(2026);
        CollectionAssert.AreEquivalent(new[] { pos, hiboutik }, eligible.Select(item => item.OrderId).ToArray());
        Assert.IsTrue(eligible.All(item => item.ArchiveYear == 2026));
        Assert.AreEqual(OrderSourceType.Pos, eligible.Single(item => item.OrderId == pos).SourceType);
        Assert.AreEqual(OrderSourceType.HiboutikPaste, eligible.Single(item => item.OrderId == hiboutik).SourceType);

        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new SqliteAnnualArchiveStagingService(paths, clock, guard, factory);
        var result = (await service.StageNextArchiveAsync())!;

        CollectionAssert.AreEquivalent(new[] { pos, hiboutik }, result.OrderIds.ToArray());
        StringAssert.StartsWith(result.StagedDatabasePath, paths.TempDirectory);
        StringAssert.StartsWith(paths.ArchiveDirectory, paths.RootDirectory);
        Assert.AreNotEqual(paths.ArchiveDirectory, paths.TempDirectory);
        Assert.IsTrue(Directory.Exists(paths.ArchiveDirectory));
        Assert.IsFalse(File.Exists(Path.Combine(paths.ArchiveDirectory, "sushi81-archive-2026.db")));

        await using var archive = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(result.StagedDatabasePath);
        Assert.AreEqual("M12-WP1-1", await ScalarTextAsync(archive, "SELECT value FROM archive_metadata WHERE key='archive_format_version';"));
        Assert.AreEqual("2026", await ScalarTextAsync(archive, "SELECT value FROM archive_metadata WHERE key='archive_year';"));
        Assert.AreEqual("2", await ScalarTextAsync(archive, "SELECT value FROM archive_metadata WHERE key='expected_order_count';"));
        Assert.AreEqual(0L, await ScalarAsync(archive, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('categories','products','business_settings','export_batches');"));
        Assert.AreEqual(2L, await ScalarAsync(archive, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(1L, await ScalarAsync(archive, "SELECT COUNT(*) FROM order_items;"));
        Assert.AreEqual(1L, await ScalarAsync(archive, "SELECT COUNT(*) FROM order_item_adjustments;"));
        Assert.AreEqual(1L, await ScalarAsync(archive, "SELECT COUNT(*) FROM order_tax_breakdown;"));
        Assert.AreEqual(1L, await ScalarAsync(archive, "SELECT COUNT(*) FROM payment_adjustments;"));
        Assert.AreEqual("Plat historique", await ScalarTextAsync(archive, "SELECT product_name_snapshot FROM order_items WHERE order_id=$id;", pos));
        Assert.AreEqual("Sauce historique", await ScalarTextAsync(archive, "SELECT label_snapshot FROM order_item_adjustments;"));
        Assert.AreEqual("2026-12-31", await ScalarTextAsync(archive, "SELECT effective_business_date FROM payment_adjustments WHERE order_id=$id;", pos));
        Assert.AreEqual(4L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(exportCountBefore, await ScalarAsync(factory, "SELECT COUNT(*) FROM export_batches;"));
    }

    [TestMethod]
    public async Task JanuaryDoesNotAcquireAuthorityAndNonAuthoritativeStagingIsBlocked()
    {
        using var paths = new TestAppPaths();
        var januaryClock = new TestClock(new DateOnly(2027, 1, 31), TimeZoneInfo.Utc);
        var factory = await InitializeAsync(paths, januaryClock);
        using var januaryGuard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var januaryService = new SqliteAnnualArchiveStagingService(paths, januaryClock, januaryGuard, factory);
        Assert.IsNull(await januaryService.StageNextArchiveAsync());
        Assert.IsFalse(Directory.Exists(Path.Combine(paths.TempDirectory, "annual-archive")));

        januaryClock.BusinessDateValue = new DateOnly(2027, 2, 1);
        await Assert.ThrowsAsync<WriteAuthorityException>(() => januaryService.StageNextArchiveAsync());
        Assert.AreEqual(0L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task BuilderFailureLeavesLiveDatabaseUnchangedAndRetryIsSafe()
    {
        using var paths = new TestAppPaths();
        var clock = new TestClock(new DateOnly(2027, 2, 1), TimeZoneInfo.Utc);
        var factory = await InitializeAsync(paths, clock);
        var id = Guid.NewGuid();
        await InsertOrderAsync(factory, id, "POS", "CLOSED", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero), null, withChildren: true, reference: "20260101-001");
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var failing = new SqliteAnnualArchiveStagingService(paths, clock, guard, factory, stage => stage == "write" ? new IOException("synthetic builder failure") : null);
        await Assert.ThrowsAsync<IOException>(() => failing.StageNextArchiveAsync());
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
        Assert.IsFalse(File.Exists(Path.Combine(paths.ArchiveDirectory, "sushi81-archive-2026.db")));

        var retry = new SqliteAnnualArchiveStagingService(paths, clock, guard, factory);
        var result = (await retry.StageNextArchiveAsync())!;
        Assert.AreEqual(id, result.OrderIds.Single());
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
        Assert.IsTrue(File.Exists(result.StagedDatabasePath));
    }

    [TestMethod]
    public async Task ValidationRejectsWrongMetadataAfterStaging()
    {
        using var paths = new TestAppPaths();
        var clock = new TestClock(new DateOnly(2027, 2, 1), TimeZoneInfo.Utc);
        var factory = await InitializeAsync(paths, clock);
        var id = Guid.NewGuid();
        await InsertOrderAsync(factory, id, "POS", "CLOSED", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero), null, withChildren: false, reference: "20260101-001");
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var result = (await new SqliteAnnualArchiveStagingService(paths, clock, guard, factory).StageNextArchiveAsync())!;

        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = result.StagedDatabasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE archive_metadata SET value='2025' WHERE key='archive_year';";
            await command.ExecuteNonQueryAsync();
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteAnnualArchiveValidator(clock).ValidateAsync(result.StagedDatabasePath, factory.LiveDatabasePath, 2026, result.OrderIds));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
    }

    private static async Task<SqliteConnectionFactory> InitializeAsync(TestAppPaths paths, IBusinessClock clock)
    {
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        return factory;
    }

    private static async Task InsertOrderAsync(
        SqliteConnectionFactory factory,
        Guid id,
        string source,
        string status,
        DateTimeOffset createdAt,
        DateTimeOffset? closedAt,
        DateTimeOffset? cancelledAt,
        bool withChildren,
        string reference)
    {
        await using var connection = await factory.OpenLiveConnectionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO orders(order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents,order_reference,card_payment_ttc_cents,cash_payment_ttc_cents,source_total_ttc_cents) VALUES($id,$source,$status,$created,$updated,$closed,$cancelled,'RETRAIT','2026-12-31','12:00:00.0000000',0,'0612345678','Adresse historique','Commentaire historique',3000,1,1,'10',0,$reference,2000,1000,3000);";
            Add(command, "$id", id.ToString()); Add(command, "$source", source); Add(command, "$status", status);
            Add(command, "$created", createdAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); Add(command, "$updated", createdAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            Add(command, "$closed", closedAt is null ? null : closedAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); Add(command, "$cancelled", cancelledAt is null ? null : cancelledAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)); Add(command, "$reference", reference);
            await command.ExecuteNonQueryAsync();
        }

        if (!withChildren) return;
        var itemId = Guid.NewGuid();
        await ExecuteAsync(connection, "INSERT INTO order_items(order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents) VALUES($item,$order,0,$product,'P-HIST','Plat historique','Catégorie historique',1250,'10',1,2,2500,3000);", ("$item", itemId.ToString()), ("$order", id.ToString()), ("$product", Guid.NewGuid().ToString()));
        await ExecuteAsync(connection, "INSERT INTO order_item_adjustments(order_item_adjustment_id,order_item_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate) VALUES($adjustment,$item,0,'PREDEFINED_OPTION',$option,'Sauces','Sauce historique',250,'5.5');", ("$adjustment", Guid.NewGuid().ToString()), ("$item", itemId.ToString()), ("$option", Guid.NewGuid().ToString()));
        await ExecuteAsync(connection, "INSERT INTO order_tax_breakdown(order_tax_breakdown_id,order_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents) VALUES($tax,$order,'10',3000,273);", ("$tax", Guid.NewGuid().ToString()), ("$order", id.ToString()));
        await ExecuteAsync(connection, "INSERT INTO payment_adjustments(payment_adjustment_id,order_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at) VALUES($payment,$order,'CB',2000,'2026-12-31','2026-12-31T12:00:00.0000000+00:00','2026-12-31T12:01:00.0000000+00:00');", ("$payment", Guid.NewGuid().ToString()), ("$order", id.ToString()));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        foreach (var parameter in parameters) Add(command, parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static void Add(SqliteCommand command, string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return await ScalarAsync(connection, sql);
    }

    private static async Task<long> ScalarAsync(SqliteConnectionFactory factory, string sql, Guid id)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return await ScalarAsync(connection, sql, id);
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql, Guid? id = null)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString());
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql, Guid? id = null)
    {
        await using var command = connection.CreateCommand(); command.CommandText = sql;
        if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString());
        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed class TestClock(DateOnly businessDate, TimeZoneInfo businessTimeZone) : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2027, 2, 1, 8, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDateValue { get; set; } = businessDate;
        public DateOnly BusinessDate => BusinessDateValue;
        public TimeZoneInfo BusinessTimeZone { get; } = businessTimeZone;
    }
}
