using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;

namespace Sushi81.Pos.Infrastructure.Migrations;

public enum InitialProductionSeedStatus { Completed, ProductionRunningOrUnknown, TargetNotPristine, SourceMissing, SourceInvalid, MigrationFailed, InstallFailed }
public sealed record InitialProductionSeedResult(InitialProductionSeedStatus Status) { public bool Succeeded => Status == InitialProductionSeedStatus.Completed; }

public interface IProductionProcessInspector { bool IsRunningOrCannotVerify(); }

public sealed class WindowsProductionProcessInspector(string localAppDataDirectory) : IProductionProcessInspector
{
    private readonly string productionExecutable = Path.GetFullPath(Path.Combine(
        localAppDataDirectory, "Programs", "Sushi81 POS", "Sushi81.Pos.Desktop.exe"));
    private readonly string preProductionExecutable = Path.GetFullPath(Path.Combine(
        localAppDataDirectory, "Programs", "Sushi81 POS PREPROD", "Sushi81.Pos.Desktop.exe"));

    public bool IsRunningOrCannotVerify()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            var currentPath = current.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(currentPath)
                || !string.Equals(Path.GetFullPath(currentPath), preProductionExecutable, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var process in Process.GetProcessesByName("Sushi81.Pos.Desktop"))
            {
                using (process)
                {
                    if (process.Id == current.Id) continue;
                    var candidatePath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(candidatePath)) return true;
                    var fullPath = Path.GetFullPath(candidatePath);
                    if (string.Equals(fullPath, productionExecutable, StringComparison.OrdinalIgnoreCase))
                        return true;
                    if (string.Equals(fullPath, preProductionExecutable, StringComparison.OrdinalIgnoreCase))
                        return true;
                    // A second same-name executable from any other path is ambiguous, so fail closed.
                    return true;
                }
            }
            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception
            or NotSupportedException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return true;
        }
    }
}

