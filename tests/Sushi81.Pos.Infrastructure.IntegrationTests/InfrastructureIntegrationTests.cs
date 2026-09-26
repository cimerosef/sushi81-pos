using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Transactions;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Ids;
using Sushi81.Pos.Infrastructure.Logging;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class InfrastructureIntegrationTests
{
    [TestMethod]
    public async Task LiveConnectionAppliesAndVerifiesRequiredPragmas()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        await using var connection = await factory.OpenLiveConnectionAsync();

        Assert.AreEqual(1L, await ScalarLongAsync(connection, "PRAGMA foreign_keys;"));
        Assert.AreEqual("wal", await ScalarStringAsync(connection, "PRAGMA journal_mode;"));
        Assert.AreEqual(2L, await ScalarLongAsync(connection, "PRAGMA synchronous;"));
        Assert.AreEqual(5000L, await ScalarLongAsync(connection, "PRAGMA busy_timeout;"));
    }

    [TestMethod]
    public async Task ConfigurationIsAtomicAndMalformedConfigurationFailsClearly()
    {
        using var paths = new TestAppPaths();
        var service = new JsonLocalConfigurationService(paths);
        await service.SaveAsync(new("zh-CN"));
        Assert.AreEqual("zh-CN", (await service.LoadAsync()).UiCulture);

        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "local-settings.json"), "{ bad json");
        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await service.LoadAsync());
        StringAssert.Contains(error.Message, "malformed");
        Assert.IsFalse(File.Exists(paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task MigrationsAreOrderedIdempotentAndRejectUnknownFutureVersion()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        var migrations = new[]
        {
            new SqliteMigration(1, "create-sentinel", "CREATE TABLE sentinel (value TEXT NOT NULL); INSERT INTO sentinel(value) VALUES ('preserved');"),
            new SqliteMigration(2, "add-column", "ALTER TABLE sentinel ADD COLUMN extra TEXT NULL;")
        };
        var runner = new SqliteMigrationRunner(factory, migrations, clock);
        await runner.InitializeAsync();
        await runner.InitializeAsync();

        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            Assert.AreEqual(2L, await ScalarLongAsync(connection, "SELECT MAX(version) FROM schema_migrations;"));
            Assert.AreEqual("preserved", await ScalarStringAsync(connection, "SELECT value FROM sentinel;"));
            await ExecuteAsync(connection, "INSERT INTO schema_migrations(version, name, applied_at_utc) VALUES (99, 'future', '2026-01-01T00:00:00.0000000+00:00');");
        }

        var futureError = await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await runner.InitializeAsync());
        StringAssert.Contains(futureError.Message, "newer");

        await using var preservedConnection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual("preserved", await ScalarStringAsync(preservedConnection, "SELECT value FROM sentinel;"));
        Assert.AreEqual(99L, await ScalarLongAsync(preservedConnection, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.AreEqual(1L, await ScalarLongAsync(preservedConnection, "SELECT COUNT(*) FROM schema_migrations WHERE version = 99 AND name = 'future';"));
    }

    [TestMethod]
    public async Task M13Wp5OrderingIndexesPreserveDataAndRemoveHotReadSorts()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        var beforeWp5 = ProductionMigrations.All.Where(migration => migration.Version <= 10).ToArray();
        await new SqliteMigrationRunner(factory, beforeWp5, clock).InitializeAsync();

        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            await ExecuteAsync(connection, """
                INSERT INTO categories(category_id,name,normalized_name,created_at_utc,updated_at_utc)
                VALUES ('d3bfbcb6-7303-4408-9e7d-3c396d8846d6','Synthetic category','synthetic category','2026-08-27T12:00:00.0000000+00:00','2026-08-27T12:00:00.0000000+00:00');
                """);
            await ExecuteAsync(connection, """
                INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc)
                VALUES ('a86b56cc-f0f7-4f1a-a237-c36de37968f1','SYN-001','SYN-001','Synthetic product','d3bfbcb6-7303-4408-9e7d-3c396d8846d6',1200,'0.10',1,1,0,'2026-08-27T12:00:00.0000000+00:00','2026-08-27T12:00:00.0000000+00:00');
                """);

            await ExecuteAsync(connection, CreateSyntheticSequenceCte() + """
                INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc)
                SELECT printf('10000000-0000-0000-0000-%012d',n),printf('SYN-%05d',n),printf('SYN-%05d',n),printf('Synthetic product %05d',n),
                       'd3bfbcb6-7303-4408-9e7d-3c396d8846d6',1200,'0.10',n%2,1,0,
                       '2026-08-27T12:00:00.0000000+00:00','2026-08-27T12:00:00.0000000+00:00'
                FROM numbers WHERE n<=10000;
                """);
            await ExecuteAsync(connection, CreateSyntheticSequenceCte() + """
                INSERT INTO orders(
                    order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,
                    fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,
                    telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,
                    pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents,order_reference,
                    card_payment_ttc_cents,cash_payment_ttc_cents,source_total_ttc_cents)
                SELECT printf('00000000-0000-0000-0000-%012d',n),'POS',CASE WHEN n%10=0 THEN 'OPEN' ELSE 'CLOSED' END,
                       '2026-08-27T12:00:00.0000000+00:00','2026-08-27T12:00:00.0000000+00:00',
                       CASE WHEN n%10=0 THEN NULL ELSE '2026-08-27T12:00:00.0000000+00:00' END,NULL,'RETRAIT',
                       date('2026-08-01',printf('+%d days',n%30)),
                       CASE WHEN n%7=0 THEN NULL ELSE printf('%02d:%02d',((n*7)/60)%24,(n*7)%60) END,
                       n%2,'0600000000','Synthetic address','Synthetic order comment',1250,0,0,NULL,0,
                       printf('20260827-%05d',n),0,0,1250
                FROM numbers WHERE n<=50000;
                """);
            await ExecuteAsync(connection, CreateSyntheticSequenceCte() + """
                INSERT INTO payment_adjustments(payment_adjustment_id,order_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at)
                SELECT printf('50000000-0000-0000-0000-%012d',n),printf('00000000-0000-0000-0000-%012d',n),
                       CASE WHEN n%2=0 THEN 'CB' ELSE 'ESPECE' END,50,date('2026-08-01',printf('+%d days',n%30)),
                       '2026-08-27T12:00:00.0000000+00:00','2026-08-27T12:00:00.0000000+00:00'
                FROM numbers WHERE n<=50000;
                """);
            await ExecuteAsync(connection, CreateSyntheticSequenceCte() + """
                INSERT INTO order_items(order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents)
                SELECT printf('60000000-0000-0000-0000-%012d',n),printf('00000000-0000-0000-0000-%012d',n),0,NULL,
                       'SYN-00001','Synthetic product','Synthetic category',100,'0.10',1,1,100,100
                FROM numbers WHERE n<=50000;
                """);
            await ExecuteAsync(connection, CreateSyntheticSequenceCte() + """
                INSERT INTO export_batches(batch_id,schema_version,generated_at_utc,app_version,filter_start_date,filter_end_date,order_count,order_line_count,tax_breakdown_count,status,payload_json,payload_hash,completed_at_utc)
                SELECT printf('20000000-0000-0000-0000-%012d',n),'1.0',printf('2026-08-%02dT12:00:00.0000000+00:00',1+(n%27)),
                       '1.0.0',NULL,NULL,1,1,0,'SUCCESS','{}','synthetic-hash','2026-08-27T12:00:00.0000000+00:00'
                FROM numbers WHERE n<=2000;
                """);
            await ExecuteAsync(connection, CreateSyntheticSequenceCte() + """
                INSERT INTO export_batch_orders(batch_id,order_id,action,action_payload_hash,expected_previous_positive_hash)
                SELECT printf('20000000-0000-0000-0000-%012d',n),printf('00000000-0000-0000-0000-%012d',n),'CREATE','synthetic-hash',NULL
                FROM numbers WHERE n<=2000;
                """);
            await ExecuteAsync(connection, CreateSyntheticSequenceCte() + """
                INSERT INTO export_emissions(emission_id,batch_id,order_id,action,positive_snapshot_hash,positive_payload_json,fulfilment_date,settlement_date,emitted_at_utc)
                SELECT printf('30000000-0000-0000-0000-%012d',n),printf('20000000-0000-0000-0000-%012d',n),
                       printf('00000000-0000-0000-0000-%012d',n),'CREATE','synthetic-hash','{}','2026-08-27','2026-08-27','2026-08-27T12:00:00.0000000+00:00'
                FROM numbers WHERE n<=2000;
                """);

            Assert.AreEqual(10001L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM products;"));
            Assert.AreEqual(50000L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM orders;"));
            Assert.AreEqual(50000L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM payment_adjustments;"));
            Assert.AreEqual(2000L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM export_batches;"));

            var productPlan = await ReadQueryPlanDetailsAsync(connection, """
                EXPLAIN QUERY PLAN
                SELECT p.product_id,p.code,p.name,c.name
                FROM products p JOIN categories c ON c.category_id=p.category_id
                ORDER BY p.code COLLATE NOCASE,p.product_id;
                """);
            var orderPlan = await ReadQueryPlanDetailsAsync(connection, """
                EXPLAIN QUERY PLAN
                SELECT order_id FROM orders
                WHERE planned_fulfilment_date='2026-08-27'
                ORDER BY planned_fulfilment_time IS NULL,planned_fulfilment_time,order_id;
                """);
            StringAssert.Contains(string.Join("\n", productPlan), "TEMP B-TREE FOR ORDER BY");
            StringAssert.Contains(string.Join("\n", orderPlan), "TEMP B-TREE FOR ORDER BY");
        }

        var snapshot = new RecordingSnapshotService();
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, snapshot).InitializeAsync();

        await using var migrated = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(11L, await ScalarLongAsync(migrated, "SELECT MAX(version) FROM schema_migrations;"));
        Assert.HasCount(1, snapshot.Changes);
        Assert.AreEqual(10L, snapshot.Changes[0].Sequence);
        Assert.AreEqual("Synthetic category", await ScalarStringAsync(migrated, "SELECT name FROM categories WHERE category_id='d3bfbcb6-7303-4408-9e7d-3c396d8846d6';"));
        Assert.AreEqual("Synthetic product", await ScalarStringAsync(migrated, "SELECT name FROM products WHERE product_id='a86b56cc-f0f7-4f1a-a237-c36de37968f1';"));
        Assert.AreEqual(10001L, await ScalarLongAsync(migrated, "SELECT COUNT(*) FROM products;"));
        Assert.AreEqual(50000L, await ScalarLongAsync(migrated, "SELECT COUNT(*) FROM orders;"));
        Assert.AreEqual(50000L, await ScalarLongAsync(migrated, "SELECT COUNT(*) FROM payment_adjustments;"));
        Assert.AreEqual(2000L, await ScalarLongAsync(migrated, "SELECT COUNT(*) FROM export_batches;"));
        Assert.AreEqual(1L, await ScalarLongAsync(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_products_code_nocase_product_id';"));
        Assert.AreEqual(1L, await ScalarLongAsync(migrated, "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_orders_planned_date_nulls_last';"));

        var migratedProductPlan = await ReadQueryPlanDetailsAsync(migrated, """
            EXPLAIN QUERY PLAN
            SELECT p.product_id,p.code,p.name,c.name
            FROM products p JOIN categories c ON c.category_id=p.category_id
            ORDER BY p.code COLLATE NOCASE,p.product_id;
            """);
        var migratedOrderPlan = await ReadQueryPlanDetailsAsync(migrated, """
            EXPLAIN QUERY PLAN
            SELECT order_id FROM orders
            WHERE planned_fulfilment_date='2026-08-27'
            ORDER BY planned_fulfilment_time IS NULL,planned_fulfilment_time,order_id;
            """);
        var productPlanText = string.Join("\n", migratedProductPlan);
        var orderPlanText = string.Join("\n", migratedOrderPlan);
        StringAssert.Contains(productPlanText, "ix_products_code_nocase_product_id");
        StringAssert.Contains(orderPlanText, "ix_orders_planned_date_nulls_last");
        Assert.IsFalse(productPlanText.Contains("TEMP B-TREE FOR ORDER BY", StringComparison.OrdinalIgnoreCase), productPlanText);
        Assert.IsFalse(orderPlanText.Contains("TEMP B-TREE FOR ORDER BY", StringComparison.OrdinalIgnoreCase), orderPlanText);

        var activeProductLookupPlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT product_id FROM products WHERE normalized_code='SYN-00042' AND is_active=1;");
        StringAssert.Contains(string.Join("\n", activeProductLookupPlan), "normalized_code=?");
        var orderReferencePlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT order_id FROM orders WHERE order_reference='20260827-00042';");
        StringAssert.Contains(string.Join("\n", orderReferencePlan), "ux_orders_reference");
        var paymentSummaryPlan = await ReadQueryPlanDetailsAsync(migrated, """
            EXPLAIN QUERY PLAN
            SELECT COALESCE(SUM(pa.delta_cents),0)
            FROM payment_adjustments pa JOIN orders o ON o.order_id=pa.order_id
            WHERE pa.effective_business_date='2026-08-27' AND o.source_type='POS' AND o.status <> 'CANCELLED';
            """);
        StringAssert.Contains(string.Join("\n", paymentSummaryPlan), "ix_payment_adjustments_effective");

        var dashboardPlan = await ReadQueryPlanDetailsAsync(migrated, """
            EXPLAIN QUERY PLAN
            SELECT order_id FROM orders
            WHERE status='OPEN' AND planned_fulfilment_date='2026-08-27'
              AND advance_order_marker=1 AND source_type='POS';
            """);
        StringAssert.Contains(string.Join("\n", dashboardPlan), "ix_orders_lifecycle_date");

        var telephoneContainsPlan = await ReadQueryPlanDetailsAsync(migrated, """
            EXPLAIN QUERY PLAN
            SELECT order_id FROM orders
            WHERE replace(replace(replace(replace(replace(replace(COALESCE(telephone,''),' ',''),'-',''),'.',''),'(',''),')',''),'+','') LIKE '%612345678%';
            """);
        var telephoneContainsText = string.Join("\n", telephoneContainsPlan);
        StringAssert.Contains(telephoneContainsText, "SCAN orders");
        Assert.IsFalse(telephoneContainsText.Contains("TEMP B-TREE", StringComparison.OrdinalIgnoreCase), telephoneContainsText);

        var exportHistoryPlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT batch_id FROM export_batches WHERE status='SUCCESS' ORDER BY generated_at_utc DESC,batch_id DESC;");
        StringAssert.Contains(string.Join("\n", exportHistoryPlan), "ix_export_batches_status_time");
        var exportRegenerationPlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT payload_json FROM export_batches WHERE batch_id='20000000-0000-0000-0000-000000000042';");
        StringAssert.Contains(string.Join("\n", exportRegenerationPlan), "batch_id=?");
        var compactionCandidatesPlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT batch_id,order_count FROM export_batches WHERE status='SUCCESS' ORDER BY generated_at_utc DESC,batch_id DESC;");
        var compactionCandidatesText = string.Join("\n", compactionCandidatesPlan);
        StringAssert.Contains(compactionCandidatesText, "ix_export_batches_status_time");
        Assert.IsFalse(compactionCandidatesText.Contains("TEMP B-TREE FOR ORDER BY", StringComparison.OrdinalIgnoreCase), compactionCandidatesText);
        var batchDependencyPlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT order_id,batch_id FROM export_batch_orders WHERE order_id='00000000-0000-0000-0000-000000000042';");
        StringAssert.Contains(string.Join("\n", batchDependencyPlan), "ix_export_batch_orders_order");
        var archiveProofPlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT archive_year FROM annual_archive_order_proofs WHERE order_id='00000000-0000-0000-0000-000000000042';");
        StringAssert.Contains(string.Join("\n", archiveProofPlan), "order_id=?");
        var archiveDiscoveryPlan = await ReadQueryPlanDetailsAsync(migrated, "EXPLAIN QUERY PLAN SELECT order_id FROM annual_archive_order_proofs WHERE archive_year=2025 ORDER BY order_id;");
        StringAssert.Contains(string.Join("\n", archiveDiscoveryPlan), "ix_annual_archive_order_proofs_year");
        var archiveSearchPlan = await ReadQueryPlanDetailsAsync(migrated, """
            EXPLAIN QUERY PLAN
            SELECT o.order_id FROM orders o
            WHERE o.planned_fulfilment_date>='2026-08-10' AND o.planned_fulfilment_date<='2026-08-27'
              AND EXISTS (SELECT 1 FROM order_items si WHERE si.order_id=o.order_id AND si.product_name_snapshot LIKE '%Synthetic%')
            ORDER BY o.planned_fulfilment_date,o.planned_fulfilment_time IS NULL,o.planned_fulfilment_time,o.order_id;
            """);
        var archiveSearchText = string.Join("\n", archiveSearchPlan);
        StringAssert.Contains(archiveSearchText, "ix_orders_planned_date_nulls_last");
        StringAssert.Contains(archiveSearchText, "ix_order_items_order");
        Assert.IsFalse(archiveSearchText.Contains("TEMP B-TREE FOR ORDER BY", StringComparison.OrdinalIgnoreCase), archiveSearchText);
    }

    [TestMethod]
    public async Task FailedMigrationRollsBackAndNeverReplacesSentinelDatabase()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        var initial = new SqliteMigrationRunner(factory, [new SqliteMigration(1, "create-sentinel", "CREATE TABLE sentinel (value TEXT NOT NULL); INSERT INTO sentinel(value) VALUES ('safe');")], clock);
        await initial.InitializeAsync();
        var originalLength = new FileInfo(paths.LiveDatabasePath).Length;

        var failing = new SqliteMigrationRunner(factory,
        [
            new SqliteMigration(1, "create-sentinel", "CREATE TABLE sentinel (value TEXT NOT NULL); INSERT INTO sentinel(value) VALUES ('safe');"),
            new SqliteMigration(2, "fail-after-write", "INSERT INTO sentinel(value) VALUES ('should-rollback'); THIS IS INVALID SQL;")
        ], clock);

        await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await failing.InitializeAsync());
        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sentinel;"));
        Assert.IsGreaterThanOrEqualTo(originalLength, new FileInfo(paths.LiveDatabasePath).Length);
    }

    [TestMethod]
    public async Task ExistingDatabaseUpgradeCreatesValidatedPreMigrationSnapshot()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, [new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);")], clock).InitializeAsync();
        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await new SqliteMigrationRunner(factory,
        [
            new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);"),
            new SqliteMigration(2, "second", "CREATE TABLE second_table (id INTEGER PRIMARY KEY);")
        ], clock, snapshots).InitializeAsync();

        var metadata = Directory.GetFiles(paths.RecoveryDirectory, "metadata.json", SearchOption.AllDirectories);
        Assert.HasCount(1, metadata);
        var database = Path.Combine(Path.GetDirectoryName(metadata[0])!, "snapshot.db");
        await SqliteLocalRecoverySnapshotService.VerifyAsync(database, metadata[0]);
    }

    [TestMethod]
    public async Task ExistingUpgradeWithoutSnapshotServiceBlocksBeforeSchemaChanges()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await new SqliteMigrationRunner(factory, [new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);")], clock).InitializeAsync();

        var upgrade = new SqliteMigrationRunner(factory,
        [
            new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);"),
            new SqliteMigration(2, "second", "CREATE TABLE second_table (id INTEGER PRIMARY KEY);")
        ], clock);

        await Assert.ThrowsAsync<DatabaseMigrationException>(async () => await upgrade.InitializeAsync());
        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(0L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'second_table';"));
    }

    [TestMethod]
    public async Task ConcurrentMigrationRunnersCannotInterleaveAppliedHistory()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        SqliteMigration[] migrations = [new SqliteMigration(1, "first", "CREATE TABLE first_table (id INTEGER PRIMARY KEY);")];
        var first = new SqliteMigrationRunner(factory, migrations, new FixedClock());
        var second = new SqliteMigrationRunner(factory, migrations, new FixedClock());

        var attempts = await Task.WhenAll(RunWithoutThrowingAsync(first), RunWithoutThrowingAsync(second));
        Assert.IsTrue(attempts.Any(result => result));
        await using var connection = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(1L, await ScalarLongAsync(connection, "SELECT COUNT(*) FROM schema_migrations WHERE version = 1 AND name = 'first';"));
    }

    [TestMethod]
    public async Task TransactionRunnerCommitsAllRowsOrRollsEverythingBack()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        await using (var setup = await factory.OpenLiveConnectionAsync())
        {
            await ExecuteAsync(setup, "CREATE TABLE test_parent(id INTEGER PRIMARY KEY); CREATE TABLE test_child(id INTEGER PRIMARY KEY, parent_id INTEGER NOT NULL REFERENCES test_parent(id));");
        }

        var runner = new SqliteTransactionRunner(factory);
        await runner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = (SqliteApplicationTransaction)transaction;
            await ExecuteAsync(sqlite.Connection, "INSERT INTO test_parent(id) VALUES (1); INSERT INTO test_child(id, parent_id) VALUES (1, 1);", sqlite.Transaction, token);
        });

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await runner.ExecuteAsync(async (transaction, token) =>
        {
            var sqlite = (SqliteApplicationTransaction)transaction;
            await ExecuteAsync(sqlite.Connection, "INSERT INTO test_parent(id) VALUES (2);", sqlite.Transaction, token);
            throw new InvalidOperationException("synthetic rollback");
        }));

        await using var verification = await factory.OpenLiveConnectionAsync();
        Assert.AreEqual(1L, await ScalarLongAsync(verification, "SELECT COUNT(*) FROM test_parent;"));
        Assert.AreEqual(1L, await ScalarLongAsync(verification, "SELECT COUNT(*) FROM test_child;"));
    }

    [TestMethod]
    public async Task SnapshotIsWalSafeValidatedAndRetainsFiveUnits()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            await ExecuteAsync(connection, "CREATE TABLE committed_values(value TEXT NOT NULL); INSERT INTO committed_values(value) VALUES ('committed');");
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();
            await ExecuteAsync(connection, "INSERT INTO committed_values(value) VALUES ('rolled-back');", transaction);
            await transaction.RollbackAsync();
        }

        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        RecoverySnapshotResult last = null!;
        for (var sequence = 1; sequence <= 6; sequence++)
        {
            last = await snapshots.CreateAsync(new DurableChange(sequence, clock.UtcNow));
        }

        await using var snapshotConnection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(last.DatabasePath);
        Assert.AreEqual(1L, await ScalarLongAsync(snapshotConnection, "SELECT COUNT(*) FROM committed_values;"));
        Assert.HasCount(5, Directory.GetFiles(paths.RecoveryDirectory, "metadata.json", SearchOption.AllDirectories));

        await File.AppendAllTextAsync(last.DatabasePath, "tamper");
        await Assert.ThrowsAsync<RecoverySnapshotValidationException>(async () => await SqliteLocalRecoverySnapshotService.VerifyAsync(last.DatabasePath, last.MetadataPath));
    }

    [TestMethod]
    public async Task IncompleteRecoveryUnitAndStagingArtifactsAreNeverValid()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        var incompleteDirectory = Path.Combine(paths.RecoveryDirectory, "recovery-incomplete");
        Directory.CreateDirectory(incompleteDirectory);
        await File.WriteAllTextAsync(Path.Combine(incompleteDirectory, "metadata.json"), "{}");
        await Assert.ThrowsAsync<RecoverySnapshotValidationException>(async () => await SqliteLocalRecoverySnapshotService.VerifyAsync(
            Path.Combine(incompleteDirectory, "snapshot.db"),
            Path.Combine(incompleteDirectory, "metadata.json")));

        var stagingDirectory = Path.Combine(paths.TempDirectory, "recovery-staging.staging");
        Directory.CreateDirectory(stagingDirectory);
        await File.WriteAllTextAsync(Path.Combine(stagingDirectory, "snapshot.db"), "not a database");
        Assert.IsEmpty(Directory.GetDirectories(paths.RecoveryDirectory, "recovery-staging*", SearchOption.TopDirectoryOnly));
    }

    [TestMethod]
    public async Task FailedSixthSnapshotPreservesPreviousFiveValidUnits()
    {
        using var paths = new TestAppPaths();
        var factory = new SqliteConnectionFactory(paths);
        var clock = new FixedClock();
        await using (var connection = await factory.OpenLiveConnectionAsync())
        {
            await ExecuteAsync(connection, "CREATE TABLE snapshot_source(value TEXT NOT NULL);");
        }

        var snapshots = new FailOnSixthSnapshotService(paths, factory, clock);
        for (var sequence = 1; sequence <= 5; sequence++)
        {
            await snapshots.CreateAsync(new DurableChange(sequence, clock.UtcNow));
        }

        await Assert.ThrowsAsync<IOException>(async () => await snapshots.CreateAsync(new DurableChange(6, clock.UtcNow)));
        var metadata = Directory.GetFiles(paths.RecoveryDirectory, "metadata.json", SearchOption.AllDirectories);
        Assert.HasCount(5, metadata);
        foreach (var metadataPath in metadata)
        {
            await SqliteLocalRecoverySnapshotService.VerifyAsync(Path.Combine(Path.GetDirectoryName(metadataPath)!, "snapshot.db"), metadataPath);
        }
    }

    [TestMethod]
    public async Task RecoverySchedulerCoalescesAndFlushesCommittedChangesOnly()
    {
        var snapshots = new RecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        scheduler.NotifyCommitted(new DurableChange(2, DateTimeOffset.UtcNow));
        await scheduler.FlushAsync();

        Assert.HasCount(1, snapshots.Changes);
        Assert.AreEqual(2L, snapshots.Changes[0].Sequence);
    }

    [TestMethod]
    public async Task RecoverySchedulerDebouncesAndNeverRunsSnapshotsConcurrently()
    {
        var snapshots = new DelayedRecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        scheduler.NotifyCommitted(new DurableChange(2, DateTimeOffset.UtcNow));
        await Task.Delay(TimeSpan.FromSeconds(4));

        Assert.HasCount(1, snapshots.Changes);
        Assert.AreEqual(2L, snapshots.Changes[0].Sequence);
        Assert.AreEqual(1, snapshots.MaximumConcurrentCalls);
    }

    [TestMethod]
    public async Task RecoverySchedulerFailureDoesNotClaimASnapshot()
    {
        var snapshots = new FailingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        await Assert.ThrowsAsync<IOException>(async () => await scheduler.FlushAsync());
        Assert.AreEqual(1, snapshots.Attempts);
    }

    [TestMethod]
    public async Task RecoverySchedulerDoesNotLoseACommitDuringAnActiveSnapshot()
    {
        var snapshots = new ActiveSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        scheduler.NotifyCommitted(new DurableChange(1, DateTimeOffset.UtcNow));
        var flush = scheduler.FlushAsync();
        await snapshots.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scheduler.NotifyCommitted(new DurableChange(2, DateTimeOffset.UtcNow));
        snapshots.Release();
        await flush;

        Assert.HasCount(2, snapshots.Changes);
        CollectionAssert.AreEqual(new long[] { 1, 2 }, snapshots.Changes.Select(change => change.Sequence).ToArray());
    }

    [TestMethod]
    public void AuthorityAndIdsFailClosedAndGenerateOpaqueUniqueValues()
    {
        var guard = new WriteAuthorityGuard();
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
        guard.SetState(WriteAuthorityState.Authoritative);
        guard.RequireWriteAuthority();
        var ids = new GuidV7IdGenerator(TimeProvider.System);
        Assert.AreNotEqual(ids.NewId(), ids.NewId());
    }

    [TestMethod]
    public async Task AuthorityStateChangeWaitsForInFlightWriteScopeToFinish()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var scope = await guard.EnterWriteScopeAsync();
        var stateChange = Task.Run(() => guard.SetState(WriteAuthorityState.Transitioning));
        var completed = await Task.WhenAny(stateChange, Task.Delay(TimeSpan.FromSeconds(1)));

        Assert.AreNotSame(stateChange, completed, "A transition must not pass an in-flight business write scope.");
        await scope.DisposeAsync();
        await stateChange;
        Assert.AreEqual(WriteAuthorityState.Transitioning, guard.State);
    }

    [TestMethod]
    public async Task AuthorityBootstrapIsDurableAndMissingStateFailsClosed()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }
        var store = new JsonAuthorityStateStore(paths);
        var legacyBootstrapEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        var firstGuard = new WriteAuthorityGuard();
        var first = await new AuthorityStateCoordinator(store, firstGuard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);

        Assert.AreEqual(WriteAuthorityState.Authoritative, first.State);
        Assert.AreEqual(WriteAuthorityState.Authoritative, firstGuard.State);
        Assert.IsTrue(await store.HasBootstrapMarkerAsync());
        Assert.IsTrue(await store.HasBootstrapAnchorAsync());

        File.Delete(Path.Combine(paths.ConfigDirectory, "authority-state.json"));
        var secondGuard = new WriteAuthorityGuard();
        var second = await new AuthorityStateCoordinator(store, secondGuard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, second.State);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, secondGuard.State);
        Assert.Throws<WriteAuthorityException>(secondGuard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task MissingAuthorityStateWithoutLegacyEvidenceFailsClosedInsteadOfRebootstrapping()
    {
        using var paths = new TestAppPaths();
        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(new JsonAuthorityStateStore(paths), guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(false);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.IsFalse(await new JsonAuthorityStateStore(paths).HasBootstrapMarkerAsync());
        Assert.IsFalse(await new JsonAuthorityStateStore(paths).HasBootstrapAnchorAsync());
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task EstablishedAuthorityWithoutIndependentAnchorFailsClosed()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var store = new JsonAuthorityStateStore(paths);
        var legacyBootstrapEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        await new AuthorityStateCoordinator(store, new WriteAuthorityGuard(), new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);
        File.Delete(Path.Combine(paths.DataDirectory, "authority-bootstrap.anchor"));

        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(store, guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.IsNotNull(result.Error);
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task EstablishedAuthorityWithoutPreExistingLiveDatabaseIsBlockedBeforeReplacementCreation()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var store = new JsonAuthorityStateStore(paths);
        var legacyEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        await new AuthorityStateCoordinator(store, new WriteAuthorityGuard(), new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance)
            .InitializeAsync(legacyEvidence);
        File.Delete(paths.LiveDatabasePath);

        await Assert.ThrowsAsync<AuthorityStartupBlockedException>(() => AuthorityStartupPreflight.CaptureAsync(paths, store));
        Assert.IsFalse(File.Exists(paths.LiveDatabasePath));

        var guard = new WriteAuthorityGuard();
        var resolution = await new AuthorityStateCoordinator(store, guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance)
            .InitializeAsync(legacyEvidence, preMigrationLiveDatabaseEvidence: false);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, resolution.State);
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task AuthorityStartupPreflightAllowsSupportedExistingLiveDatabase()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var evidence = await AuthorityStartupPreflight.CaptureAsync(paths, new JsonAuthorityStateStore(paths));

        Assert.IsTrue(evidence.HasPreExistingLiveDatabase);
        Assert.IsFalse(evidence.HasEstablishedAuthorityArtifacts);
    }

    [TestMethod]
    public async Task FreshMigratedDatabaseDoesNotQualifyAsLegacyBootstrapEvidence()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        var store = new JsonAuthorityStateStore(paths);
        var preflight = await AuthorityStartupPreflight.CaptureAsync(paths, store);
        var evidenceBeforeMigrations = await store.HasLegacyBootstrapEvidenceAsync();

        var clock = new FixedClock();
        await new SqliteMigrationRunner(new SqliteConnectionFactory(paths), ProductionMigrations.All, clock).InitializeAsync();
        Assert.IsFalse(await store.HasLegacyBootstrapEvidenceAsync(), "Fresh-install provenance must override migrated schema history.");

        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(store, guard, clock, NullLogger<AuthorityStateCoordinator>.Instance)
            .InitializeAsync(evidenceBeforeMigrations, preflight.HasPreExistingLiveDatabase);

        Assert.IsFalse(evidenceBeforeMigrations);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.Throws<WriteAuthorityException>(guard.RequireWriteAuthority);
    }

    [TestMethod]
    public async Task FreshInstallProvenancePartialOrCorruptEvidenceAlwaysFailsClosed()
    {
        foreach (var mutate in new Action<TestAppPaths>[]
        {
            paths => File.Delete(Path.Combine(paths.ConfigDirectory, "m07-fresh-install.marker")),
            paths => File.Delete(Path.Combine(paths.DataDirectory, "m07-fresh-install.anchor")),
            paths => File.WriteAllText(Path.Combine(paths.ConfigDirectory, "m07-fresh-install.marker"), "corrupt"),
            paths => File.WriteAllText(Path.Combine(paths.DataDirectory, "m07-fresh-install.anchor"), "crash-shaped-partial")
        })
        {
            using var paths = new TestAppPaths();
            var store = new JsonAuthorityStateStore(paths);
            await AuthorityStartupPreflight.CaptureAsync(paths, store);
            mutate(paths);

            await Assert.ThrowsAsync<InvalidDataException>(() => AuthorityStartupPreflight.CaptureAsync(paths, store));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.HasLegacyBootstrapEvidenceAsync());
        }
    }

    [TestMethod]
    public async Task EstablishedAuthorityStatesRoundTripDurablyAcrossRestart()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var store = new JsonAuthorityStateStore(paths);
        var legacyBootstrapEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        await new AuthorityStateCoordinator(store, new WriteAuthorityGuard(), new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);
        foreach (var (legacyState, expectedState) in new[]
        {
            (WriteAuthorityState.Authoritative, WriteAuthorityState.Authoritative),
            (WriteAuthorityState.NonAuthoritativeReadOnly, WriteAuthorityState.NonAuthoritativeReadOnly),
            (WriteAuthorityState.Transitioning, WriteAuthorityState.RecoveryRequired),
            (WriteAuthorityState.RecoveryRequired, WriteAuthorityState.RecoveryRequired)
        })
        {
            await store.SaveAsync(new AuthorityStateDocument(1, legacyState, DateTimeOffset.UtcNow));
            var guard = new WriteAuthorityGuard();
            var reloaded = await new AuthorityStateCoordinator(store, guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(legacyBootstrapEvidence);
            Assert.AreEqual(expectedState, reloaded.State);
            Assert.AreEqual(expectedState, guard.State);
        }
    }

    [TestMethod]
    public async Task CanonicalAuthorityPersistsOnlyDetailedProtocolAndDerivesTheCoarseGuardState()
    {
        using var paths = new TestAppPaths();
        var store = new JsonAuthorityStateStore(paths);
        var lineage = Guid.NewGuid();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        var hash = new string('a', 64);
        var snapshotReceipt = new RemoteAssetEvidence(12, 34, "snapshot.db", 123, hash);
        var grantReceipt = new RemoteAssetEvidence(12, 35, "grant.json", 456, hash);
        var transfer = new TransferEvidence(
            Guid.NewGuid(), lineage, 1, 1, source, target, 7,
            "snapshot.db", "C:\\snapshot.db", 123, hash, snapshotReceipt, grantReceipt, DateTimeOffset.UtcNow);

        var cases = new (AuthorityPhase Phase, WriteAuthorityState State, AuthorityProtocolState Protocol)[]
        {
            (AuthorityPhase.Uninitialized, WriteAuthorityState.RecoveryRequired,
                new(1, source, "fresh", null, 0, 0, 0, AuthorityPhase.Uninitialized)),
            (AuthorityPhase.PairedUninitializedReadOnly, WriteAuthorityState.NonAuthoritativeReadOnly,
                new(1, source, "joined", lineage, 1, 0, 0, AuthorityPhase.PairedUninitializedReadOnly)),
            (AuthorityPhase.Authoritative, WriteAuthorityState.Authoritative,
                new(1, source, "source", lineage, 1, 0, 0, AuthorityPhase.Authoritative)),
            (AuthorityPhase.ClosedRetainedAuthority, WriteAuthorityState.Authoritative,
                new(1, source, "source", lineage, 1, 0, 0, AuthorityPhase.ClosedRetainedAuthority)),
            (AuthorityPhase.TransferPreparing, WriteAuthorityState.Transitioning,
                new(1, source, "source", lineage, 1, 1, 7, AuthorityPhase.TransferPreparing, transfer with { SnapshotReceipt = null, GrantReceipt = null, RelinquishedAtUtc = null })),
            (AuthorityPhase.RelinquishedPendingGrant, WriteAuthorityState.Transitioning,
                new(1, source, "source", lineage, 1, 1, 7, AuthorityPhase.RelinquishedPendingGrant, transfer with { GrantReceipt = null })),
            (AuthorityPhase.ReleasedNonAuthoritative, WriteAuthorityState.NonAuthoritativeReadOnly,
                new(1, source, "source", lineage, 1, 1, 7, AuthorityPhase.ReleasedNonAuthoritative, transfer)),
            (AuthorityPhase.TargetAcquisitionPending, WriteAuthorityState.Transitioning,
                new(1, target, "target", lineage, 1, 1, 7, AuthorityPhase.TargetAcquisitionPending, transfer)),
            (AuthorityPhase.NonAuthoritativeReadOnly, WriteAuthorityState.NonAuthoritativeReadOnly,
                new(1, target, "target", lineage, 1, 1, 7, AuthorityPhase.NonAuthoritativeReadOnly)),
            (AuthorityPhase.StaleGeneration, WriteAuthorityState.NonAuthoritativeReadOnly,
                new(1, target, "target", lineage, 1, 1, 7, AuthorityPhase.StaleGeneration)),
            (AuthorityPhase.DisasterRecoveryPending, WriteAuthorityState.Transitioning,
                new(1, target, "replacement", lineage, 1, 1, 7, AuthorityPhase.DisasterRecoveryPending,
                    Recovery: new(Guid.NewGuid(), target, lineage, 1, 2, "candidate", hash, 7, snapshotReceipt))),
            (AuthorityPhase.RecoveryRequired, WriteAuthorityState.RecoveryRequired,
                new(1, target, "target", null, 0, 0, 0, AuthorityPhase.RecoveryRequired))
        };

        foreach (var testCase in cases)
        {
            testCase.Protocol.Validate();
            var document = new AuthorityStateDocument(2, testCase.State, DateTimeOffset.UtcNow)
            {
                Protocol = testCase.Protocol
            };
            await store.SaveAsync(document);
            var json = await File.ReadAllTextAsync(Path.Combine(paths.ConfigDirectory, "authority-state.json"));
            using var jsonDocument = JsonDocument.Parse(json);
            Assert.IsFalse(jsonDocument.RootElement.TryGetProperty("state", out _));
            var reloaded = await store.LoadAsync();
            Assert.IsNotNull(reloaded);
            Assert.AreEqual(testCase.State, reloaded!.EffectiveState);
            Assert.AreEqual(testCase.Phase, reloaded.Protocol!.Phase);
        }
    }

    [TestMethod]
    public async Task ExactSerializedM06RecoveryAndTransitioningStatesRemainNonWritableAfterMigration()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await using (var legacyConnection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = paths.LiveDatabasePath, Pooling = false }.ToString()))
        {
            await legacyConnection.OpenAsync();
            await ExecuteAsync(legacyConnection, "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES(5);");
        }

        var store = new JsonAuthorityStateStore(paths);
        var legacyEvidence = await store.HasLegacyBootstrapEvidenceAsync();
        await new AuthorityStateCoordinator(store, new WriteAuthorityGuard(), new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance)
            .InitializeAsync(legacyEvidence);

        foreach (var legacyState in new[] { "Transitioning", "RecoveryRequired" })
        {
            var legacyValue = legacyState == "Transitioning" ? 3 : 4;
            await File.WriteAllTextAsync(
                Path.Combine(paths.ConfigDirectory, "authority-state.json"),
                $"{{\"schemaVersion\":1,\"state\":{legacyValue},\"updatedAtUtc\":\"2026-09-07T12:00:00Z\"}}");
            var guard = new WriteAuthorityGuard();
            var result = await new AuthorityStateCoordinator(store, guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance)
                .InitializeAsync(legacyEvidence);
            Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
            Assert.AreEqual(WriteAuthorityState.RecoveryRequired, guard.State);
            Assert.AreEqual(2, (await store.LoadAsync())!.SchemaVersion);
        }
    }

    [TestMethod]
    public async Task CanonicalAuthorityReplacementReopensAndFailureBeforeReplaceLeavesPriorDocumentIntact()
    {
        using var paths = new TestAppPaths();
        var lineage = Guid.NewGuid();
        var device = Guid.NewGuid();
        var firstProtocol = new AuthorityProtocolState(1, device, "device", lineage, 1, 0, 0, AuthorityPhase.Authoritative);
        var firstDocument = new AuthorityStateDocument(2, WriteAuthorityState.Authoritative, DateTimeOffset.UtcNow)
        {
            Protocol = firstProtocol
        };
        var firstEvents = new List<string>();
        var store = new JsonAuthorityStateStore(paths, firstEvents.Add);
        await store.SaveAsync(firstDocument);
        CollectionAssert.Contains(firstEvents, "before-replace");
        CollectionAssert.Contains(firstEvents, "after-reopen");

        var secondProtocol = firstProtocol with { Phase = AuthorityPhase.NonAuthoritativeReadOnly };
        var secondDocument = new AuthorityStateDocument(2, WriteAuthorityState.NonAuthoritativeReadOnly, DateTimeOffset.UtcNow)
        {
            Protocol = secondProtocol
        };
        var failingStore = new JsonAuthorityStateStore(paths, point =>
        {
            if (point == "before-replace") throw new IOException("injected before replace");
        });
        await Assert.ThrowsAsync<IOException>(() => failingStore.SaveAsync(secondDocument));
        var persisted = await store.LoadAsync();
        Assert.IsNotNull(persisted);
        Assert.AreEqual(AuthorityPhase.Authoritative, persisted!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.Authoritative, persisted.EffectiveState);
    }

    [TestMethod]
    public async Task MalformedOrFutureAuthorityStateFailsClosedWithoutBootstrap()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "authority-state.json"), "{\"schemaVersion\":99,\"state\":\"Authoritative\",\"updatedAtUtc\":\"2026-08-27T12:00:00Z\"}");
        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "authority-bootstrap.marker"), "marker");

        var guard = new WriteAuthorityGuard();
        var result = await new AuthorityStateCoordinator(new JsonAuthorityStateStore(paths), guard, new FixedClock(), NullLogger<AuthorityStateCoordinator>.Instance).InitializeAsync(false);

        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, result.State);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, guard.State);
    }

    [TestMethod]
    public async Task DurableChangeNotifierPersistsSequenceAndCoalescesRecovery()
    {
        using var paths = new TestAppPaths();
        var snapshots = new RecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(snapshots, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        using var notifier = await DurableChangeNotifier.CreateAsync(paths, new FixedClock(), scheduler, NullLogger<DurableChangeNotifier>.Instance);

        await notifier.NotifyCommittedAsync();
        await notifier.NotifyCommittedAsync();
        await scheduler.FlushAsync();

        Assert.HasCount(1, snapshots.Changes);
        Assert.AreEqual(2L, snapshots.Changes[0].Sequence);
        StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(paths.ConfigDirectory, "recovery-sequence.json")), "2");
    }

    [TestMethod]
    public async Task DurableChangeNotifierReconcilesCorruptSequenceWithValidatedRecoveryMetadataAfterRestart()
    {
        using var paths = new TestAppPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var snapshots = new SqliteLocalRecoverySnapshotService(paths, factory, clock);
        await snapshots.CreateAsync(new DurableChange(7, clock.UtcNow));
        await File.WriteAllTextAsync(Path.Combine(paths.ConfigDirectory, "recovery-sequence.json"), "{ not-json");

        var recording = new RecordingSnapshotService();
        await using var scheduler = new DebouncedRecoveryScheduler(recording, TimeProvider.System, NullLogger<DebouncedRecoveryScheduler>.Instance);
        using var notifier = await DurableChangeNotifier.CreateAsync(paths, clock, scheduler, NullLogger<DurableChangeNotifier>.Instance);
        await notifier.NotifyCommittedAsync();

        StringAssert.Contains(await File.ReadAllTextAsync(Path.Combine(paths.ConfigDirectory, "recovery-sequence.json")), "8");
    }

    [TestMethod]
    public void RedactorExcludesRepresentativeSensitiveValues()
    {
        const string usernameOnlyUrl = "https://synthetic-user@example.test/path";
        var credentialUrl = string.Concat("https://", "user", ":", "password", "@example.test/api?access_token=query-secret&next=1");
        var output = SensitiveDataRedactor.Redact(
            "phone 0612345678 email client@example.test "
            + "Bearer standalone-secret ghp_abcdefghijklmnopqrstuvwxyz "
            + credentialUrl + " token=field-secret "
            + usernameOnlyUrl + " "
            + "{\"token\":\"json-token\", \"access_token\": \"json-access\", "
            + "\"refresh_token\":\"json-refresh\", \"secret\": \"json-secret\", "
            + "\"password\":\"json-password\", \"client_secret\": \"json-client\", "
            + "\"authorization\": \"json-authorization\"} Authorization: Bearer header-secret");
        Assert.IsFalse(output.Contains("0612345678", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("client@example.test", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("header-secret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("standalone-secret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("ghp_abcdefghijklmnopqrstuvwxyz", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("user:password@", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("synthetic-user@", StringComparison.Ordinal));
        StringAssert.Contains(output, "https://[redacted-userinfo]@example.test/path");
        StringAssert.Contains(output, "https://[redacted-userinfo]@example.test/api");
        Assert.IsFalse(output.Contains("query-secret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("field-secret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-token", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-access", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-refresh", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-secret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-password", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-client", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-authorization", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RollingFileLoggerRedactsMessageAndExceptionSecretsBeforeWriting()
    {
        using var paths = new TestAppPaths();
        using (var provider = new RollingFileLoggerProvider(paths, TimeProvider.System))
        {
            var logger = provider.CreateLogger("synthetic");
            var messageCredentialUrl = string.Concat("https://", "user", ":", "password", "@example.test/api?token=query-secret");
            var exceptionCredentialUrl = string.Concat("https://", "exception-user", ":", "exception-password", "@example.test/exception");
            logger.Log(
                LogLevel.Error,
                new EventId(9001, "SyntheticSecret"),
                "transport failed " + messageCredentialUrl + " "
                    + "https://synthetic-user@example.test/message ghp_abcdefghijklmnopqrstuvwxyz "
                    + "{\"token\":\"json-token\", \"password\": \"json-password\"}",
                new InvalidOperationException(
                    exceptionCredentialUrl + " "
                    + "https://exception-synthetic-user@example.test/exception-user "
                    + "?access_token=exception-query {\"secret\":\"exception-json\", \"client_secret\": \"exception-client\"} "
                    + "ghp_exception_abcdefghijklmnopqrstuvwxyz Authorization: Bearer exception-secret"),
                static (state, exception) => state);
        }

        var output = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(paths.LogsDirectory, "sushi81-*.log").Select(File.ReadAllText));
        Assert.IsFalse(output.Contains("exception-secret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("user:password@", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("synthetic-user@", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("exception-user:exception-password@", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("exception-synthetic-user@", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("exception-query", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("exception-json", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("exception-client", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("ghp_exception_abcdefghijklmnopqrstuvwxyz", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("query-secret", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("ghp_abcdefghijklmnopqrstuvwxyz", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-token", StringComparison.Ordinal));
        Assert.IsFalse(output.Contains("json-password", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RollingFileLoggerFailureDoesNotEscapeIntoBusinessOperation()
    {
        using var paths = new TestAppPaths();
        using var provider = new RollingFileLoggerProvider(paths, TimeProvider.System);
        Directory.Delete(paths.LogsDirectory);
        File.WriteAllText(paths.LogsDirectory, "synthetic blocker");
        var logger = provider.CreateLogger("synthetic-failure-path");

        logger.Log(LogLevel.Error, new EventId(9002, "SyntheticLogIoFailure"), "diagnostics unavailable", null, static (state, _) => state);

        Assert.AreEqual("synthetic blocker", File.ReadAllText(paths.LogsDirectory));
    }

    [TestMethod]
    public void RollingFileLoggerRetainsAtMostFourteenNewestFiles()
    {
        using var paths = new TestAppPaths();
        paths.EnsureInitialized();
        for (var sequence = 0; sequence < 16; sequence++)
            File.WriteAllText(Path.Combine(paths.LogsDirectory, $"sushi81-20250101-{sequence:D3}.log"), "synthetic old log");

        using (var provider = new RollingFileLoggerProvider(paths, TimeProvider.System))
            provider.CreateLogger("retention-test").Log(LogLevel.Information, new EventId(9003, "SyntheticRetention"), "synthetic newest log", null, static (state, _) => state);

        var remaining = Directory.EnumerateFiles(paths.LogsDirectory, "sushi81-*.log").ToArray();
        Assert.HasCount(14, remaining);
        Assert.IsTrue(remaining.Any(path => Path.GetFileName(path).StartsWith("sushi81-", StringComparison.Ordinal)));
        Assert.IsFalse(File.Exists(Path.Combine(paths.LogsDirectory, "sushi81-20250101-000.log")));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, SqliteTransaction? transaction = null, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string CreateSyntheticSequenceCte() => """
        WITH digit(n) AS (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)),
        numbers(n) AS (
            SELECT 1 + ones.n + tens.n*10 + hundreds.n*100 + thousands.n*1000 + tenThousands.n*10000
            FROM digit AS ones
            CROSS JOIN digit AS tens
            CROSS JOIN digit AS hundreds
            CROSS JOIN digit AS thousands
            CROSS JOIN digit AS tenThousands
        )
        """;

    private static async Task<string> ScalarStringAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static async Task<IReadOnlyList<string>> ReadQueryPlanDetailsAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var details = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) details.Add(reader.GetString(3));
        return details;
    }

    private static async Task<bool> RunWithoutThrowingAsync(SqliteMigrationRunner runner)
    {
        try
        {
            await runner.InitializeAsync();
            return true;
        }
        catch (DatabaseMigrationException)
        {
            return false;
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

        public DateOnly BusinessDate => new(2026, 8, 27);

        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class RecordingSnapshotService : ILocalRecoverySnapshotService
    {
        public List<DurableChange> Changes { get; } = [];

        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            return Task.FromResult(new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1));
        }
    }

    private sealed class DelayedRecordingSnapshotService : ILocalRecoverySnapshotService
    {
        private int activeCalls;
        private int maximumConcurrentCalls;

        public List<DurableChange> Changes { get; } = [];

        public int MaximumConcurrentCalls => maximumConcurrentCalls;

        public async Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            var active = Interlocked.Increment(ref activeCalls);
            InterlockedExtensions.Max(ref maximumConcurrentCalls, active);
            try
            {
                await Task.Delay(50, cancellationToken);
                Changes.Add(change);
                return new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1);
            }
            finally
            {
                Interlocked.Decrement(ref activeCalls);
            }
        }
    }

    private sealed class ActiveSnapshotService : ILocalRecoverySnapshotService
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<DurableChange> Changes { get; } = [];

        public async Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Changes.Add(change);
            Started.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return new RecoverySnapshotResult("synthetic.db", "synthetic.json", "checksum", change.CommittedAtUtc, change.Sequence, 1);
        }

        public void Release() => release.TrySetResult(true);
    }

    private sealed class FailingSnapshotService : ILocalRecoverySnapshotService
    {
        public int Attempts { get; private set; }

        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            Attempts++;
            throw new IOException("synthetic snapshot failure");
        }
    }

    private sealed class FailOnSixthSnapshotService(IAppPaths paths, SqliteConnectionFactory connectionFactory, IBusinessClock clock)
        : SqliteLocalRecoverySnapshotService(paths, connectionFactory, clock)
    {
        private int calls;

        protected override Task BackupToStagingAsync(string stagingDatabasePath, CancellationToken cancellationToken)
        {
            calls++;
            return calls == 6
                ? Task.FromException(new IOException("synthetic sixth snapshot failure"))
                : base.BackupToStagingAsync(stagingDatabasePath, cancellationToken);
        }
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref int location, int value)
    {
        var current = Volatile.Read(ref location);
        while (current < value)
        {
            var observed = Interlocked.CompareExchange(ref location, value, current);
            if (observed == current)
            {
                return;
            }

            current = observed;
        }
    }
}

internal sealed class TestAppPaths : IAppPaths, IDisposable
{
    public TestAppPaths()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.Tests", Guid.NewGuid().ToString("N"));
        DataDirectory = Path.Combine(RootDirectory, "Data");
        RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
        CacheDirectory = Path.Combine(RootDirectory, "Cache");
        LogsDirectory = Path.Combine(RootDirectory, "Logs");
        ConfigDirectory = Path.Combine(RootDirectory, "Config");
        TempDirectory = Path.Combine(RootDirectory, "Temp");
        ArchiveDirectory = Path.Combine(RootDirectory, "Archive");
        LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
    }

    public string RootDirectory { get; }
    public string DataDirectory { get; }
    public string RecoveryDirectory { get; }
    public string CacheDirectory { get; }
    public string LogsDirectory { get; }
    public string ConfigDirectory { get; }
    public string TempDirectory { get; }
    public string ArchiveDirectory { get; }
    public string LiveDatabasePath { get; }

    public void EnsureInitialized()
    {
        foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory, ArchiveDirectory })
        {
            Directory.CreateDirectory(path);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(RootDirectory))
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
