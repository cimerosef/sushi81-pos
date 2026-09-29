using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M14InitialProductionSeedTests
{
    [TestMethod]
    public async Task SyntheticSchemaFiveSeedMigratesOnlyTheStagedCopyAndBootstrapsFreshPreProductionAuthority()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        await fixture.InsertCatalogueDataAsync();
        await fixture.AddProductionOnlyArtifactsAsync();
        var sourceHash = await HashAsync(fixture.Production.LiveDatabasePath);
        var service = fixture.Service();

        Assert.IsTrue(service.IsOfferEligible());
        var result = await service.SeedAsync();

        Assert.AreEqual(InitialProductionSeedStatus.Completed, result.Status);
        Assert.AreEqual(sourceHash, await HashAsync(fixture.Production.LiveDatabasePath), "The source database file must remain byte-identical.");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.PreProduction.ConfigDirectory, "authority-state.json")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.PreProduction.ConfigDirectory, "local-settings.json")));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.ConfigDirectory));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.RecoveryDirectory));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.CacheDirectory));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.LogsDirectory));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.ArchiveDirectory));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.TempDirectory));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.PreProduction.RootDirectory, "Recovery", "prod-recovery")));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.PreProduction.ArchiveDirectory, "2025")));

        var factory = new SqliteConnectionFactory(fixture.PreProduction);
        await using (var db = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(fixture.PreProduction.LiveDatabasePath))
        {
            Assert.AreEqual("ok", await SqliteConnectionFactory.ExecuteScalarStringAsync(db, "PRAGMA integrity_check;", CancellationToken.None));
            Assert.AreEqual(0, await SqliteConnectionFactory.ExecuteScalarIntAsync(db, "SELECT COUNT(*) FROM pragma_foreign_key_check;", CancellationToken.None));
            Assert.AreEqual(11, await SqliteConnectionFactory.ExecuteScalarIntAsync(db, "SELECT MAX(version) FROM schema_migrations;", CancellationToken.None));
            Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(db, "SELECT COUNT(*) FROM categories WHERE category_id='synthetic-category';", CancellationToken.None));
            Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(db, "SELECT COUNT(*) FROM products WHERE product_id='synthetic-product';", CancellationToken.None));
            Assert.AreEqual(0, await SqliteConnectionFactory.ExecuteScalarIntAsync(db, "SELECT CAST(value AS INTEGER) FROM foundation_metadata WHERE key='business_data_revision';", CancellationToken.None));
            StringAssert.Contains(await SqliteConnectionFactory.ExecuteScalarStringAsync(db, $"SELECT value FROM foundation_metadata WHERE key='{InitialProductionSeedService.ProvenanceKey}';", CancellationToken.None), "snapshotSha256=");
        }

        var authorityStore = new JsonAuthorityStateStore(fixture.PreProduction);
        var preflight = await AuthorityStartupPreflight.CaptureAsync(fixture.PreProduction, authorityStore);
        Assert.IsTrue(preflight.HasPreExistingLiveDatabase);
        Assert.IsFalse(preflight.HasEstablishedAuthorityArtifacts);
        Assert.IsFalse(await authorityStore.HasFreshInstallProvenanceAsync());
        var legacyEvidence = await authorityStore.HasLegacyBootstrapEvidenceAsync();
        var guard = new WriteAuthorityGuard();
        var resolution = await new AuthorityStateCoordinator(authorityStore, guard, fixture.Clock, NullLogger.Instance)
            .InitializeAsync(legacyEvidence, preflight.HasPreExistingLiveDatabase);
        Assert.IsTrue(resolution.IsUsable);
        Assert.AreEqual(Sushi81.Pos.Application.Foundation.Authority.WriteAuthorityState.Authoritative, resolution.State);
        var authority = (await authorityStore.LoadAsync())!.Protocol!;
        Assert.AreNotEqual(Guid.Parse("b8676481-c1a2-4e1c-9d62-f89a050a0001"), authority.DeviceId);
        Assert.AreNotEqual(Guid.Parse("b8676481-c1a2-4e1c-9d62-f89a050a0002"), authority.LineageId);
        Assert.AreEqual(0L, authority.BusinessRevision);
        var seededHash = await HashAsync(fixture.PreProduction.LiveDatabasePath);
        var repeated = await fixture.Service().SeedAsync();
        Assert.AreEqual(InitialProductionSeedStatus.TargetNotPristine, repeated.Status);
        Assert.AreEqual(sourceHash, await HashAsync(fixture.Production.LiveDatabasePath));
        Assert.AreEqual(seededHash, await HashAsync(fixture.PreProduction.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CurrentSchemaSeedRetainsBusinessLedgersWithoutCopyingAnnualArchiveFiles()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 11);
        await fixture.InsertCatalogueDataAsync();
        await fixture.InsertOptionDataAsync();
        await fixture.InsertBusinessOperationsAsync();
        await fixture.InsertExportAndArchiveLedgersAsync();
        var archiveFile = Path.Combine(fixture.Production.ArchiveDirectory, "2025", "archive.db");
        Directory.CreateDirectory(Path.GetDirectoryName(archiveFile)!);
        await File.WriteAllTextAsync(archiveFile, "synthetic canonical archive");
        var result = await fixture.Service().SeedAsync();
        Assert.AreEqual(InitialProductionSeedStatus.Completed, result.Status);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.PreProduction.ArchiveDirectory, "2025", "archive.db")));
        await using var seeded = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(fixture.PreProduction.LiveDatabasePath);
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM export_batches;", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM annual_archive_completions;", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM annual_archive_order_proofs;", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM business_settings;", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM option_groups;", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM options;", CancellationToken.None));
        Assert.AreEqual(1000, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT card_payment_ttc_cents+cash_payment_ttc_cents FROM orders WHERE order_reference='20260101-001';", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM orders WHERE order_id='b8676481-c1a2-4e1c-9d62-f89a050a0010';", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM order_items;", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM order_item_adjustments;", CancellationToken.None));
        Assert.AreEqual(1, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT COUNT(*) FROM payment_adjustments;", CancellationToken.None));
    }

    [TestMethod]
    public async Task PreProductionRemoteConfigurationBlocksSeedOffer()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        fixture.PreProduction.EnsureInitialized();
        await File.WriteAllTextAsync(Path.Combine(fixture.PreProduction.ConfigDirectory, "local-settings.json"),
            "{\"uiCulture\":\"fr-FR\",\"oneDriveRoot\":\"C:\\\\prod\\\\cloud\",\"githubOwner\":\"synthetic-prod\",\"githubRepository\":\"prod\",\"githubCredentialTarget\":\"prod-target\"}");
        var sourceHash = await HashAsync(fixture.Production.LiveDatabasePath);
        var service = fixture.Service();
        Assert.IsFalse(service.IsOfferEligible());
        Assert.AreEqual(InitialProductionSeedStatus.TargetNotPristine, (await service.SeedAsync()).Status);
        Assert.AreEqual(sourceHash, await HashAsync(fixture.Production.LiveDatabasePath));
        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));
    }

    [TestMethod]
    public async Task PreProductionPrinterCustomReleaseAndUnknownConfigurationBlockSeedOffer()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        fixture.PreProduction.EnsureInitialized();
        var settingsPath = Path.Combine(fixture.PreProduction.ConfigDirectory, "local-settings.json");
        var cases = new[]
        {
            "{\"uiCulture\":\"fr-FR\",\"kitchenPrinterQueueName\":\"Synthetic printer\"}",
            "{\"uiCulture\":\"fr-FR\",\"githubReleaseTag\":\"synthetic-preprod\"}",
            "{\"uiCulture\":\"fr-FR\",\"unrecognizedTransport\":\"synthetic-config\"}"
        };

        foreach (var settings in cases)
        {
            await File.WriteAllTextAsync(settingsPath, settings);
            Assert.IsFalse(fixture.Service().IsOfferEligible(), settings);
            Assert.AreEqual(InitialProductionSeedStatus.TargetNotPristine, (await fixture.Service().SeedAsync()).Status, settings);
            File.Delete(settingsPath);
        }

        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));
    }

    [TestMethod]
    public async Task ActiveProductionProcessAndNonPristinePreProductionTargetAreRejectedWithoutOverwrite()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        var sourceHash = await HashAsync(fixture.Production.LiveDatabasePath);

        var running = await fixture.Service(productionRunning: true).SeedAsync();
        Assert.AreEqual(InitialProductionSeedStatus.ProductionRunningOrUnknown, running.Status);
        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));

        Directory.CreateDirectory(fixture.PreProduction.DataDirectory);
        var sentinel = Path.Combine(fixture.PreProduction.DataDirectory, "keep.txt");
        await File.WriteAllTextAsync(sentinel, "synthetic target state");
        var blocked = await fixture.Service().SeedAsync();
        Assert.AreEqual(InitialProductionSeedStatus.TargetNotPristine, blocked.Status);
        Assert.AreEqual("synthetic target state", await File.ReadAllTextAsync(sentinel));
        Assert.AreEqual(sourceHash, await HashAsync(fixture.Production.LiveDatabasePath));
        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));
    }

    [TestMethod]
    public async Task InvalidMigrationHistoryLeavesSourceAndPreProductionTargetUnchanged()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        await using (var db = await new SqliteConnectionFactory(fixture.Production).OpenLiveConnectionAsync())
        {
            await SqliteConnectionFactory.ExecuteNonQueryAsync(db, "UPDATE schema_migrations SET name='unexpected-migration' WHERE version=5;", CancellationToken.None);
        }
        var sourceHash = await HashAsync(fixture.Production.LiveDatabasePath);

        var result = await fixture.Service().SeedAsync();

        Assert.AreEqual(InitialProductionSeedStatus.MigrationFailed, result.Status);
        Assert.AreEqual(sourceHash, await HashAsync(fixture.Production.LiveDatabasePath));
        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.DataDirectory));
    }

    [TestMethod]
    public async Task FailureAfterAtomicMoveIsRecognizedOnlyWhenTheCompleteSeedIsInstalled()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        await fixture.InsertCatalogueDataAsync();
        Action<string, string> installThenFail = (source, destination) =>
        {
            File.Move(source, destination, overwrite: false);
            throw new IOException("synthetic failure reported after atomic move");
        };
        var result = await fixture.Service(atomicInstall: installThenFail).SeedAsync();
        Assert.AreEqual(InitialProductionSeedStatus.Completed, result.Status);
        await using var seeded = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(fixture.PreProduction.LiveDatabasePath);
        Assert.AreEqual("ok", await SqliteConnectionFactory.ExecuteScalarStringAsync(seeded, "PRAGMA integrity_check;", CancellationToken.None));
        Assert.AreEqual(11, await SqliteConnectionFactory.ExecuteScalarIntAsync(seeded, "SELECT MAX(version) FROM schema_migrations;", CancellationToken.None));
    }

    [TestMethod]
    public async Task TargetAppearanceImmediatelyBeforeCommitIsPreservedAndSeedIsRefused()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        var sourceHash = await HashAsync(fixture.Production.LiveDatabasePath);
        Action appear = () =>
        {
            Directory.CreateDirectory(fixture.PreProduction.DataDirectory);
            File.WriteAllText(Path.Combine(fixture.PreProduction.DataDirectory, "race.txt"), "external target state");
        };
        var result = await fixture.Service(beforeFinalInstall: appear).SeedAsync();
        Assert.AreEqual(InitialProductionSeedStatus.TargetNotPristine, result.Status);
        Assert.AreEqual("external target state", await File.ReadAllTextAsync(Path.Combine(fixture.PreProduction.DataDirectory, "race.txt")));
        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));
        Assert.AreEqual(sourceHash, await HashAsync(fixture.Production.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CancellationImmediatelyBeforeCommitLeavesTargetRetryableAndSourceUnchanged()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        var sourceHash = await HashAsync(fixture.Production.LiveDatabasePath);
        using var cancellation = new CancellationTokenSource();
        var result = await fixture.Service(beforeFinalInstall: cancellation.Cancel).SeedAsync(cancellation.Token);
        Assert.AreEqual(InitialProductionSeedStatus.InstallFailed, result.Status);
        Assert.AreEqual(sourceHash, await HashAsync(fixture.Production.LiveDatabasePath));
        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));
        Assert.IsFalse(Directory.Exists(fixture.PreProduction.RootDirectory));
    }
    [TestMethod]
    public async Task ProductionProfileCannotRunPreProductionSeedService()
    {
        using var fixture = await Fixture.CreateAsync(schemaVersion: 5);
        var service = new InitialProductionSeedService(fixture.Production, new FixedProcessInspector(false), fixture.Clock);

        Assert.IsFalse(service.IsOfferEligible());
        Assert.AreEqual(InitialProductionSeedStatus.TargetNotPristine, (await service.SeedAsync()).Status);
        Assert.IsFalse(File.Exists(fixture.PreProduction.LiveDatabasePath));
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed class FixedProcessInspector(bool running) : IProductionProcessInspector
    {
        public bool IsRunningOrCannotVerify() => running;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root;
        public WindowsAppPaths Production { get; }
        public WindowsAppPaths PreProduction { get; }
        public TimeProviderBusinessClock Clock { get; }

        private Fixture(string root, WindowsAppPaths production, WindowsAppPaths preProduction, TimeProviderBusinessClock clock)
        { this.root = root; Production = production; PreProduction = preProduction; Clock = clock; }

        public static async Task<Fixture> CreateAsync(int schemaVersion)
        {
            var root = Path.Combine(Path.GetTempPath(), $"m14-seed-{Guid.NewGuid():N}");
            var production = new WindowsAppPaths(DeploymentProfile.Production, root);
            var preProduction = new WindowsAppPaths(DeploymentProfile.PreProduction, root);
            var clock = new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Utc);
            production.EnsureInitialized();
            var factory = new SqliteConnectionFactory(production);
            var migrations = ProductionMigrations.All.Where(value => value.Version <= schemaVersion).ToArray();
            await new SqliteMigrationRunner(factory, migrations, clock).InitializeAsync();
            return new Fixture(root, production, preProduction, clock);
        }

        public InitialProductionSeedService Service(bool productionRunning = false, Action<string, string>? atomicInstall = null, Action? beforeFinalInstall = null)
            => new(PreProduction, new FixedProcessInspector(productionRunning), Clock, atomicInstall, beforeFinalInstall);

        public async Task InsertCatalogueDataAsync()
        {
            await using var db = await new SqliteConnectionFactory(Production).OpenLiveConnectionAsync();
            await SqliteConnectionFactory.ExecuteNonQueryAsync(db, """
                INSERT INTO categories(category_id,name,normalized_name,created_at_utc,updated_at_utc)
                    VALUES('synthetic-category','Synthetic category','synthetic category','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
                INSERT INTO products(product_id,code,normalized_code,name,category_id,price_ttc_cents,vat_rate,is_active,discount_eligible,options_enabled,created_at_utc,updated_at_utc)
                    VALUES('synthetic-product','SYN-1','SYN-1','Synthetic product','synthetic-category',1250,'10',1,1,0,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
                INSERT INTO foundation_metadata(key,value) VALUES('business_data_revision','41');
                """, CancellationToken.None);
        }

        public async Task InsertOptionDataAsync()
        {
            await using var db = await new SqliteConnectionFactory(Production).OpenLiveConnectionAsync();
            await SqliteConnectionFactory.ExecuteNonQueryAsync(db, """
                UPDATE products SET options_enabled=1 WHERE product_id='synthetic-product';
                INSERT INTO option_groups(option_group_id,product_id,name,selection_mode,is_required,min_selections,max_selections,display_order,created_at_utc,updated_at_utc)
                    VALUES('synthetic-group','synthetic-product','Synthetic options','SINGLE',0,NULL,NULL,0,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
                INSERT INTO options(option_id,option_group_id,name,price_adjustment_ttc_cents,is_active,display_order,created_at_utc,updated_at_utc)
                    VALUES('synthetic-option','synthetic-group','Synthetic option',50,1,0,'2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
                """, CancellationToken.None);
        }

        public async Task InsertBusinessOperationsAsync()
        {
            await using var db = await new SqliteConnectionFactory(Production).OpenLiveConnectionAsync();
            await SqliteConnectionFactory.ExecuteNonQueryAsync(db, """
                INSERT INTO orders(order_id,source_type,status,created_at_utc,updated_at_utc,closed_at_utc,cancelled_at_utc,fulfilment_mode,planned_fulfilment_date,planned_fulfilment_time,advance_order_marker,telephone,delivery_address,comment,total_ttc_cents,manual_total_override_active,pickup_discount_applied,pickup_discount_rate,delivery_fee_ttc_cents,order_reference,card_payment_ttc_cents,cash_payment_ttc_cents,source_total_ttc_cents)
                    VALUES('b8676481-c1a2-4e1c-9d62-f89a050a0010','POS','CLOSED','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z',NULL,'RETRAIT','2026-01-01',NULL,0,NULL,NULL,'synthetic order',1000,0,0,NULL,0,'20260101-001',600,400,1000);
                INSERT INTO order_items(order_item_id,order_id,line_position,source_product_id,product_code_snapshot,product_name_snapshot,category_name_snapshot,product_base_price_ttc_cents,product_vat_rate,product_discount_eligible_snapshot,quantity,extended_base_ttc_cents,calculated_line_total_ttc_cents)
                    VALUES('b8676481-c1a2-4e1c-9d62-f89a050a0011','b8676481-c1a2-4e1c-9d62-f89a050a0010',0,'synthetic-product','SYN-1','Synthetic product','Synthetic category',1000,'10',1,1,1000,1000);
                INSERT INTO order_item_adjustments(order_item_adjustment_id,order_item_id,display_order,adjustment_kind,source_option_id,group_name_snapshot,label_snapshot,adjustment_ttc_per_unit_cents,vat_rate)
                    VALUES('b8676481-c1a2-4e1c-9d62-f89a050a0012','b8676481-c1a2-4e1c-9d62-f89a050a0011',0,'CUSTOM_ADJUSTMENT',NULL,NULL,'Synthetic adjustment',0,'10');
                INSERT INTO order_tax_breakdown(order_tax_breakdown_id,order_id,vat_rate,taxable_ttc_cents,included_vat_ttc_cents)
                    VALUES('b8676481-c1a2-4e1c-9d62-f89a050a0014','b8676481-c1a2-4e1c-9d62-f89a050a0010','10',1000,91);
                INSERT INTO payment_adjustments(payment_adjustment_id,order_id,bucket,delta_cents,effective_business_date,effective_at,recorded_at)
                    VALUES('b8676481-c1a2-4e1c-9d62-f89a050a0013','b8676481-c1a2-4e1c-9d62-f89a050a0010','CB',100,'2026-01-01','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
                INSERT INTO order_reference_sequences(business_date,next_sequence) VALUES('2026-01-01',2);
                """, CancellationToken.None);
        }
        public async Task InsertExportAndArchiveLedgersAsync()
        {
            var sha = new string('A', 64);
            await using var db = await new SqliteConnectionFactory(Production).OpenLiveConnectionAsync();
            var sql = "INSERT INTO export_batches(batch_id,schema_version,generated_at_utc,app_version,filter_start_date,filter_end_date,order_count,order_line_count,tax_breakdown_count,status,payload_json,payload_hash,completed_at_utc) VALUES('synthetic-batch','1','2026-01-01T00:00:00Z','1.0.1',NULL,NULL,0,0,0,'SUCCESS','{}','synthetic-hash','2026-01-01T00:00:00Z');"
                + "INSERT INTO annual_archive_completions(archive_year,archive_format_version,archive_schema_version,archive_file_name,archive_order_count,archive_sha256,completed_at_utc) VALUES(2025,'1','1','2025.db',0,$sha,'2026-01-01T00:00:00Z');"
                + "INSERT INTO annual_archive_order_proofs(order_id,archive_year,archive_sha256,archive_completed_at_utc) VALUES('b8676481-c1a2-4e1c-9d62-f89a050a0003',2025,$sha,'2026-01-01T00:00:00Z');";
            await using var command = db.CreateCommand();
            command.CommandText = sql;
            command.Parameters.AddWithValue("$sha", sha);
            await command.ExecuteNonQueryAsync();
        }
        public async Task AddProductionOnlyArtifactsAsync()
        {
            await File.WriteAllTextAsync(Path.Combine(Production.ConfigDirectory, "local-settings.json"),
                "{\"uiCulture\":\"fr-FR\",\"oneDriveRoot\":\"C:\\\\prod\\\\cloud\",\"githubOwner\":\"synthetic-prod\",\"githubRepository\":\"prod\",\"githubCredentialTarget\":\"prod-target\"}");
            await File.WriteAllTextAsync(Path.Combine(Production.ConfigDirectory, "pairing-metadata.json"), "{\"deviceId\":\"synthetic-prod-device\"}");
            await File.WriteAllTextAsync(Path.Combine(Production.ConfigDirectory, "pending-handoff.json"), "{\"lineageId\":\"synthetic-prod-lineage\"}");
            await File.WriteAllTextAsync(Path.Combine(Production.ConfigDirectory, "authority-state.json"),
                "{\"deviceId\":\"b8676481-c1a2-4e1c-9d62-f89a050a0001\",\"lineageId\":\"b8676481-c1a2-4e1c-9d62-f89a050a0002\"}");
            Directory.CreateDirectory(Path.Combine(Production.CacheDirectory, "prod-cache"));
            await File.WriteAllTextAsync(Path.Combine(Production.CacheDirectory, "prod-cache", "cache.bin"), "synthetic-cache");
            Directory.CreateDirectory(Path.Combine(Production.LogsDirectory, "prod-logs"));
            await File.WriteAllTextAsync(Path.Combine(Production.LogsDirectory, "prod-logs", "startup.log"), "synthetic-log");
            Directory.CreateDirectory(Path.Combine(Production.TempDirectory, "prod-temp"));
            await File.WriteAllTextAsync(Path.Combine(Production.TempDirectory, "prod-temp", "staging.db"), "synthetic-temp");
            Directory.CreateDirectory(Path.Combine(Production.RecoveryDirectory, "prod-recovery"));
            await File.WriteAllTextAsync(Path.Combine(Production.RecoveryDirectory, "prod-recovery", "snapshot.db"), "synthetic-recovery");
            Directory.CreateDirectory(Path.Combine(Production.ArchiveDirectory, "2025"));
            await File.WriteAllTextAsync(Path.Combine(Production.ArchiveDirectory, "2025", "archive.db"), "synthetic-archive");
        }

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
