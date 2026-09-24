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

    [TestMethod]
    public async Task ValidatorRejectsTruncatedArchiveAndPreservesLiveData()
    {
        using var paths = new TestAppPaths();
        var clock = new TestClock(new DateOnly(2027, 2, 1), TimeZoneInfo.Utc);
        var factory = await InitializeAsync(paths, clock);
        var id = Guid.NewGuid();
        await InsertOrderAsync(factory, id, "POS", "CLOSED", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero), null, withChildren: true, reference: "20260101-001");
        await InsertExportLedgerFactsAsync(factory, id);
        var exportLedgerBefore = await ReadExportLedgerDumpAsync(factory);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var result = (await new SqliteAnnualArchiveStagingService(paths, clock, guard, factory).StageNextArchiveAsync())!;

        File.WriteAllBytes(result.StagedDatabasePath, [0x53, 0x51, 0x4c, 0x69, 0x74, 0x65]);
        await Assert.ThrowsAsync<Exception>(() => new SqliteAnnualArchiveValidator(clock).ValidateAsync(result.StagedDatabasePath, factory.LiveDatabasePath, 2026, result.OrderIds));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(exportLedgerBefore, await ReadExportLedgerDumpAsync(factory));
    }

    [TestMethod]
    public async Task ValidatorRejectsMissingRequiredSchemaAndCountMismatch()
    {
        using var paths = new TestAppPaths();
        var clock = new TestClock(new DateOnly(2027, 2, 1), TimeZoneInfo.Utc);
        var factory = await InitializeAsync(paths, clock);
        var id = Guid.NewGuid();
        await InsertOrderAsync(factory, id, "POS", "CLOSED", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero), null, withChildren: false, reference: "20260101-001");
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var result = (await new SqliteAnnualArchiveStagingService(paths, clock, guard, factory).StageNextArchiveAsync())!;

        await using (var connection = await OpenWritableAsync(result.StagedDatabasePath))
        {
            await ExecuteAsync(connection, "DROP TABLE archive_metadata;");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteAnnualArchiveValidator(clock).ValidateAsync(result.StagedDatabasePath, factory.LiveDatabasePath, 2026, result.OrderIds));

        var second = (await new SqliteAnnualArchiveStagingService(paths, clock, guard, factory).StageNextArchiveAsync())!;
        await using (var connection = await OpenWritableAsync(second.StagedDatabasePath))
        {
            await ExecuteAsync(connection, "UPDATE archive_metadata SET value='2' WHERE key='expected_order_count';");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteAnnualArchiveValidator(clock).ValidateAsync(second.StagedDatabasePath, factory.LiveDatabasePath, 2026, second.OrderIds));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task ValidatorRejectsChildMismatchAndArchiveRetainsEquivalentSnapshotFactsWithoutCatalogue()
    {
        using var paths = new TestAppPaths();
        var clock = new TestClock(new DateOnly(2027, 2, 1), TimeZoneInfo.Utc);
        var factory = await InitializeAsync(paths, clock);
        var id = Guid.NewGuid();
        await InsertOrderAsync(factory, id, "POS", "CLOSED", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero), null, withChildren: true, reference: "20260101-001");
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new SqliteAnnualArchiveStagingService(paths, clock, guard, factory);
        var result = (await service.StageNextArchiveAsync())!;
        var snapshot = await ReadArchiveSnapshotAsync(result.StagedDatabasePath, id);

        Assert.AreEqual(id, snapshot.Id);
        Assert.AreEqual(OrderSourceType.Pos, snapshot.SourceType);
        Assert.AreEqual(OrderStatus.Closed, snapshot.Status);
        Assert.AreEqual("20260101-001", snapshot.Reference);
        Assert.AreEqual(Money.FromCents(3000), snapshot.TotalTtc);
        Assert.AreEqual(Money.FromCents(3000), snapshot.SourceTotalTtc);
        Assert.AreEqual(Money.FromCents(2000), snapshot.Snapshot.CardPaymentTtc);
        Assert.AreEqual(Money.FromCents(1000), snapshot.Snapshot.CashPaymentTtc);
        Assert.AreEqual("Adresse historique", snapshot.DeliveryAddress);
        Assert.AreEqual("Commentaire historique", snapshot.Comment);
        Assert.HasCount(1, snapshot.Items);
        Assert.AreEqual("Plat historique", snapshot.Items[0].ProductName);
        Assert.HasCount(1, snapshot.Items[0].Adjustments);
        Assert.AreEqual("Sauce historique", snapshot.Items[0].Adjustments[0].Label);
        Assert.HasCount(1, snapshot.TaxBreakdown);
        Assert.AreEqual(273, snapshot.TaxBreakdown[0].IncludedVatTtc.Cents);
        Assert.HasCount(1, snapshot.Payments);
        Assert.AreEqual("CB", snapshot.Payments[0].Bucket);
        Assert.AreEqual(2000L, snapshot.Payments[0].DeltaCents);
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));

        await using (var connection = await OpenWritableAsync(result.StagedDatabasePath))
        {
            await ExecuteAsync(connection, "DELETE FROM order_item_adjustments; DELETE FROM order_items;");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new SqliteAnnualArchiveValidator(clock).ValidateAsync(result.StagedDatabasePath, factory.LiveDatabasePath, 2026, result.OrderIds));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));
    }

    [TestMethod]
    public async Task RepeatedStagingIsIndependentAndM11LedgerStateRemainsByteEquivalent()
    {
        using var paths = new TestAppPaths();
        var clock = new TestClock(new DateOnly(2027, 2, 1), TimeZoneInfo.Utc);
        var factory = await InitializeAsync(paths, clock);
        var id = Guid.NewGuid();
        await InsertOrderAsync(factory, id, "POS", "CLOSED", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 12, 31, 12, 0, 0, TimeSpan.Zero), null, withChildren: true, reference: "20260101-001");
        await InsertExportLedgerFactsAsync(factory, id);
        var before = await ReadExportLedgerDumpAsync(factory);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var service = new SqliteAnnualArchiveStagingService(paths, clock, guard, factory);

        var first = (await service.StageNextArchiveAsync())!;
        var second = (await service.StageNextArchiveAsync())!;
        Assert.AreNotEqual(first.StagedDatabasePath, second.StagedDatabasePath);
        Assert.IsTrue(File.Exists(first.StagedDatabasePath));
        Assert.IsTrue(File.Exists(second.StagedDatabasePath));
        Assert.AreEqual(before, await ReadExportLedgerDumpAsync(factory));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));

        var failing = new SqliteAnnualArchiveStagingService(paths, clock, guard, factory, stage => stage == "validation" ? new InvalidDataException("synthetic validation failure") : null);
        var stagedDirectoriesBeforeFailure = Directory.GetDirectories(Path.Combine(paths.TempDirectory, "annual-archive"));
        await Assert.ThrowsAsync<InvalidDataException>(() => failing.StageNextArchiveAsync());
        CollectionAssert.AreEqual(stagedDirectoriesBeforeFailure, Directory.GetDirectories(Path.Combine(paths.TempDirectory, "annual-archive")));
        Assert.AreEqual(before, await ReadExportLedgerDumpAsync(factory));
        Assert.AreEqual(1L, await ScalarAsync(factory, "SELECT COUNT(*) FROM orders;"));

        var retry = (await service.StageNextArchiveAsync())!;
        Assert.IsTrue(File.Exists(retry.StagedDatabasePath));
        Assert.AreEqual(before, await ReadExportLedgerDumpAsync(factory));
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

    private static async Task InsertExportLedgerFactsAsync(SqliteConnectionFactory factory, Guid orderId)
    {
        var batchId = Guid.NewGuid().ToString();
        await using var connection = await factory.OpenLiveConnectionAsync();
        await ExecuteAsync(connection, "INSERT INTO export_batches(batch_id,schema_version,generated_at_utc,app_version,filter_start_date,filter_end_date,order_count,order_line_count,tax_breakdown_count,status,payload_json,payload_hash,completed_at_utc) VALUES($batch,'M11-1','2027-02-01T08:00:00.0000000+00:00','test','2026-01-01','2026-12-31',1,1,1,'SUCCESS','{\"order\":1}','hash-1','2027-02-01T08:01:00.0000000+00:00');", ("$batch", batchId));
        await ExecuteAsync(connection, "INSERT INTO export_batch_orders(batch_id,order_id,action,action_payload_hash,expected_previous_positive_hash) VALUES($batch,$order,'CREATE','action-hash',NULL);", ("$batch", batchId), ("$order", orderId.ToString()));
        await ExecuteAsync(connection, "INSERT INTO export_emissions(emission_id,batch_id,order_id,action,positive_snapshot_hash,positive_payload_json,fulfilment_date,settlement_date,emitted_at_utc) VALUES($emission,$batch,$order,'CREATE','positive-hash','{\"emission\":1}','2026-12-31','2026-12-31','2027-02-01T08:02:00.0000000+00:00');", ("$emission", Guid.NewGuid().ToString()), ("$batch", batchId), ("$order", orderId.ToString()));
    }

    private static async Task<string> ReadExportLedgerDumpAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return string.Join(
            "\n",
            await DumpQueryAsync(connection, "SELECT * FROM export_batches ORDER BY batch_id;"),
            await DumpQueryAsync(connection, "SELECT * FROM export_batch_orders ORDER BY batch_id,order_id;"),
            await DumpQueryAsync(connection, "SELECT * FROM export_emissions ORDER BY emission_id;"));
    }

    private static async Task<IReadOnlyList<string>> DumpQueryAsync(SqliteConnection connection, string sql)
    {
        var rows = new List<string>();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = Enumerable.Range(0, reader.FieldCount)
                .Select(index => reader.IsDBNull(index) ? "<NULL>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture) ?? string.Empty);
            rows.Add(string.Join("|", values));
        }
        return rows;
    }

    private static async Task<ArchiveSnapshotEvidence> ReadArchiveSnapshotAsync(string path, Guid orderId)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(path);
        await using var orderCommand = connection.CreateCommand();
        orderCommand.CommandText = "SELECT order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents,order_reference,card_payment_ttc_cents,cash_payment_ttc_cents,source_total_ttc_cents FROM orders WHERE order_id=$id;";
        orderCommand.Parameters.AddWithValue("$id", orderId.ToString());
        await using var orderReader = await orderCommand.ExecuteReaderAsync();
        Assert.IsTrue(await orderReader.ReadAsync());
        var snapshot = new OrderSnapshot(
            Guid.Parse(orderReader.GetString(0)),
            ParseSource(orderReader.GetString(1)),
            ParseStatus(orderReader.GetString(2)),
            DateTimeOffset.Parse(orderReader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(orderReader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            ReadNullableDateTime(orderReader, 5),
            ReadNullableDateTime(orderReader, 6),
            ParseFulfilment(orderReader.GetString(7)),
            DateOnly.ParseExact(orderReader.GetString(8), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            orderReader.IsDBNull(9) ? null : TimeOnly.ParseExact(orderReader.GetString(9), "HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            orderReader.GetInt64(10) == 1,
            ReadNullableString(orderReader, 11),
            ReadNullableString(orderReader, 12),
            ReadNullableString(orderReader, 13),
            Money.FromCents(orderReader.GetInt64(14)),
            orderReader.GetInt64(15) == 1,
            orderReader.GetInt64(16) == 1,
            orderReader.IsDBNull(17) ? null : decimal.Parse(orderReader.GetString(17), CultureInfo.InvariantCulture),
            Money.FromCents(orderReader.GetInt64(18)),
            [], [])
        {
            Reference = ReadNullableString(orderReader, 19) ?? string.Empty,
            CardPaymentTtc = Money.FromCents(orderReader.GetInt64(20)),
            CashPaymentTtc = Money.FromCents(orderReader.GetInt64(21)),
            SourceTotalTtc = orderReader.IsDBNull(22) ? null : Money.FromCents(orderReader.GetInt64(22))
        };
        await orderReader.CloseAsync();

        var items = new List<OrderItemSnapshot>();
        await using (var itemCommand = connection.CreateCommand())
        {
            itemCommand.CommandText = "SELECT order_item_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents FROM order_items WHERE order_id=$id ORDER BY line_position,order_item_id;";
            itemCommand.Parameters.AddWithValue("$id", orderId.ToString());
            await using var itemReader = await itemCommand.ExecuteReaderAsync();
            while (await itemReader.ReadAsync())
            {
                var itemId = Guid.Parse(itemReader.GetString(0));
                var adjustments = new List<OrderLineAdjustmentSnapshot>();
                await using var adjustmentCommand = connection.CreateCommand();
                adjustmentCommand.CommandText = "SELECT order_item_adjustment_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate FROM order_item_adjustments WHERE order_item_id=$id ORDER BY display_order,order_item_adjustment_id;";
                adjustmentCommand.Parameters.AddWithValue("$id", itemId.ToString());
                await using var adjustmentReader = await adjustmentCommand.ExecuteReaderAsync();
                while (await adjustmentReader.ReadAsync())
                {
                    adjustments.Add(new(
                        Guid.Parse(adjustmentReader.GetString(0)), adjustmentReader.GetInt32(1), ParseAdjustmentKind(adjustmentReader.GetString(2)),
                        adjustmentReader.IsDBNull(3) ? null : Guid.Parse(adjustmentReader.GetString(3)), ReadNullableString(adjustmentReader, 4), adjustmentReader.GetString(5),
                        Money.FromCents(adjustmentReader.GetInt64(6)), adjustmentReader.IsDBNull(7) ? null : decimal.Parse(adjustmentReader.GetString(7), CultureInfo.InvariantCulture)));
                }
                items.Add(new(itemId, itemReader.GetInt32(1), itemReader.IsDBNull(2) ? null : Guid.Parse(itemReader.GetString(2)), itemReader.GetString(3), itemReader.GetString(4), itemReader.GetString(5), Money.FromCents(itemReader.GetInt64(6)), decimal.Parse(itemReader.GetString(7), CultureInfo.InvariantCulture), itemReader.GetInt64(8) == 1, itemReader.GetInt32(9), Money.FromCents(itemReader.GetInt64(10)), Money.FromCents(itemReader.GetInt64(11)), adjustments));
            }
        }

        var taxes = new List<OrderTaxBreakdown>();
        await using (var taxCommand = connection.CreateCommand())
        {
            taxCommand.CommandText = "SELECT order_tax_breakdown_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents FROM order_tax_breakdown WHERE order_id=$id ORDER BY vat_rate,order_tax_breakdown_id;";
            taxCommand.Parameters.AddWithValue("$id", orderId.ToString());
            await using var taxReader = await taxCommand.ExecuteReaderAsync();
            while (await taxReader.ReadAsync()) taxes.Add(new(decimal.Parse(taxReader.GetString(1), CultureInfo.InvariantCulture), Money.FromCents(taxReader.GetInt64(2)), Money.FromCents(taxReader.GetInt64(3)), Guid.Parse(taxReader.GetString(0))));
        }

        var payments = new List<PaymentAdjustmentEvidence>();
        await using (var paymentCommand = connection.CreateCommand())
        {
            paymentCommand.CommandText = "SELECT payment_adjustment_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at FROM payment_adjustments WHERE order_id=$id ORDER BY recorded_at,payment_adjustment_id;";
            paymentCommand.Parameters.AddWithValue("$id", orderId.ToString());
            await using var paymentReader = await paymentCommand.ExecuteReaderAsync();
            while (await paymentReader.ReadAsync()) payments.Add(new(Guid.Parse(paymentReader.GetString(0)), paymentReader.GetString(1), paymentReader.GetInt64(2), paymentReader.GetString(3), paymentReader.GetString(4), paymentReader.GetString(5)));
        }

        return new(snapshot with { Items = items, TaxBreakdown = taxes }, payments);
    }

    private static async Task<SqliteConnection> OpenWritableAsync(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        await connection.OpenAsync();
        return connection;
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

    private static DateTimeOffset? ReadNullableDateTime(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : DateTimeOffset.Parse(reader.GetString(index), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string? ReadNullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    private static OrderSourceType ParseSource(string value) => value switch
    {
        "POS" => OrderSourceType.Pos,
        "HIBOUTIK_PASTE" => OrderSourceType.HiboutikPaste,
        _ => throw new InvalidDataException($"Unexpected archive source '{value}'.")
    };

    private static OrderStatus ParseStatus(string value) => value switch
    {
        "OPEN" => OrderStatus.Open,
        "CLOSED" => OrderStatus.Closed,
        "CANCELLED" => OrderStatus.Cancelled,
        _ => throw new InvalidDataException($"Unexpected archive status '{value}'.")
    };

    private static FulfilmentMode ParseFulfilment(string value) => value switch
    {
        "RETRAIT" => FulfilmentMode.Retrait,
        "LIVRAISON" => FulfilmentMode.Livraison,
        _ => throw new InvalidDataException($"Unexpected archive fulfilment '{value}'.")
    };

    private static OrderAdjustmentKind ParseAdjustmentKind(string value) => value switch
    {
        "PREDEFINED_OPTION" => OrderAdjustmentKind.PredefinedOption,
        "CUSTOM_ADJUSTMENT" => OrderAdjustmentKind.CustomAdjustment,
        _ => throw new InvalidDataException($"Unexpected archive adjustment kind '{value}'.")
    };

    private sealed record ArchiveSnapshotEvidence(OrderSnapshot Snapshot, IReadOnlyList<PaymentAdjustmentEvidence> Payments)
    {
        public Guid Id => Snapshot.Id;
        public OrderSourceType SourceType => Snapshot.SourceType;
        public OrderStatus Status => Snapshot.Status;
        public string Reference => Snapshot.Reference;
        public Money TotalTtc => Snapshot.TotalTtc;
        public Money SourceTotalTtc => Snapshot.SourceTotalTtc ?? Money.Zero;
        public string? DeliveryAddress => Snapshot.DeliveryAddress;
        public string? Comment => Snapshot.Comment;
        public IReadOnlyList<OrderItemSnapshot> Items => Snapshot.Items;
        public IReadOnlyList<OrderTaxBreakdown> TaxBreakdown => Snapshot.TaxBreakdown;
    }

    private sealed record PaymentAdjustmentEvidence(
        Guid Id,
        string Bucket,
        long DeltaCents,
        string EffectiveBusinessDate,
        string EffectiveAt,
        string RecordedAt);

    private sealed class TestClock(DateOnly businessDate, TimeZoneInfo businessTimeZone) : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2027, 2, 1, 8, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDateValue { get; set; } = businessDate;
        public DateOnly BusinessDate => BusinessDateValue;
        public TimeZoneInfo BusinessTimeZone { get; } = businessTimeZone;
    }
}