/// <summary>One-time PREPROD-only business database seed; no production files are modified or copied except a read-only SQLite snapshot.</summary>
public sealed class InitialProductionSeedService
{
    public const string ProvenanceKey = "m14_preprod_seed_provenance";
    private const string RevisionKey = "business_data_revision";
    private static readonly JsonSerializerOptions LocalSettingsOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> LocalSettingsPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "uiCulture", "oneDriveRoot", "githubOwner", "githubRepository", "githubReleaseTag", "githubReleaseName",
        "githubCredentialTarget", "deviceDisplayName", "kitchenPrinterQueueId", "kitchenPrinterQueueName",
        "customerPrinterQueueId", "customerPrinterQueueName"
    };
    private static readonly HashSet<string> FullSchema = new(StringComparer.Ordinal)
    {
        "schema_migrations", "foundation_metadata", "categories", "products", "option_groups", "options",
        "business_settings", "orders", "order_items", "order_item_adjustments", "order_tax_breakdown",
        "order_reference_sequences", "payment_adjustments", "export_batches", "export_batch_orders",
        "export_emissions", "annual_archive_completions", "annual_archive_order_proofs"
    };
    private static readonly HashSet<string> M05Schema = new(StringComparer.Ordinal)
    {
        "schema_migrations", "foundation_metadata", "categories", "products", "option_groups", "options",
        "business_settings", "orders", "order_items", "order_item_adjustments", "order_tax_breakdown",
        "order_reference_sequences", "payment_adjustments"
    };

    private readonly WindowsAppPaths target;
    private readonly string sourcePath;
    private readonly IProductionProcessInspector processInspector;
    private readonly IBusinessClock clock;
    private readonly Action<string, string> atomicInstall;
    private readonly Action? beforeFinalInstall;

    public InitialProductionSeedService(WindowsAppPaths target, IProductionProcessInspector? processInspector = null, IBusinessClock? clock = null)
        : this(target, processInspector, clock, null, null)
    {
    }

    internal InitialProductionSeedService(
        WindowsAppPaths target,
        IProductionProcessInspector? processInspector,
        IBusinessClock? clock,
        Action<string, string>? atomicInstall,
        Action? beforeFinalInstall)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
        var localAppData = Directory.GetParent(target.RootDirectory)?.FullName
            ?? throw new ArgumentException("PREPROD root has no LocalAppData parent.", nameof(target));
        sourcePath = Path.Combine(localAppData, DeploymentProfile.Production.DataRootName, "Data", "live.db");
        this.processInspector = processInspector ?? new WindowsProductionProcessInspector(localAppData);
        this.clock = clock ?? new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Local);
        this.atomicInstall = atomicInstall ?? ((source, destination) => File.Move(source, destination, overwrite: false));
        this.beforeFinalInstall = beforeFinalInstall;
    }

    public string SourceDatabasePath => sourcePath;
    public bool IsOfferEligible()
    {
        try
        {
            return target.Profile.IsPreProduction && File.Exists(sourcePath)
                && new FileInfo(sourcePath).Length > 0 && IsFixedSourcePathSafe() && IsTargetPristine(null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or System.Security.SecurityException or JsonException)
        {
            return false;
        }
    }

    public async Task<InitialProductionSeedResult> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!target.Profile.IsPreProduction) return new(InitialProductionSeedStatus.TargetNotPristine);
        var rootExisted = Directory.Exists(target.RootDirectory);
        var tempExisted = Directory.Exists(target.TempDirectory);
        var recoveryExisted = Directory.Exists(target.RecoveryDirectory);
        var stage = Path.Combine(target.TempDirectory, $"initial-prod-seed-{Guid.NewGuid():N}");
        var stagingRecovery = Path.Combine(target.RecoveryDirectory, $"preprod-seed-{Guid.NewGuid():N}");
        var staging = new StagingPaths(stage, stagingRecovery, target.TempDirectory);
        var createdDataDirectory = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(sourcePath) || new FileInfo(sourcePath).Length == 0) return new(InitialProductionSeedStatus.SourceMissing);
            if (!IsFixedSourcePathSafe()) return new(InitialProductionSeedStatus.SourceInvalid);
            if (!IsTargetPristine(null)) return new(InitialProductionSeedStatus.TargetNotPristine);
            if (processInspector.IsRunningOrCannotVerify()) return new(InitialProductionSeedStatus.ProductionRunningOrUnknown);

            Directory.CreateDirectory(staging.DataDirectory);
            await CreateSnapshotAsync(sourcePath, staging.LiveDatabasePath, cancellationToken);
            var snapshotHash = await HashAsync(staging.LiveDatabasePath, cancellationToken);
            await ValidateAsync(staging.LiveDatabasePath, latest: false, cancellationToken);

            var factory = new SqliteConnectionFactory(staging);
            var recovery = new SqliteLocalRecoverySnapshotService(staging, factory, clock);
            try
            {
                await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock, recovery).InitializeAsync(cancellationToken);
                await StampPreProductionAsync(factory, snapshotHash, clock.UtcNow, cancellationToken);
                await ValidateAsync(staging.LiveDatabasePath, latest: true, cancellationToken);
                await MakeStandaloneAsync(factory, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is SqliteException or InvalidOperationException or IOException
                or InvalidDataException or UnauthorizedAccessException) { return new(InitialProductionSeedStatus.MigrationFailed); }

            RemoveDirectory(staging.RecoveryDirectory);
            EnsureNoSidecars(staging.LiveDatabasePath);
            cancellationToken.ThrowIfCancellationRequested();
            if (processInspector.IsRunningOrCannotVerify()) return new(InitialProductionSeedStatus.ProductionRunningOrUnknown);
            beforeFinalInstall?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsTargetPristine(stage)) return new(InitialProductionSeedStatus.TargetNotPristine);

            createdDataDirectory = !Directory.Exists(target.DataDirectory);
            Directory.CreateDirectory(target.DataDirectory);
            try { atomicInstall(staging.LiveDatabasePath, target.LiveDatabasePath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                if (await IsCompletedSeedAsync(target.LiveDatabasePath)) return new(InitialProductionSeedStatus.Completed);
                if (createdDataDirectory && Directory.Exists(target.DataDirectory) && !Directory.EnumerateFileSystemEntries(target.DataDirectory).Any())
                    Directory.Delete(target.DataDirectory);
                return new(InitialProductionSeedStatus.InstallFailed);
            }
            return new(InitialProductionSeedStatus.Completed);
        }
        catch (OperationCanceledException) { return new(InitialProductionSeedStatus.InstallFailed); }
        catch (Exception exception) when (exception is IOException or SqliteException or InvalidDataException
            or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException or System.Security.SecurityException)
        { return new(InitialProductionSeedStatus.SourceInvalid); }
        finally
        {
            RemoveDirectory(stage);
            RemoveDirectory(stagingRecovery);
            if (!tempExisted) RemoveEmptyDirectory(target.TempDirectory);
            if (!recoveryExisted) RemoveEmptyDirectory(target.RecoveryDirectory);
            if (!rootExisted) RemoveEmptyDirectory(target.RootDirectory);
            if (createdDataDirectory && !File.Exists(target.LiveDatabasePath)) RemoveEmptyDirectory(target.DataDirectory);
        }
    }

    private bool IsTargetPristine(string? ownStage)
    {
        if (!target.Profile.IsPreProduction || File.Exists(target.LiveDatabasePath)) return false;
        if (!Directory.Exists(target.RootDirectory)) return true;
        if (HasReparsePoint(target.RootDirectory)) return false;
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { target.DataDirectory, target.RecoveryDirectory, target.CacheDirectory, target.LogsDirectory, target.ConfigDirectory, target.TempDirectory, target.ArchiveDirectory };
        foreach (var item in Directory.EnumerateFileSystemEntries(target.RootDirectory))
            if (!Directory.Exists(item) || !allowed.Contains(Path.GetFullPath(item)) || HasReparsePoint(item)) return false;
        if (ContainsEntries(target.DataDirectory) || ContainsEntries(target.RecoveryDirectory)
            || ContainsEntries(target.ArchiveDirectory) || ContainsEntries(target.CacheDirectory)) return false;
        if (Directory.Exists(target.TempDirectory))
            foreach (var item in Directory.EnumerateFileSystemEntries(target.TempDirectory))
                if (ownStage is null || !string.Equals(Path.GetFullPath(item), Path.GetFullPath(ownStage), StringComparison.OrdinalIgnoreCase)) return false;
        if (Directory.Exists(target.ConfigDirectory))
            foreach (var item in Directory.EnumerateFileSystemEntries(target.ConfigDirectory))
                if (Directory.Exists(item) || HasReparsePoint(item) || !string.Equals(Path.GetFileName(item), "local-settings.json", StringComparison.OrdinalIgnoreCase) || !LocalSettingsAreIsolated(item)) return false;
        return true;
    }

    private bool IsFixedSourcePathSafe()
    {
        var dataDirectory = Path.GetDirectoryName(sourcePath)!;
        var productionRoot = Directory.GetParent(dataDirectory)?.FullName;
        return productionRoot is not null
            && Directory.Exists(productionRoot)
            && Directory.Exists(dataDirectory)
            && File.Exists(sourcePath)
            && !HasReparsePoint(productionRoot)
            && !HasReparsePoint(dataDirectory)
            && !HasReparsePoint(sourcePath);
    }

    private static bool HasReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static bool LocalSettingsAreIsolated(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in document.RootElement.EnumerateObject())
                if (!LocalSettingsPropertyNames.Contains(property.Name) || !seen.Add(property.Name)) return false;

            var value = document.RootElement.Deserialize<LocalSettingsShape>(LocalSettingsOptions);
            return value is not null && string.IsNullOrWhiteSpace(value.OneDriveRoot) && string.IsNullOrWhiteSpace(value.GitHubOwner)
                && string.IsNullOrWhiteSpace(value.GitHubRepository) && string.IsNullOrWhiteSpace(value.GitHubCredentialTarget)
                && string.IsNullOrWhiteSpace(value.DeviceDisplayName)
                && string.IsNullOrWhiteSpace(value.KitchenPrinterQueueId) && string.IsNullOrWhiteSpace(value.KitchenPrinterQueueName)
                && string.IsNullOrWhiteSpace(value.CustomerPrinterQueueId) && string.IsNullOrWhiteSpace(value.CustomerPrinterQueueName)
                && IsDefaultOrBlank(value.GitHubReleaseTag, "sushi81-handoff-v1")
                && IsDefaultOrBlank(value.GitHubReleaseName, "Sushi81 POS Handoff Transport");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException) { return false; }
    }

    private static bool IsDefaultOrBlank(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, defaultValue, StringComparison.Ordinal);

    private static bool ContainsEntries(string path) => Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Any();

    private static async Task CreateSnapshotAsync(string sourcePath, string destinationPath, CancellationToken token)
    {
        await using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false }.ToString());
        await source.OpenAsync(token);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = destinationPath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Private, Pooling = false }.ToString());
        await destination.OpenAsync(token);
        source.BackupDatabase(destination);
    }

    private static async Task ValidateAsync(string path, bool latest, CancellationToken token)
    {
        await using var db = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(path, token);
        if (!string.Equals(await SqliteConnectionFactory.ExecuteScalarStringAsync(db, "PRAGMA integrity_check;", token), "ok", StringComparison.OrdinalIgnoreCase)
            || await SqliteConnectionFactory.ExecuteScalarIntAsync(db, "SELECT COUNT(*) FROM pragma_foreign_key_check;", token) != 0)
            throw new InvalidDataException("SQLite integrity validation failed.");
        var version = await SqliteConnectionFactory.ExecuteScalarIntAsync(db, "SELECT COALESCE(MAX(version),0) FROM schema_migrations;", token);
        var max = ProductionMigrations.All.Max(m => m.Version);
        if (version < 5 || version > max || (latest && version != max)) throw new InvalidDataException("Unsupported database schema version.");

        var found = new HashSet<string>(StringComparer.Ordinal);
        await using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT type,name FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND type IN ('table','view','trigger');";
            await using var reader = await cmd.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token))
            {
                var type = reader.GetString(0); var name = reader.GetString(1);
                if (type != "table" || !FullSchema.Contains(name)) throw new InvalidDataException("Unsupported SQLite schema object.");
                found.Add(name);
            }
        }
        var expected = latest ? FullSchema : M05Schema;
        if (!expected.IsSubsetOf(found) || (latest && !found.SetEquals(FullSchema))) throw new InvalidDataException("Incomplete business schema.");
        await using var meta = db.CreateCommand();
        meta.CommandText = "SELECT key FROM foundation_metadata;";
        await using var metaReader = await meta.ExecuteReaderAsync(token);
        while (await metaReader.ReadAsync(token))
        {
            var key = metaReader.GetString(0);
            if (key != RevisionKey && (latest ? key != ProvenanceKey : true)) throw new InvalidDataException("Unsupported technical database metadata.");
        }
    }

    private static async Task StampPreProductionAsync(SqliteConnectionFactory factory, string snapshotHash, DateTimeOffset seededAt, CancellationToken token)
    {
        await using var db = await factory.OpenLiveConnectionAsync(token);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = """
            INSERT INTO foundation_metadata(key,value) VALUES ($revision,'0')
              ON CONFLICT(key) DO UPDATE SET value='0';
            INSERT INTO foundation_metadata(key,value) VALUES ($provenance,$value)
              ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        cmd.Parameters.AddWithValue("$revision", RevisionKey);
        cmd.Parameters.AddWithValue("$provenance", ProvenanceKey);
        cmd.Parameters.AddWithValue("$value", $"schema=1;seededAtUtc={seededAt.ToUniversalTime():O};snapshotSha256={snapshotHash}");
        await cmd.ExecuteNonQueryAsync(token);
    }

    private static async Task<bool> IsCompletedSeedAsync(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            await ValidateAsync(path, latest: true, CancellationToken.None);
            await using var db = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(path);
            await using var command = db.CreateCommand();
            command.CommandText = "SELECT value FROM foundation_metadata WHERE key=$key;";
            command.Parameters.AddWithValue("$key", ProvenanceKey);
            var value = Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
            return value is not null && value.StartsWith("schema=1;seededAtUtc=", StringComparison.Ordinal)
                && value.Contains(";snapshotSha256=", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or SqliteException or InvalidDataException
            or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
    private static async Task MakeStandaloneAsync(SqliteConnectionFactory factory, CancellationToken token)
    {
        await using var db = await factory.OpenLiveConnectionAsync(token);
        await SqliteConnectionFactory.ExecuteNonQueryAsync(db, "PRAGMA wal_checkpoint(TRUNCATE);", token);
        if (!string.Equals(await SqliteConnectionFactory.ExecuteScalarStringAsync(db, "PRAGMA journal_mode=DELETE;", token), "delete", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Could not checkpoint the staged database.");
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    private static void EnsureNoSidecars(string path)
    {
        if (File.Exists(path + "-wal") || File.Exists(path + "-shm") || File.Exists(path + "-journal"))
            throw new InvalidDataException("Staged SQLite sidecars remain.");
    }
    private static void RemoveDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
    private static void RemoveEmptyDirectory(string path) { if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }

    private sealed record LocalSettingsShape(
        string? UiCulture,
        string? OneDriveRoot,
        string? GitHubOwner,
        string? GitHubRepository,
        string? GitHubReleaseTag,
        string? GitHubReleaseName,
        string? GitHubCredentialTarget,
        string? DeviceDisplayName,
        string? KitchenPrinterQueueId,
        string? KitchenPrinterQueueName,
        string? CustomerPrinterQueueId,
        string? CustomerPrinterQueueName);

    private sealed class StagingPaths(string root, string recoveryRoot, string tempDirectory) : IAppPaths
    {
        public DeploymentProfile Profile => DeploymentProfile.PreProduction;
        public string RootDirectory => root;
        public string DataDirectory => Path.Combine(root, "Data");
        public string RecoveryDirectory => recoveryRoot;
        public string CacheDirectory => Path.Combine(root, "Cache");
        public string LogsDirectory => Path.Combine(root, "Logs");
        public string ConfigDirectory => Path.Combine(root, "Config");
        public string TempDirectory => tempDirectory;
        public string ArchiveDirectory => Path.Combine(root, "Archive");
        public string LiveDatabasePath => Path.Combine(DataDirectory, "live.db");
        public void EnsureInitialized()
        {
            Directory.CreateDirectory(RootDirectory); Directory.CreateDirectory(DataDirectory); Directory.CreateDirectory(RecoveryDirectory);
            Directory.CreateDirectory(TempDirectory);
            Directory.CreateDirectory(CacheDirectory); Directory.CreateDirectory(LogsDirectory); Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(ArchiveDirectory);
        }
    }
}
