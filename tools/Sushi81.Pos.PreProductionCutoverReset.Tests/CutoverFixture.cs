using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.PreProductionCutoverReset;

namespace Sushi81.Pos.PreProductionCutoverReset.Tests;

internal sealed class CutoverFixture : IDisposable
{
    public CutoverFixture(int authorityPhase = 3)
    {
        Root = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.Cutover.Tests", Guid.NewGuid().ToString("N"));
        Data = Path.Combine(Root, "Data");
        Config = Path.Combine(Root, "Config");
        Archive = Path.Combine(Root, "Archive");
        OneDriveRoot = Path.Combine(Root, "SyntheticOneDrive");
        DatabasePath = Path.Combine(Data, "live.db");
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Config);
        Directory.CreateDirectory(Archive);
        Directory.CreateDirectory(OneDriveRoot);
        DeviceId = Guid.NewGuid();
        LineageId = Guid.NewGuid();
        WriteAuthority(phase: authorityPhase, transfer: null, recovery: null);
        WriteSystemIdentity();
        File.WriteAllText(Path.Combine(Config, "local-settings.json"), JsonSerializer.Serialize(new { oneDriveRoot = OneDriveRoot }));
        File.WriteAllText(Path.Combine(Config, "preserved-config.txt"), "synthetic-configuration");
        File.WriteAllBytes(Path.Combine(Config, "synthetic-config.bin"), [0, 1, 2, 255]);
        File.WriteAllText(Path.Combine(Archive, "2025.json"), "synthetic archived payload");
        Directory.CreateDirectory(Path.Combine(Archive, "nested"));
        File.WriteAllText(Path.Combine(Archive, "nested", "2024.json"), "second synthetic archive payload");
        CreateDatabase();
        Host = new TestCutoverHost();
        Service = new PreProductionCutoverService(Root, Host);
    }

    public string Root { get; }
    public string Data { get; }
    public string Config { get; }
    public string Archive { get; }
    public string OneDriveRoot { get; }
    public string DatabasePath { get; }
    public Guid DeviceId { get; }
    public Guid LineageId { get; }
    public TestCutoverHost Host { get; }
    public PreProductionCutoverService Service { get; private set; }

    public string ReadOnlyStateSnapshot()
    {
        using var connection = OpenReadOnly();
        var pieces = new List<string>();
        foreach (var table in CutoverTables.All)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
            pieces.Add(table + "=" + Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT key || '=' || value FROM foundation_metadata ORDER BY key;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) pieces.Add("meta:" + reader.GetString(0));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM business_settings ORDER BY singleton_id;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                for (var i = 0; i < reader.FieldCount; i++) pieces.Add("settings:" + reader.GetName(i) + "=" + Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture));
        }
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT version || ':' || name || ':' || applied_at_utc FROM schema_migrations ORDER BY version;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) pieces.Add("migration:" + reader.GetString(0));
        }
        pieces.Add("integrity=" + Integrity(connection));
        return string.Join("\n", pieces);
    }

    public long ReadRevision()
    {
        using var connection = OpenReadOnly();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        return long.Parse(Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture);
    }

    public IReadOnlyDictionary<string, string> HashConfigurationAndSystem()
    {
        var paths = Directory.EnumerateFiles(Config, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(OneDriveRoot, "System"), "*", SearchOption.AllDirectories));
        return paths.ToDictionary(Path.GetFullPath, HashFile, StringComparer.OrdinalIgnoreCase);
    }

    public string HashDatabase() => HashFile(DatabasePath);

    public IReadOnlyDictionary<string, string> HashArchive()
        => Directory.EnumerateFiles(Archive, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(Archive, path).Replace('\\', '/'), HashFile, StringComparer.Ordinal);

    public CutoverRunResult Execute()
        => Service.Run(new(true, PreProductionCutoverService.ConfirmationToken, Root));

    public void RewriteAuthority(int phase = 3, object? transfer = null, object? recovery = null)
    {
        WriteAuthority(phase, transfer, recovery);
        Service = new PreProductionCutoverService(Root, Host);
    }

    public void CreateDatabase()
    {
        if (File.Exists(DatabasePath)) File.Delete(DatabasePath);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys=ON;
            CREATE TABLE schema_migrations(version INTEGER NOT NULL PRIMARY KEY, name TEXT NOT NULL, applied_at_utc TEXT NOT NULL);
            CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE business_settings(singleton_id INTEGER NOT NULL PRIMARY KEY CHECK(singleton_id=1), business_name TEXT NOT NULL, delivery_fee_enabled INTEGER NOT NULL, opaque_bytes BLOB NOT NULL);
            CREATE TABLE categories(category_id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE products(product_id TEXT NOT NULL PRIMARY KEY, category_id TEXT NOT NULL REFERENCES categories(category_id) ON DELETE RESTRICT, name TEXT NOT NULL);
            CREATE TABLE option_groups(group_id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE options(option_id TEXT NOT NULL PRIMARY KEY, group_id TEXT NOT NULL REFERENCES option_groups(group_id) ON DELETE RESTRICT, name TEXT NOT NULL);
            CREATE TABLE orders(order_id TEXT NOT NULL PRIMARY KEY, source_type TEXT NOT NULL, status TEXT NOT NULL);
            CREATE TABLE order_items(order_item_id TEXT NOT NULL PRIMARY KEY, order_id TEXT NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE, product_id TEXT NOT NULL REFERENCES products(product_id) ON DELETE RESTRICT);
            CREATE TABLE order_item_adjustments(adjustment_id TEXT NOT NULL PRIMARY KEY, order_item_id TEXT NOT NULL REFERENCES order_items(order_item_id) ON DELETE CASCADE);
            CREATE TABLE order_tax_breakdown(tax_id TEXT NOT NULL PRIMARY KEY, order_id TEXT NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE);
            CREATE TABLE payment_adjustments(payment_adjustment_id TEXT NOT NULL PRIMARY KEY, order_id TEXT NOT NULL REFERENCES orders(order_id) ON DELETE CASCADE);
            CREATE TABLE order_reference_sequences(business_date TEXT NOT NULL PRIMARY KEY, next_sequence INTEGER NOT NULL);
            CREATE TABLE annual_archive_completions(archive_year INTEGER NOT NULL PRIMARY KEY, archive_sha256 TEXT NOT NULL);
            CREATE TABLE annual_archive_order_proofs(order_id TEXT NOT NULL PRIMARY KEY, archive_year INTEGER NOT NULL REFERENCES annual_archive_completions(archive_year) ON DELETE RESTRICT);
            CREATE TABLE export_batches(batch_id TEXT NOT NULL PRIMARY KEY, status TEXT NOT NULL);
            CREATE TABLE export_batch_orders(batch_id TEXT NOT NULL REFERENCES export_batches(batch_id) ON DELETE CASCADE, order_id TEXT NOT NULL, PRIMARY KEY(batch_id,order_id));
            CREATE TABLE export_emissions(emission_id TEXT NOT NULL PRIMARY KEY, batch_id TEXT NOT NULL REFERENCES export_batches(batch_id) ON DELETE RESTRICT, order_id TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO foundation_metadata(key,value) VALUES('business_data_revision','41'),('synthetic_keep','yes');
            INSERT INTO business_settings VALUES(1,'Synthetic Sushi',1,X'00FF10');
            INSERT INTO categories VALUES('cat-1','Synthetic category');
            INSERT INTO products VALUES('product-1','cat-1','Synthetic product');
            INSERT INTO option_groups VALUES('group-1','Synthetic group');
            INSERT INTO options VALUES('option-1','group-1','Synthetic option');
            INSERT INTO orders VALUES('order-1','POS','OPEN'),('order-2','HIBOUTIK_PASTE','CLOSED');
            INSERT INTO order_items VALUES('item-1','order-1','product-1'),('item-2','order-2','product-1');
            INSERT INTO order_item_adjustments VALUES('adjustment-1','item-1');
            INSERT INTO order_tax_breakdown VALUES('tax-1','order-1');
            INSERT INTO payment_adjustments VALUES('payment-adjustment-1','order-1');
            INSERT INTO order_reference_sequences VALUES('2026-09-25',7);
            INSERT INTO annual_archive_completions VALUES(2025,'synthetic-sha');
            INSERT INTO annual_archive_order_proofs VALUES('order-1',2025);
            INSERT INTO export_batches VALUES('batch-1','SUCCESS');
            INSERT INTO export_batch_orders VALUES('batch-1','order-1');
            INSERT INTO export_emissions VALUES('emission-1','batch-1','order-1');
            """;
        insert.ExecuteNonQuery();
        using var migrations = connection.CreateCommand();
        migrations.CommandText = "INSERT INTO schema_migrations(version,name,applied_at_utc) VALUES($version,$name,$applied);";
        var versionParameter = migrations.Parameters.Add("$version", SqliteType.Integer);
        var nameParameter = migrations.Parameters.Add("$name", SqliteType.Text);
        var appliedParameter = migrations.Parameters.Add("$applied", SqliteType.Text);
        for (var version = 1; version <= 11; version++)
        {
            versionParameter.Value = version;
            nameParameter.Value = "synthetic-migration-" + version;
            appliedParameter.Value = "2026-01-01T00:00:00.0000000+00:00";
            migrations.ExecuteNonQuery();
        }
    }

    private void WriteAuthority(int phase, object? transfer, object? recovery)
    {
        var state = new
        {
            schemaVersion = 2,
            updatedAtUtc = "2026-09-25T00:00:00+00:00",
            protocol = new
            {
                revision = 8,
                deviceId = DeviceId,
                displayName = "Synthetic A",
                lineageId = LineageId,
                generation = 1,
                handoffVersion = 4,
                businessRevision = 41,
                phase,
                transfer,
                recovery,
                lastRecovery = (object?)null
            }
        };
        File.WriteAllText(Path.Combine(Config, "authority-state.json"), JsonSerializer.Serialize(state));
    }

    private void WriteSystemIdentity()
    {
        var system = Path.Combine(OneDriveRoot, "System");
        var lineagePath = Path.Combine(system, "Lineage", "lineage.json");
        var devicePath = Path.Combine(system, "Devices", "1", $"{DeviceId:N}.device.json");
        Directory.CreateDirectory(Path.GetDirectoryName(lineagePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(devicePath)!);
        File.WriteAllText(lineagePath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            protocolVersion = "M07",
            lineageId = LineageId,
            currentGeneration = 1,
            createdAtUtc = "2026-01-01T00:00:00+00:00"
        }));
        File.WriteAllText(devicePath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            protocolVersion = "M07",
            artifactKind = "device-membership",
            deviceId = DeviceId,
            displayName = "Synthetic A",
            lineageId = LineageId,
            generation = 1,
            registeredAtUtc = "2026-01-01T00:00:00+00:00"
        }));
    }

    private SqliteConnection OpenReadOnly()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            ForeignKeys = true,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string Integrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

internal static class CutoverTables
{
    public static readonly string[] All =
    [
        "export_emissions", "export_batch_orders", "export_batches", "annual_archive_order_proofs",
        "annual_archive_completions", "payment_adjustments", "order_item_adjustments", "order_items",
        "order_tax_breakdown", "orders", "order_reference_sequences", "options", "option_groups",
        "products", "categories"
    ];
}

internal sealed class TestCutoverHost : ICutoverHost
{
    public bool DesktopRunning { get; set; }
    public CutoverCheckpoint? DesktopStartsAt { get; set; }
    public bool WrongProvenance { get; set; }
    public CutoverCheckpoint? ThrowAt { get; set; }
    public DateTimeOffset UtcNow => new(2026, 9, 25, 20, 0, 0, TimeSpan.Zero);
    public bool IsDesktopRunning() => DesktopRunning;

    public InstalledApplicationProvenance ReadInstalledApplicationProvenance(string installDirectory)
    {
        _ = installDirectory;
        return new("Sushi81 POS", "1.0.0", "1.0.0.0",
            WrongProvenance ? "0000000000000000000000000000000000000000" : WindowsCutoverHost.AcceptedApplicationSourceHead,
            "win-x64", true);
    }

    public void Checkpoint(CutoverCheckpoint checkpoint)
    {
        if (DesktopStartsAt == checkpoint) DesktopRunning = true;
        if (ThrowAt == checkpoint) throw new IOException("Synthetic failure at " + checkpoint);
    }
}
