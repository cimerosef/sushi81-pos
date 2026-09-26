using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Maintenance;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M13BusinessDataResetIntegrationTests
{
    private static readonly string[] ResetTables =
    [
        "export_emissions", "export_batch_orders", "export_batches",
        "annual_archive_order_proofs", "annual_archive_completions", "payment_adjustments",
        "order_item_adjustments", "order_items", "order_tax_breakdown", "orders",
        "order_reference_sequences", "options", "option_groups", "products", "categories"
    ];

    [TestMethod]
    public async Task PopulatedResetCreatesWalAwareRetainedBackupClearsOnlyBusinessTablesAndAdvancesRevisionOnce()
    {
        using var fixture = await Fixture.CreateAsync();
        await using var walSnapshot = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(fixture.Factory.LiveDatabasePath);
        await using var walSnapshotTransaction = (SqliteTransaction)await walSnapshot.BeginTransactionAsync();
        await using (var snapshotCommand = walSnapshot.CreateCommand())
        {
            snapshotCommand.Transaction = walSnapshotTransaction;
            snapshotCommand.CommandText = "SELECT COUNT(*) FROM orders;";
            Assert.AreEqual(0L, Convert.ToInt64(await snapshotCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture));
        }

        await fixture.PopulateBusinessDataAsync();
        var walPath = fixture.Paths.LiveDatabasePath + "-wal";
        Assert.IsTrue(File.Exists(walPath) && new FileInfo(walPath).Length > 0, "The held pre-write snapshot should keep newly committed synthetic rows in WAL.");
        var beforePreserved = await ReadPreservedStateAsync(fixture.Factory);
        var beforePersistentFiles = await ReadPersistentFileSnapshotAsync(fixture.Paths.RootDirectory);
        var beforeArchive = await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory);
        var beforeRevision = await ReadRevisionAsync(fixture.Factory);
        var preview = await fixture.Service().PreviewAsync();

        Assert.AreEqual(3L, preview.Orders);
        Assert.AreEqual(1L, preview.Products);
        Assert.AreEqual(1L, preview.Categories);
        Assert.AreEqual(1L, preview.OptionGroups);
        Assert.AreEqual(1L, preview.Options);
        Assert.AreEqual(2L, preview.ExportBatches);
        Assert.AreEqual(1L, preview.PreparedExportBatches);
        Assert.AreEqual(2L, preview.AnnualArchiveRecords);
        Assert.AreEqual(1, preview.ActiveArchiveYears);
        Assert.AreEqual(2, preview.ActiveArchiveFiles);
        Assert.IsTrue(preview.HasBusinessData);

        var notifier = new RecordingNotifier();
        var service = fixture.Service(notifier: notifier);
        var result = await service.ExecuteAsync(BusinessDataResetService.RequiredConfirmation, finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.Reset, result.Status);
        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(1, notifier.Calls, "A committed reset publishes the durable change exactly once.");
        Assert.AreEqual(beforeRevision + 1, await ReadRevisionAsync(fixture.Factory));
        Assert.AreEqual(beforePreserved, await ReadPreservedStateAsync(fixture.Factory));
        AssertArchiveSnapshotEqual(new Dictionary<string, string>(), await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
        foreach (var table in ResetTables)
            Assert.AreEqual(0L, await CountAsync(fixture.Factory, table), $"Reset table {table} was not cleared.");

        AssertArchiveSnapshotEqual(beforePersistentFiles, await ReadPersistentFileSnapshotAsync(fixture.Paths.RootDirectory));

        Assert.IsFalse(string.IsNullOrWhiteSpace(result.BackupId));
        var backupRoot = Path.Combine(fixture.Paths.RootDirectory, "MaintenanceBackups", result.BackupId!);
        var backupDatabase = Path.Combine(backupRoot, "live.db");
        Assert.IsTrue(File.Exists(backupDatabase));
        var manifestPath = Path.Combine(backupRoot, "manifest.json");
        Assert.IsTrue(File.Exists(manifestPath));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
        await using (var backupStream = File.OpenRead(backupDatabase))
            Assert.AreEqual(manifest.RootElement.GetProperty("DatabaseSha256").GetString(), Convert.ToHexString(await SHA256.HashDataAsync(backupStream)).ToLowerInvariant());
        var backupArchives = await ReadArchiveSnapshotAsync(Path.Combine(backupRoot, "Archive"));
        AssertArchiveSnapshotEqual(beforeArchive, backupArchives);
        await using var backup = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(backupDatabase);
        Assert.AreEqual("ok", await SqliteConnectionFactory.ExecuteScalarStringAsync(backup, "PRAGMA integrity_check;", CancellationToken.None));
        Assert.AreEqual(3L, await ScalarAsync(backup, "SELECT COUNT(*) FROM orders;"), "SQLite BackupDatabase must capture committed WAL state.");
        Assert.AreEqual(beforeRevision, await ScalarAsync(backup, "SELECT CAST(value AS INTEGER) FROM foundation_metadata WHERE key='business_data_revision';"));
        Assert.AreEqual(0L, await ScalarAsync(backup, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [TestMethod]
    public async Task PreviewOnlyLeavesBusinessStateArchiveAndPersistentFilesUnchanged()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.PopulateBusinessDataAsync();
        var beforeBusiness = await ReadResettableStateAsync(fixture.Factory);
        var beforePreserved = await ReadPreservedStateAsync(fixture.Factory);
        var beforeArchive = await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory);
        var beforePersistentFiles = await ReadPersistentFileSnapshotAsync(fixture.Paths.RootDirectory);

        var preview = await fixture.Service().PreviewAsync();

        Assert.AreEqual(3L, preview.Orders);
        Assert.AreEqual(1L, preview.Products);
        Assert.AreEqual(1L, preview.Categories);
        Assert.AreEqual(1L, preview.OptionGroups);
        Assert.AreEqual(1L, preview.Options);
        Assert.AreEqual(2L, preview.ExportBatches);
        Assert.AreEqual(1L, preview.PreparedExportBatches);
        Assert.AreEqual(2L, preview.AnnualArchiveRecords);
        Assert.AreEqual(1, preview.ActiveArchiveYears);
        Assert.AreEqual(2, preview.ActiveArchiveFiles);
        Assert.AreEqual(1L, preview.OrderReferenceSequences);
        Assert.IsTrue(preview.HasBusinessData);

        Assert.AreEqual(beforeBusiness, await ReadResettableStateAsync(fixture.Factory));
        Assert.AreEqual(beforePreserved, await ReadPreservedStateAsync(fixture.Factory));
        AssertArchiveSnapshotEqual(beforeArchive, await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
        AssertArchiveSnapshotEqual(beforePersistentFiles, await ReadPersistentFileSnapshotAsync(fixture.Paths.RootDirectory));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Paths.RootDirectory, "MaintenanceBackups")), "Preview must not create the retained pre-reset backup.");
    }

    [TestMethod]
    public async Task WrongTokenAndNonAuthoritativeGuardRefuseWithoutCreatingBackupOrMutating()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.PopulateBusinessDataAsync();
        var before = await ReadResettableStateAsync(fixture.Factory);
        var beforeArchive = await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory);
        var notifier = new RecordingNotifier();
        var service = fixture.Service(notifier: notifier);

        var wrongToken = await service.ExecuteAsync("reset", finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.FailedWithoutMutation, wrongToken.Status);
        Assert.AreEqual(before, await ReadResettableStateAsync(fixture.Factory));
        AssertArchiveSnapshotEqual(beforeArchive, await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
        Assert.AreEqual(0, notifier.Calls);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Paths.RootDirectory, "MaintenanceBackups")));

        foreach (var state in new[]
        {
            WriteAuthorityState.NonAuthoritativeReadOnly,
            WriteAuthorityState.Transitioning,
            WriteAuthorityState.RecoveryRequired
        })
        {
            fixture.Authority.SetState(state);
            await Assert.ThrowsAsync<WriteAuthorityException>(
                () => service.ExecuteAsync(BusinessDataResetService.RequiredConfirmation, finalConfirmation: true));
            await Assert.ThrowsAsync<WriteAuthorityException>(() => service.PreviewAsync());
            Assert.AreEqual(before, await ReadResettableStateAsync(fixture.Factory), $"Authority state {state} must not mutate the dataset.");
            AssertArchiveSnapshotEqual(beforeArchive, await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
            Assert.AreEqual(0, notifier.Calls);
        }
    }

    [TestMethod]
    public async Task TransactionFailureRollsBackEveryTableAndLeavesArchiveUntouched()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.PopulateBusinessDataAsync();
        var before = await ReadResettableStateAsync(fixture.Factory);
        var beforeArchive = await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory);
        var notifier = new RecordingNotifier();
        var failing = fixture.Service(
            notifier: notifier,
            failureInjector: stage => stage == "transaction.before-commit" ? new InvalidOperationException("synthetic transaction failure") : null);

        var result = await failing.ExecuteAsync(BusinessDataResetService.RequiredConfirmation, finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.FailedWithoutMutation, result.Status);
        Assert.AreEqual(before, await ReadResettableStateAsync(fixture.Factory));
        AssertArchiveSnapshotEqual(beforeArchive, await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
        Assert.AreEqual(0, notifier.Calls);
        Assert.IsTrue(Directory.Exists(Path.Combine(fixture.Paths.RootDirectory, "MaintenanceBackups", result.BackupId!)));
    }

    [TestMethod]
    public async Task ArchiveBackupStagingFailureBeforeTransactionLeavesDatabaseAndArchiveUntouched()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.PopulateBusinessDataAsync();
        var before = await ReadResettableStateAsync(fixture.Factory);
        var beforeArchive = await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory);
        var notifier = new RecordingNotifier();
        var failing = fixture.Service(
            notifier: notifier,
            failureInjector: stage => stage == "backup.complete" ? new IOException("synthetic archive staging failure") : null);

        var result = await failing.ExecuteAsync(BusinessDataResetService.RequiredConfirmation, finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.FailedWithoutMutation, result.Status);
        Assert.AreEqual(before, await ReadResettableStateAsync(fixture.Factory));
        AssertArchiveSnapshotEqual(beforeArchive, await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
        Assert.AreEqual(0, notifier.Calls);
    }

    [TestMethod]
    public async Task ArchiveRemovalFailureRestoresDatabaseAndNestedArchiveFiles()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.PopulateBusinessDataAsync();
        var before = await ReadResettableStateAsync(fixture.Factory);
        var beforeArchive = await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory);
        var notifier = new RecordingNotifier();
        var failing = fixture.Service(
            notifier: notifier,
            failureInjector: stage => stage == "archive.remove.after" ? new IOException("synthetic archive verification failure") : null);

        var result = await failing.ExecuteAsync(BusinessDataResetService.RequiredConfirmation, finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.FailedAndRestored, result.Status);
        Assert.AreEqual(before, await ReadResettableStateAsync(fixture.Factory));
        AssertArchiveSnapshotEqual(beforeArchive, await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
        Assert.AreEqual(0, notifier.Calls);
    }

    [TestMethod]
    public async Task AlreadyEmptyDatasetIsANoOpWithoutBackupOrRevisionChurn()
    {
        using var fixture = await Fixture.CreateAsync();
        var revision = await ReadRevisionAsync(fixture.Factory);
        var notifier = new RecordingNotifier();

        var result = await fixture.Service(notifier: notifier).ExecuteAsync(
            BusinessDataResetService.RequiredConfirmation,
            finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.AlreadyEmpty, result.Status);
        Assert.AreEqual(revision, await ReadRevisionAsync(fixture.Factory));
        Assert.AreEqual(0, notifier.Calls);
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Paths.RootDirectory, "MaintenanceBackups")));
    }

    [TestMethod]
    public async Task OrphanedOrderReferenceSequenceIsShownAndClearedInsteadOfBeingMisclassifiedAsEmpty()
    {
        using var fixture = await Fixture.CreateAsync();
        await using (var connection = await fixture.Factory.OpenLiveConnectionAsync())
            await ExecuteAsync(connection, "INSERT INTO order_reference_sequences(business_date,next_sequence) VALUES ('2026-09-26',2);");

        var revision = await ReadRevisionAsync(fixture.Factory);
        var service = fixture.Service();
        var preview = await service.PreviewAsync();
        Assert.AreEqual(1L, preview.OrderReferenceSequences);
        Assert.IsTrue(preview.HasBusinessData);

        var result = await service.ExecuteAsync(BusinessDataResetService.RequiredConfirmation, finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.Reset, result.Status);
        Assert.AreEqual(0L, await CountAsync(fixture.Factory, "order_reference_sequences"));
        Assert.AreEqual(revision + 1, await ReadRevisionAsync(fixture.Factory));
    }

    [TestMethod]
    public async Task CancellationAfterCommitStillCompletesDatabaseAndArchiveRecovery()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.PopulateBusinessDataAsync();
        var before = await ReadResettableStateAsync(fixture.Factory);
        var beforeArchive = await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory);
        var notifier = new RecordingNotifier();
        using var cancellation = new CancellationTokenSource();
        var failing = fixture.Service(
            notifier: notifier,
            failureInjector: stage =>
            {
                if (stage != "archive.remove.before") return null;
                cancellation.Cancel();
                return new OperationCanceledException("synthetic cancellation after database commit");
            });

        var result = await failing.ExecuteAsync(
            BusinessDataResetService.RequiredConfirmation,
            finalConfirmation: true,
            cancellationToken: cancellation.Token);

        Assert.AreEqual(BusinessDataResetStatus.FailedAndRestored, result.Status);
        Assert.AreEqual(before, await ReadResettableStateAsync(fixture.Factory));
        AssertArchiveSnapshotEqual(beforeArchive, await ReadArchiveSnapshotAsync(fixture.Paths.ArchiveDirectory));
        Assert.AreEqual(0, notifier.Calls);
    }

    [TestMethod]
    public async Task RecoveryNotificationFailureReportsCommittedResetForFailClosedUi()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.PopulateBusinessDataAsync();
        var notifier = new RecordingNotifier { ThrowOnNotify = true };

        var result = await fixture.Service(notifier: notifier).ExecuteAsync(
            BusinessDataResetService.RequiredConfirmation,
            finalConfirmation: true);

        Assert.AreEqual(BusinessDataResetStatus.ResetCommittedRecoveryNotificationFailed, result.Status);
        Assert.AreEqual(1, notifier.Calls);
        foreach (var table in ResetTables)
            Assert.AreEqual(0L, await CountAsync(fixture.Factory, table), $"Reset table {table} remained populated after commit.");
        Assert.AreEqual(8L, await ReadRevisionAsync(fixture.Factory));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Paths.RootDirectory, "MaintenanceBackups", result.BackupId!, "live.db")));
    }

    private static async Task<string> ReadResettableStateAsync(SqliteConnectionFactory factory)
    {
        var builder = new StringBuilder();
        foreach (var table in ResetTables)
            builder.Append(table).Append('=').Append(await CountAsync(factory, table)).AppendLine();
        builder.Append("revision=").Append(await ReadRevisionAsync(factory));
        return builder.ToString();
    }

    private static async Task<string> ReadPreservedStateAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        var output = new StringBuilder();
        output.Append(await ReadRowsAsync(connection, "SELECT * FROM business_settings ORDER BY singleton_id;")).AppendLine();
        output.Append(await ReadRowsAsync(connection, "SELECT * FROM schema_migrations ORDER BY version;")).AppendLine();
        output.Append(await ReadRowsAsync(connection, "SELECT key,value FROM foundation_metadata WHERE key <> 'business_data_revision' ORDER BY key;"));
        return output.ToString();
    }

    private static async Task<string> ReadRowsAsync(SqliteConnection connection, string sql)
    {
        var output = new StringBuilder();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            for (var index = 0; index < reader.FieldCount; index++)
                output.Append(reader.GetName(index)).Append('=').Append(reader.IsDBNull(index) ? "<null>" : Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture)).Append('\u001f');
            output.AppendLine();
        }
        return output.ToString();
    }

    private static async Task<Dictionary<string, string>> ReadArchiveSnapshotAsync(string root)
    {
        if (!Directory.Exists(root)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(root, file);
            await using var stream = File.OpenRead(file);
            result.Add(relative, Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant());
        }
        return result;
    }

    private static async Task<Dictionary<string, string>> ReadPersistentFileSnapshotAsync(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (firstSegment.Equals("Data", StringComparison.OrdinalIgnoreCase)
                || firstSegment.Equals("Archive", StringComparison.OrdinalIgnoreCase)
                || firstSegment.Equals("MaintenanceBackups", StringComparison.OrdinalIgnoreCase))
                continue;

            await using var stream = File.OpenRead(file);
            result.Add(relative, Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant());
        }
        return result;
    }

    private static void AssertArchiveSnapshotEqual(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        Assert.HasCount(expected.Count, actual);
        foreach (var entry in expected)
            Assert.AreEqual(entry.Value, actual[entry.Key], $"Archive entry {entry.Key} changed.");
    }

    private static async Task<long> ReadRevisionAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return await ScalarAsync(connection, "SELECT CAST(value AS INTEGER) FROM foundation_metadata WHERE key='business_data_revision';");
    }

    private static async Task<long> CountAsync(SqliteConnectionFactory factory, string table)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(factory.LiveDatabasePath);
        return await ScalarAsync(connection, $"SELECT COUNT(*) FROM {table};");
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private sealed class Fixture(TestPaths paths, SqliteConnectionFactory factory, WriteAuthorityGuard authority) : IDisposable
    {
        public TestPaths Paths { get; } = paths;
        public SqliteConnectionFactory Factory { get; } = factory;
        public WriteAuthorityGuard Authority { get; } = authority;

        public static async Task<Fixture> CreateAsync()
        {
            var paths = new TestPaths();
            var factory = new SqliteConnectionFactory(paths);
            await new SqliteMigrationRunner(factory, ProductionMigrations.All, new TestBusinessClock()).InitializeAsync();
            await using (var connection = await factory.OpenLiveConnectionAsync())
            {
                await ExecuteAsync(connection, "INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision','7'),('synthetic-preserved-metadata','keep-me');");
            }

            Directory.CreateDirectory(paths.ConfigDirectory);
            Directory.CreateDirectory(paths.RecoveryDirectory);
            Directory.CreateDirectory(Path.Combine(paths.RootDirectory, "OneDrive", "System"));
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "local-settings.json"), "{\"language\":\"fr-FR\",\"kitchenPrinter\":\"synthetic-kitchen\",\"customerPrinter\":\"synthetic-customer\",\"githubCredentialTarget\":\"synthetic-target\",\"oneDriveRoot\":\"synthetic-root\"}");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "authority-state.json"), "{\"deviceId\":\"synthetic-device-a\",\"lineageId\":\"synthetic-lineage\",\"generation\":4,\"phase\":\"Authoritative\"}");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "recovery-sequence.json"), "{\"sequence\":12}");
            await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "onedrive-checkpoint-watermark.json"), "{\"checkpoint\":\"synthetic-checkpoint\"}");
            await File.WriteAllTextAsync(Path.Combine(paths.RootDirectory, "OneDrive", "System", "membership.json"), "{\"deviceId\":\"synthetic-device-a\",\"lineageId\":\"synthetic-lineage\",\"generation\":4}");
            await File.WriteAllTextAsync(Path.Combine(paths.RecoveryDirectory, "retained.json"), "synthetic-recovery-history");
            return new Fixture(paths, factory, new WriteAuthorityGuard(WriteAuthorityState.Authoritative));
        }

        public async Task PopulateBusinessDataAsync()
        {
            await using (var connection = await Factory.OpenLiveConnectionAsync())
            {
                await ExecuteAsync(connection, "UPDATE business_settings SET pickup_discount_rate='0.15',pickup_discount_min_total_ttc_cents=1800,delivery_min_merchandise_total_ttc_cents=3500,delivery_fee_enabled=1,delivery_fee_amount_ttc_cents=250,updated_at_utc='synthetic-preserved-time' WHERE singleton_id=1;");
                await ExecuteAsync(connection, "INSERT INTO categories(category_id,name,normalized_name,created_at_utc,updated_at_utc) VALUES ('10000000-0000-0000-0000-000000000001','Synthetic category','synthetic category','2026-09-26T00:00:00Z','2026-09-26T00:00:00Z');");
                await ExecuteAsync(connection, "INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc) VALUES ('20000000-0000-0000-0000-000000000001','SYN-1','SYN-1','Synthetic product','10000000-0000-0000-0000-000000000001',1200,'10',1,1,1,'2026-09-26T00:00:00Z','2026-09-26T00:00:00Z');");
                await ExecuteAsync(connection, "INSERT INTO option_groups(option_group_id,product_id,name,selection_mode,is_required,min_selections,max_selections,display_order,created_at_utc,updated_at_utc) VALUES ('30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Synthetic options','SINGLE',0,NULL,NULL,0,'2026-09-26T00:00:00Z','2026-09-26T00:00:00Z');");
                await ExecuteAsync(connection, "INSERT INTO options(option_id,option_group_id,name,price_adjustment_ttc_cents,is_active,display_order,created_at_utc,updated_at_utc) VALUES ('40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','Synthetic extra',50,1,0,'2026-09-26T00:00:00Z','2026-09-26T00:00:00Z');");
                await ExecuteAsync(connection, """
                    INSERT INTO orders(order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents,order_reference,card_payment_ttc_cents,cash_payment_ttc_cents,source_total_ttc_cents) VALUES
                    ('50000000-0000-0000-0000-000000000001','POS','OPEN','2026-09-26T00:00:00Z','2026-09-26T00:00:00Z',NULL,NULL,'RETRAIT','2026-09-26','12:00:00',0,'0600000001',NULL,'synthetic open',1200,0,0,NULL,0,'20260926-00001',0,0,1200),
                    ('50000000-0000-0000-0000-000000000002','POS','CLOSED','2026-09-26T00:00:00Z','2026-09-26T00:00:00Z','2026-09-26T00:01:00Z',NULL,'RETRAIT','2026-09-26','12:01:00',0,'0600000002',NULL,'synthetic closed',1200,0,0,NULL,0,'20260926-00002',700,500,1200),
                    ('50000000-0000-0000-0000-000000000003','HIBOUTIK_PASTE','CANCELLED','2026-09-26T00:00:00Z','2026-09-26T00:00:00Z',NULL,'2026-09-26T00:02:00Z','LIVRAISON','2026-09-26',NULL,0,'0600000003','synthetic address','synthetic cancelled',1200,0,0,NULL,250,'20260926-00003',0,0,1200);
                    """);
                await ExecuteAsync(connection, "INSERT INTO order_items(order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents) VALUES ('60000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001',0,'20000000-0000-0000-0000-000000000001','SYN-1','Synthetic product','Synthetic category',1200,'10',1,1,1200,1200);");
                await ExecuteAsync(connection, "INSERT INTO order_item_adjustments(order_item_adjustment_id,order_item_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate) VALUES ('61000000-0000-0000-0000-000000000001','60000000-0000-0000-0000-000000000001',0,'CUSTOM_ADJUSTMENT',NULL,'Synthetic group','Synthetic adjustment',25,'10');");
                await ExecuteAsync(connection, "INSERT INTO order_tax_breakdown(order_tax_breakdown_id,order_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents) VALUES ('62000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','10',1200,109);");
                await ExecuteAsync(connection, "INSERT INTO payment_adjustments(payment_adjustment_id,order_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at) VALUES ('63000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000002','CB',-50,'2026-09-26','2026-09-26T00:03:00Z','2026-09-26T00:03:00Z');");
                await ExecuteAsync(connection, "INSERT INTO order_reference_sequences(business_date,next_sequence) VALUES ('2026-09-26',4);");
                await ExecuteAsync(connection, """
                    INSERT INTO export_batches(batch_id,schema_version,generated_at_utc,app_version,filter_start_date,filter_end_date,order_count,order_line_count,tax_breakdown_count,status,payload_json,payload_hash,completed_at_utc) VALUES
                    ('70000000-0000-0000-0000-000000000001','M11-1','2026-09-26T00:04:00Z','test',NULL,NULL,1,1,1,'SUCCESS','{}','synthetic-success','2026-09-26T00:05:00Z'),
                    ('70000000-0000-0000-0000-000000000002','M11-1','2026-09-26T00:06:00Z','test',NULL,NULL,1,1,0,'PREPARED','{}','synthetic-prepared',NULL);
                    """);
                await ExecuteAsync(connection, "INSERT INTO export_batch_orders(batch_id,order_id,action,action_payload_hash,expected_previous_positive_hash) VALUES ('70000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','CREATE','synthetic-action',NULL),('70000000-0000-0000-0000-000000000002','50000000-0000-0000-0000-000000000003','CANCEL','synthetic-prepared-action',NULL);");
                await ExecuteAsync(connection, "INSERT INTO export_emissions(emission_id,batch_id,order_id,action,positive_snapshot_hash,positive_payload_json,fulfilment_date,settlement_date,emitted_at_utc) VALUES ('71000000-0000-0000-0000-000000000001','70000000-0000-0000-0000-000000000001','50000000-0000-0000-0000-000000000001','CREATE','synthetic-positive','{}','2026-09-26','2026-09-26','2026-09-26T00:05:00Z');");
                await ExecuteAsync(connection, "INSERT INTO annual_archive_completions(archive_year,archive_format_version,archive_schema_version,archive_file_name,archive_order_count,archive_sha256,completed_at_utc) VALUES (2025,'synthetic-format','synthetic-schema','sushi81-archive-2025.db',1,'synthetic-archive-hash','2026-09-26T00:07:00Z');");
                await ExecuteAsync(connection, "INSERT INTO annual_archive_order_proofs(order_id,archive_year,archive_sha256,archive_completed_at_utc) VALUES ('80000000-0000-0000-0000-000000000001',2025,'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA','2026-09-26T00:07:00Z');");
            }

            var nested = Path.Combine(Paths.ArchiveDirectory, "nested");
            Directory.CreateDirectory(nested);
            await File.WriteAllTextAsync(Path.Combine(Paths.ArchiveDirectory, "sushi81-archive-2025.db"), "synthetic archive database bytes");
            await File.WriteAllTextAsync(Path.Combine(nested, "index.json"), "synthetic archive index");
        }

        public BusinessDataResetService Service(
            RecordingNotifier? notifier = null,
            Func<string, Exception?>? failureInjector = null)
        {
            var runner = new SqliteTransactionRunner(Factory);
            var store = new SqliteBusinessDataResetStore(Paths, Factory, runner, failureInjector: failureInjector);
            return new BusinessDataResetService(store, Authority, notifier ?? new RecordingNotifier());
        }

        public void Dispose()
        {
            Authority.Dispose();
            Paths.Dispose();
        }
    }

    private sealed class RecordingNotifier : IDurableChangeNotifier
    {
        public int Calls { get; private set; }
        public bool ThrowOnNotify { get; init; }

        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (ThrowOnNotify) throw new InvalidOperationException("Synthetic recovery notification failure.");
            return Task.CompletedTask;
        }
    }

    private sealed class TestBusinessClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 26);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M13.Reset.Tests", Guid.NewGuid().ToString("N"));
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
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath { get; }
        public string ArchiveDirectory => Path.Combine(RootDirectory, "Archive");
        public void EnsureInitialized() { }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
