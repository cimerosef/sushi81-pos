using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Sushi81.Pos.PreProductionCutoverReset;

/// <summary>Owner-only, fail-closed reset of the synthetic pre-production business dataset.</summary>
public sealed class PreProductionCutoverService
{
    public const string ConfirmationToken = "DELETE-ALL-TEST-BUSINESS-DATA";
    private const int RequiredDatabaseSchemaVersion = 11;
    private const int CanonicalAuthoritySchemaVersion = 2;
    private const int AuthoritativeEnumValue = 2;
    private const int ClosedRetainedAuthorityEnumValue = 3;
    private const string AcceptedOneDriveProtocol = "M07";
    private const string CutoverBackupDirectoryName = "CutoverBackups";
    private const string BackupPrefix = "m13-preproduction-cutover-";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ResetTablesInDeleteOrder =
    [
        "export_emissions", "export_batch_orders", "export_batches",
        "annual_archive_order_proofs", "annual_archive_completions", "payment_adjustments",
        "order_item_adjustments", "order_items", "order_tax_breakdown", "orders",
        "order_reference_sequences", "options", "option_groups", "products", "categories"
    ];

    private readonly string root;
    private readonly string dataDirectory;
    private readonly string configDirectory;
    private readonly string liveDatabasePath;
    private readonly string archiveDirectory;
    private readonly ICutoverHost host;
    private readonly Action<CutoverPreflightReport>? reportWriter;

    public PreProductionCutoverService(string dataRoot, ICutoverHost host, Action<CutoverPreflightReport>? reportWriter = null)
    {
        root = Path.GetFullPath(dataRoot ?? throw new ArgumentNullException(nameof(dataRoot)));
        dataDirectory = Path.Combine(root, "Data");
        configDirectory = Path.Combine(root, "Config");
        liveDatabasePath = Path.Combine(dataDirectory, "live.db");
        archiveDirectory = Path.Combine(root, "Archive");
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.reportWriter = reportWriter;
    }

    public CutoverRunResult Run(CutoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        EnsureNoCompletedOrIncompletePriorCutover();
        if (host.IsDesktopRunning())
            throw new InvalidOperationException("Sushi81.Pos.Desktop is running. Close the application and retry after its durable files are settled.");

        ValidateRootAndDirectories();
        var installDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Sushi81 POS");
        var application = host.ReadInstalledApplicationProvenance(installDirectory);
        ValidateInstalledApplication(application);
        var authority = ReadAndValidateAuthority();
        var configurationHashes = HashDirectoryFiles(configDirectory);
        var systemHashes = HashFiles(authority.SystemIdentityPaths);
        var archiveFiles = EnumerateArchiveFiles();
        var database = ReadAndValidateDatabase();
        var report = new CutoverPreflightReport(
            root, request.Execute ? "EXECUTE" : "DRY RUN", application.SourceHeadSha, application.ProductVersion,
            authority.SchemaVersion, authority.PhaseName, database.SchemaVersion, database.Integrity,
            database.ForeignKeyViolationCount, database.OrdersByStatusAndSource, database.TableCounts,
            database.BusinessSettings.Count, database.BusinessDataRevision, archiveFiles.Count,
            configurationHashes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        reportWriter?.Invoke(report);

        if (!request.Execute)
            return new(false, report, null, null, database.BusinessDataRevision, database.BusinessDataRevision,
                "Read-only dry run complete. No backup, database, configuration, authority, or Archive files were changed.");
        if (database.TableCounts.Values.Sum() == 0 && archiveFiles.Count == 0)
            throw new InvalidOperationException("The approved pre-production dataset is already empty. Refusing a repeat cutover and revision bump.");

        return ExecuteCutover(report, database, authority, configurationHashes, systemHashes, archiveFiles);
    }

    private void ValidateRequest(CutoverRequest request)
    {
        if (!request.Execute) return;
        if (!string.Equals(request.Confirmation, ConfirmationToken, StringComparison.Ordinal))
            throw new InvalidOperationException($"Mutation requires --confirmation {ConfirmationToken}.");
        if (!string.Equals(Path.GetFullPath(request.ConfirmRoot ?? string.Empty), root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Mutation requires --confirm-root to exactly match the resolved data root: {root}");
    }

    private void ValidateRootAndDirectories()
    {
        EnsurePathAncestorsNotReparsePoint(root, "data root path");
        EnsureNotReparsePoint(root, "data root");
        if (!Directory.Exists(dataDirectory) || !Directory.Exists(configDirectory) || !Directory.Exists(archiveDirectory))
            throw new DirectoryNotFoundException("Data, Config, and Archive directories must already exist. The utility will not create or reconstruct application data directories.");
        EnsureNotReparsePoint(dataDirectory, "Data directory");
        EnsureNotReparsePoint(configDirectory, "Config directory");
        EnsureNotReparsePoint(archiveDirectory, "Archive directory");
        if (!File.Exists(liveDatabasePath) || new FileInfo(liveDatabasePath).Length <= 0)
            throw new InvalidDataException("The existing non-empty Data/live.db is required. The utility never creates or replaces it.");
        EnsureNotReparsePoint(liveDatabasePath, "live.db");
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = liveDatabasePath + suffix;
            if (File.Exists(sidecar))
                throw new InvalidDataException($"SQLite sidecar '{Path.GetFileName(sidecar)}' exists. Close/settle the application database and retry; the utility will not checkpoint or remove sidecars.");
        }
    }

    private static void ValidateInstalledApplication(InstalledApplicationProvenance application)
    {
        if (!string.Equals(application.ProductName, "Sushi81 POS", StringComparison.Ordinal)
            || !string.Equals(application.ProductVersion, "1.0.0", StringComparison.Ordinal)
            || !string.Equals(application.FileVersion, "1.0.0.0", StringComparison.Ordinal)
            || !string.Equals(application.SourceHeadSha, WindowsCutoverHost.AcceptedApplicationSourceHead, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(application.RuntimeIdentifier, "win-x64", StringComparison.Ordinal)
            || !application.SelfContained)
            throw new InvalidDataException("Installed application provenance does not match the manually accepted Sushi81 POS 1.0.0 win-x64 source head.");
    }
    private ValidatedAuthority ReadAndValidateAuthority()
    {
        var authorityPath = Path.Combine(configDirectory, "authority-state.json");
        var settingsPath = Path.Combine(configDirectory, "local-settings.json");
        if (!File.Exists(authorityPath) || !File.Exists(settingsPath))
            throw new InvalidDataException("Canonical authority-state.json and local-settings.json are required; missing local configuration fails closed.");

        using var authorityDocument = JsonDocument.Parse(File.ReadAllBytes(authorityPath));
        var authorityRoot = authorityDocument.RootElement;
        if (authorityRoot.ValueKind != JsonValueKind.Object || authorityRoot.TryGetProperty("state", out _)
            || ReadInt(authorityRoot, "schemaVersion") != CanonicalAuthoritySchemaVersion)
            throw new InvalidDataException("Authority state must use canonical schema 2 without a competing coarse state field.");
        var protocol = authorityRoot.GetProperty("protocol");
        var phase = protocol.ValueKind == JsonValueKind.Object ? ReadInt(protocol, "phase") : -1;
        if (protocol.ValueKind != JsonValueKind.Object
            || phase != AuthoritativeEnumValue && phase != ClosedRetainedAuthorityEnumValue
            || !IsNullOrMissing(protocol, "transfer") || !IsNullOrMissing(protocol, "recovery"))
            throw new InvalidDataException("Authority must be settled retained authority (Authoritative/ClosedRetainedAuthority with desktop closed) with no active Transfer or Recovery evidence.");
        var phaseName = phase switch
        {
            AuthoritativeEnumValue => "Authoritative",
            ClosedRetainedAuthorityEnumValue => "ClosedRetainedAuthority",
            _ => throw new InvalidDataException("The retained authority phase is not supported.")
        };

        var deviceId = ReadGuid(protocol, "deviceId");
        var lineageId = ReadGuid(protocol, "lineageId");
        var generation = ReadLong(protocol, "generation");
        if (deviceId == Guid.Empty || lineageId == Guid.Empty || generation < 1 || ReadLong(protocol, "revision") < 1)
            throw new InvalidDataException("The established authority device/lineage identity is incomplete.");
        _ = ReadString(protocol, "displayName");
        _ = ReadLong(protocol, "businessRevision");

        using var localConfiguration = JsonDocument.Parse(File.ReadAllBytes(settingsPath));
        var oneDriveRoot = ReadString(localConfiguration.RootElement, "oneDriveRoot");
        if (!Path.IsPathFullyQualified(oneDriveRoot) || !Directory.Exists(oneDriveRoot))
            throw new InvalidDataException("The configured OneDrive root must be present and fully qualified.");
        var systemDirectory = Path.Combine(Path.GetFullPath(oneDriveRoot), "System");
        var lineagePath = Path.Combine(systemDirectory, "Lineage", "lineage.json");
        var devicePath = Path.Combine(systemDirectory, "Devices", generation.ToString(CultureInfo.InvariantCulture), $"{deviceId:N}.device.json");
        if (!File.Exists(lineagePath) || !File.Exists(devicePath))
            throw new InvalidDataException("Matching OneDrive System lineage and current-device membership evidence is required.");
        EnsurePathAncestorsNotReparsePoint(systemDirectory, "OneDrive System path");
        EnsureNotReparsePoint(systemDirectory, "OneDrive System directory");
        EnsureNotReparsePoint(lineagePath, "OneDrive lineage file");
        EnsureNotReparsePoint(devicePath, "OneDrive device membership file");

        using var lineageDocument = JsonDocument.Parse(File.ReadAllBytes(lineagePath));
        var lineage = lineageDocument.RootElement;
        if (ReadInt(lineage, "schemaVersion") != 1
            || !string.Equals(ReadString(lineage, "protocolVersion"), AcceptedOneDriveProtocol, StringComparison.Ordinal)
            || ReadGuid(lineage, "lineageId") != lineageId || ReadLong(lineage, "currentGeneration") != generation)
            throw new InvalidDataException("OneDrive System lineage metadata does not match the established local authority identity.");

        using var deviceDocument = JsonDocument.Parse(File.ReadAllBytes(devicePath));
        var device = deviceDocument.RootElement;
        if (ReadInt(device, "schemaVersion") != 1
            || !string.Equals(ReadString(device, "protocolVersion"), AcceptedOneDriveProtocol, StringComparison.Ordinal)
            || !string.Equals(ReadString(device, "artifactKind"), "device-membership", StringComparison.Ordinal)
            || ReadGuid(device, "deviceId") != deviceId || ReadGuid(device, "lineageId") != lineageId
            || ReadLong(device, "generation") != generation)
            throw new InvalidDataException("OneDrive System device membership does not match the established local authority identity.");
        return new(2, phaseName, lineagePath, devicePath);
    }

    private DatabaseSnapshot ReadAndValidateDatabase()
    {
        using var connection = OpenDatabase(liveDatabasePath, SqliteOpenMode.ReadOnly);
        var integrity = ReadIntegrity(connection, null);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PRAGMA integrity_check did not return exactly 'ok'.");
        var foreignKeyViolations = CountForeignKeyViolations(connection, null);
        if (foreignKeyViolations != 0) throw new InvalidDataException($"PRAGMA foreign_key_check returned {foreignKeyViolations} violation(s).");
        foreach (var table in ResetTablesInDeleteOrder)
            if (!TableExists(connection, null, table)) throw new InvalidDataException($"Required M13 reset table '{table}' is missing.");
        if (!TableExists(connection, null, "business_settings") || !TableExists(connection, null, "foundation_metadata")
            || !TableExists(connection, null, "schema_migrations"))
            throw new InvalidDataException("Required business settings, foundation metadata, or schema migration structure is missing.");

        var migrationRows = ReadMigrationRows(connection, null);
        if (migrationRows.Count != RequiredDatabaseSchemaVersion
            || migrationRows.Where((row, index) => row.Version != index + 1).Any()
            || migrationRows[^1].Version != RequiredDatabaseSchemaVersion)
            throw new InvalidDataException("The production schema migration history must be contiguous and end exactly at version 11.");
        var tableCounts = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in ResetTablesInDeleteOrder)
            tableCounts.Add(table, ExecuteScalarLong(connection, null, $"SELECT COUNT(*) FROM \"{table}\";"));
        var businessSettings = ReadBusinessSettings(connection, null);
        if (businessSettings.Count != 1)
            throw new InvalidDataException($"Exactly one business_settings row must exist and be preserved; found {businessSettings.Count}.");
        return new(RequiredDatabaseSchemaVersion, integrity, foreignKeyViolations,
            ReadOrdersByStatusAndSource(connection, null), tableCounts, businessSettings, migrationRows,
            ReadBusinessDataRevision(connection, null));
    }
    private CutoverRunResult ExecuteCutover(
        CutoverPreflightReport report,
        DatabaseSnapshot database,
        ValidatedAuthority authority,
        IReadOnlyDictionary<string, string> configurationHashes,
        IReadOnlyDictionary<string, string> systemHashes,
        IReadOnlyList<ArchiveFileHash> archiveFiles)
    {
        var backupParent = Path.Combine(root, CutoverBackupDirectoryName);
        EnsureSameVolume(root, backupParent, archiveDirectory);
        if (Directory.Exists(backupParent)) EnsureNotReparsePoint(backupParent, "cutover backup parent");
        Directory.CreateDirectory(backupParent);
        var backupName = "m13-preproduction-cutover-" + host.UtcNow.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
        var backupDirectory = Path.Combine(backupParent, backupName);
        var backupDatabasePath = Path.Combine(backupDirectory, "live.db");
        var backupArchiveDirectory = Path.Combine(backupDirectory, "Archive");
        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        var archiveMutationStarted = false;
        var commitAttempted = false;
        var databaseBackupHash = string.Empty;
        var manifest = new BackupManifest(
            "Preparing", host.UtcNow, report, database.BusinessDataRevision, null, string.Empty,
            archiveFiles, configurationHashes, systemHashes, "Backup is not yet complete.")
        {
            ArchiveBackupDirectory = "Archive"
        };

        try
        {
            EnsurePathAncestorsNotReparsePoint(backupParent, "cutover backup path");
            host.Checkpoint(CutoverCheckpoint.BeforeBackupCreation);
            Directory.CreateDirectory(backupDirectory);
            CopyFileSet(archiveFiles, archiveDirectory, backupArchiveDirectory);
            VerifyFileSet(archiveFiles, backupArchiveDirectory);
            CreateSqliteBackup(liveDatabasePath, backupDatabasePath);
            databaseBackupHash = ComputeSha256(backupDatabasePath);
            ValidateDatabaseFile(backupDatabasePath);
            manifest = manifest with
            {
                State = "BackupPrepared",
                LiveDatabaseBackupSha256 = databaseBackupHash,
                Note = "SQLite-consistent live.db backup and SHA-256 created; local Archive file backup verified."
            };
            WriteManifest(manifestPath, manifest);
            host.Checkpoint(CutoverCheckpoint.AfterBackupCreation);

            VerifySnapshotsUnchanged(configurationHashes, systemHashes, authority);
            if (!FileSetEquals(archiveFiles, EnumerateArchiveFiles()))
                throw new InvalidDataException("Local Archive changed after preflight; no business-data mutation was started.");
            host.Checkpoint(CutoverCheckpoint.BeforeArchiveStaging);
            VerifyFileSet(archiveFiles, backupArchiveDirectory);
            manifest = manifest with { State = "ArchiveBackupReady", Note = "Every active Archive file has a verified local backup copy before database mutation." };
            WriteManifest(manifestPath, manifest);
            host.Checkpoint(CutoverCheckpoint.AfterArchiveStaging);
            EnsureDesktopClosed();

            using (var connection = OpenDatabase(liveDatabasePath, SqliteOpenMode.ReadWrite))
            using (var transaction = connection.BeginTransaction())
            {
                try
                {
                    var currentSettings = ReadBusinessSettings(connection, transaction);
                    var currentMigrations = ReadMigrationRows(connection, transaction);
                    var currentRevision = ReadBusinessDataRevision(connection, transaction);
                    if (!RowsEqual(database.BusinessSettings, currentSettings)
                        || !MigrationRowsEqual(database.Migrations, currentMigrations)
                        || currentRevision != database.BusinessDataRevision)
                        throw new InvalidDataException("Database preservation state changed between preflight and transaction start.");

                    foreach (var table in ResetTablesInDeleteOrder)
                        ExecuteNonQuery(connection, transaction, $"DELETE FROM \"{table}\";");
                    AdvanceBusinessDataRevision(connection, transaction, currentRevision);
                    host.Checkpoint(CutoverCheckpoint.DuringDatabaseTransaction);

                    if (ResetTablesInDeleteOrder.Any(table => ExecuteScalarLong(connection, transaction, $"SELECT COUNT(*) FROM \"{table}\";") != 0))
                        throw new InvalidDataException("A reset table retained rows inside the reset transaction.");
                    if (!RowsEqual(database.BusinessSettings, ReadBusinessSettings(connection, transaction)))
                        throw new InvalidDataException("Business settings changed during the reset transaction.");
                    if (!MigrationRowsEqual(database.Migrations, ReadMigrationRows(connection, transaction)))
                        throw new InvalidDataException("Schema migration history changed during the reset transaction.");
                    if (ReadBusinessDataRevision(connection, transaction) != checked(database.BusinessDataRevision + 1))
                        throw new InvalidDataException("business_data_revision did not advance exactly once.");
                    if (!string.Equals(ReadIntegrity(connection, transaction), "ok", StringComparison.OrdinalIgnoreCase)
                        || CountForeignKeyViolations(connection, transaction) != 0)
                        throw new InvalidDataException("Database integrity or foreign keys failed inside the reset transaction.");

                    EnsureDesktopClosed();
                    archiveMutationStarted = true;
                    ClearActiveArchive(archiveFiles, backupArchiveDirectory);
                    host.Checkpoint(CutoverCheckpoint.BeforeDatabaseCommit);
                    EnsureDesktopClosed();
                    VerifySnapshotsUnchanged(configurationHashes, systemHashes, authority);
                    commitAttempted = true;
                    transaction.Commit();
                }
                catch
                {
                    try { transaction.Rollback(); }
                    catch { /* A verified local backup remains available if rollback itself fails. */ }
                    throw;
                }
            }

            using (var verification = OpenDatabase(liveDatabasePath, SqliteOpenMode.ReadOnly))
            {
                if (!string.Equals(ReadIntegrity(verification, null), "ok", StringComparison.OrdinalIgnoreCase)
                    || CountForeignKeyViolations(verification, null) != 0)
                    throw new InvalidDataException("Post-commit SQLite integrity verification failed.");
                if (ResetTablesInDeleteOrder.Any(table => ExecuteScalarLong(verification, null, $"SELECT COUNT(*) FROM \"{table}\";") != 0))
                    throw new InvalidDataException("Post-commit verification found rows in an approved reset table.");
                if (!RowsEqual(database.BusinessSettings, ReadBusinessSettings(verification, null))
                    || !MigrationRowsEqual(database.Migrations, ReadMigrationRows(verification, null))
                    || ReadBusinessDataRevision(verification, null) != checked(database.BusinessDataRevision + 1))
                    throw new InvalidDataException("Post-commit settings, migration history, or revision verification failed.");
            }

            if (EnumerateArchiveFiles().Count != 0)
                throw new InvalidDataException("Post-commit verification found files in the active Archive directory.");
            VerifySnapshotsUnchanged(configurationHashes, systemHashes, authority);
            manifest = manifest with
            {
                State = "Complete",
                BusinessDataRevisionAfter = checked(database.BusinessDataRevision + 1),
                Note = "Cutover completed. Reset data was verified empty; settings, migration history, authority/configuration and System membership were preserved."
            };
            WriteManifest(manifestPath, manifest);
            return new(true, report, backupDirectory, databaseBackupHash,
                database.BusinessDataRevision, checked(database.BusinessDataRevision + 1),
                "Cutover completed and verified. Test-era Recovery and remote disaster-recovery/handoff artifacts were preserved and may remain temporarily.");
        }
        catch (Exception operationError)
        {
            var rollbackFailures = new List<string>();
            if (commitAttempted)
            {
                try { RestoreDatabaseFromBackup(backupDatabasePath, databaseBackupHash); }
                catch (Exception exception) { rollbackFailures.Add("live.db restoration failed: " + exception.Message); }
            }
            if (archiveMutationStarted)
            {
                try { RestoreArchiveFromBackup(archiveFiles, backupArchiveDirectory); }
                catch (Exception exception) { rollbackFailures.Add("Archive restoration failed: " + exception.Message); }
            }

            if (rollbackFailures.Count == 0 && File.Exists(manifestPath))
            {
                try
                {
                    manifest = manifest with { State = "RolledBack", Note = "Operation failed and the pre-cutover state was restored. " + operationError.Message };
                    WriteManifest(manifestPath, manifest);
                }
                catch (Exception exception) { rollbackFailures.Add("rollback manifest update failed: " + exception.Message); }
            }
            if (rollbackFailures.Count > 0)
                throw new InvalidOperationException($"Cutover failed: {operationError.Message} Rollback also failed: {string.Join("; ", rollbackFailures)}. Keep the local backup at '{backupDirectory}', leave Sushi81 POS closed, and request controller-guided recovery.", operationError);
            throw new InvalidOperationException($"Cutover failed and was rolled back: {operationError.Message} Local backup retained at '{backupDirectory}'.", operationError);
        }
    }

    private void EnsureNoCompletedOrIncompletePriorCutover()
    {
        var backupParent = Path.Combine(root, CutoverBackupDirectoryName);
        if (!Directory.Exists(backupParent)) return;
        EnsureNotReparsePoint(backupParent, "cutover backup parent");
        foreach (var directory in Directory.EnumerateDirectories(backupParent, "m13-preproduction-cutover-*", SearchOption.TopDirectoryOnly))
        {
            EnsureNotReparsePoint(directory, "prior cutover backup");
            var manifestPath = Path.Combine(directory, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException($"An incomplete prior cutover backup exists at '{directory}'. Leave the application closed and request controller-guided recovery.");
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var state = ReadString(manifest.RootElement, "state");
            if (string.Equals(state, "Complete", StringComparison.Ordinal))
                throw new InvalidOperationException($"A successful one-time pre-production cutover is already recorded at '{directory}'. This utility permanently refuses a second cutover, even if data is later reintroduced.");
            if (!string.Equals(state, "RolledBack", StringComparison.Ordinal))
                throw new InvalidDataException($"Prior cutover backup '{directory}' has unresolved state '{state}'. Leave the application closed and request controller-guided recovery.");
        }
    }

    private void EnsureDesktopClosed()
    {
        if (host.IsDesktopRunning())
            throw new InvalidOperationException("Sushi81.Pos.Desktop started during cutover. The operation is stopping and will restore from its verified local backup where required.");
    }

    private void VerifySnapshotsUnchanged(
        IReadOnlyDictionary<string, string> originalConfigurationHashes,
        IReadOnlyDictionary<string, string> originalSystemHashes,
        ValidatedAuthority authority)
    {
        if (!DictionaryEqual(originalConfigurationHashes, HashDirectoryFiles(configDirectory)))
            throw new InvalidDataException("A Config file hash changed during the cutover; authority/configuration state was not written by this utility.");
        if (!DictionaryEqual(originalSystemHashes, HashFiles(authority.SystemIdentityPaths)))
            throw new InvalidDataException("OneDrive System lineage/device membership hashes changed during the cutover.");
    }

    private static void CreateSqliteBackup(string sourcePath, string destinationPath)
    {
        using var source = OpenDatabase(sourcePath, SqliteOpenMode.ReadOnly);
        using var destination = OpenDatabase(destinationPath, SqliteOpenMode.ReadWriteCreate);
        source.BackupDatabase(destination);
    }

    private void RestoreDatabaseFromBackup(string backupPath, string expectedSha256)
    {
        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length <= 0)
            throw new InvalidDataException("The verified live.db rollback backup is missing or empty.");
        if (!string.Equals(ComputeSha256(backupPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The verified live.db rollback backup changed after preflight; it was not restored.");
        ValidateDatabaseFile(backupPath);
        using var source = OpenDatabase(backupPath, SqliteOpenMode.ReadOnly);
        using var destination = OpenDatabase(liveDatabasePath, SqliteOpenMode.ReadWrite);
        source.BackupDatabase(destination);
    }

    private static void ValidateDatabaseFile(string path)
    {
        using var connection = OpenDatabase(path, SqliteOpenMode.ReadOnly);
        if (!string.Equals(ReadIntegrity(connection, null), "ok", StringComparison.OrdinalIgnoreCase)
            || CountForeignKeyViolations(connection, null) != 0)
            throw new InvalidDataException("The local SQLite rollback backup failed integrity or foreign-key validation.");
    }
    private void ClearActiveArchive(IReadOnlyList<ArchiveFileHash> files, string backupArchiveDirectory)
    {
        VerifyFileSet(files, backupArchiveDirectory);
        if (!FileSetEquals(files, EnumerateArchiveFiles()))
            throw new InvalidDataException("Active Archive changed after its verified backup; refusing to remove it.");
        foreach (var file in files)
            File.Delete(SafeChildPath(archiveDirectory, file.RelativePath));
        foreach (var directory in EnumerateDirectoriesDeepestFirst(archiveDirectory))
            Directory.Delete(directory, recursive: false);
        if (EnumerateArchiveFiles().Count != 0)
            throw new InvalidDataException("Active Archive did not become empty after reset.");
    }

    private void RestoreArchiveFromBackup(IReadOnlyList<ArchiveFileHash> files, string backupArchiveDirectory)
    {
        VerifyFileSet(files, backupArchiveDirectory);
        Directory.CreateDirectory(archiveDirectory);
        foreach (var file in files)
        {
            var sourcePath = SafeChildPath(backupArchiveDirectory, file.RelativePath);
            var destinationPath = SafeChildPath(archiveDirectory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            if (File.Exists(destinationPath))
            {
                if (!string.Equals(ComputeSha256(destinationPath), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"A different file now occupies Archive/{file.RelativePath}; it was not overwritten.");
            }
            else File.Copy(sourcePath, destinationPath, overwrite: false);
        }
        if (!FileSetEquals(files, EnumerateArchiveFiles()))
            throw new InvalidDataException("Archive rollback did not restore the exact pre-cutover file set.");
    }

    private List<ArchiveFileHash> EnumerateArchiveFiles()
    {
        return EnumerateFilesWithoutReparsePoints(archiveDirectory)
            .Select(path => new ArchiveFileHash(
                Path.GetRelativePath(archiveDirectory, path).Replace((char)92, '/'),
                new FileInfo(path).Length,
                ComputeSha256(path)))
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static void CopyFileSet(IReadOnlyList<ArchiveFileHash> files, string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var file in files)
        {
            var sourcePath = SafeChildPath(sourceRoot, file.RelativePath);
            var destinationPath = SafeChildPath(destinationRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
            if (new FileInfo(destinationPath).Length != file.Size
                || !string.Equals(ComputeSha256(destinationPath), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Archive backup verification failed for '{file.RelativePath}'.");
        }
    }

    private static void VerifyFileSet(IReadOnlyList<ArchiveFileHash> expected, string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            if (expected.Count == 0) return;
            throw new DirectoryNotFoundException("The verified local Archive backup directory is missing.");
        }
        var actual = EnumerateFilesWithoutReparsePoints(rootPath)
            .Select(path => new ArchiveFileHash(
                Path.GetRelativePath(rootPath, path).Replace((char)92, '/'),
                new FileInfo(path).Length,
                ComputeSha256(path)))
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        if (!FileSetEquals(expected, actual)) throw new InvalidDataException("The local Archive backup file set or hashes do not match preflight.");
    }

    private static void WriteManifest(string path, BackupManifest manifest)
    {
        var temporaryPath = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static SqliteConnection OpenDatabase(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5,
            ForeignKeys = true
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string ReadIntegrity(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA integrity_check;";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read()) rows.Add(reader.GetString(0));
        return rows.Count == 1 ? rows[0] : $"unexpected-result-count:{rows.Count}";
    }

    private static int CountForeignKeyViolations(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA foreign_key_check;";
        using var reader = command.ExecuteReader();
        var count = 0;
        while (reader.Read()) count++;
        return count;
    }

    private static bool TableExists(SqliteConnection connection, SqliteTransaction? transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
    }

    private static long ExecuteScalarLong(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static List<MigrationRow> ReadMigrationRows(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version,name,applied_at_utc FROM schema_migrations ORDER BY version;";
        using var reader = command.ExecuteReader();
        var result = new List<MigrationRow>();
        while (reader.Read()) result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private static List<List<SqliteCell>> ReadBusinessSettings(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM business_settings ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        var rows = new List<List<SqliteCell>>();
        while (reader.Read())
        {
            var row = new List<SqliteCell>();
            for (var index = 0; index < reader.FieldCount; index++)
            {
                var value = reader.GetValue(index);
                var storageType = value is DBNull ? "null" : value.GetType().Name;
                var serialized = value switch
                {
                    DBNull => string.Empty,
                    byte[] bytes => Convert.ToBase64String(bytes),
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
                };
                row.Add(new(reader.GetName(index), storageType, serialized));
            }
            rows.Add(row);
        }
        return rows;
    }

    private static List<OrderStatusSourceCount> ReadOrdersByStatusAndSource(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT status,source_type,COUNT(*) FROM orders GROUP BY status,source_type ORDER BY status,source_type;";
        using var reader = command.ExecuteReader();
        var result = new List<OrderStatusSourceCount>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2)));
        return result;
    }

    private static long ReadBusinessDataRevision(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM foundation_metadata WHERE key='business_data_revision';";
        var value = command.ExecuteScalar();
        if (value is null or DBNull)
            throw new InvalidDataException("foundation_metadata.business_data_revision is missing; refusing to infer a reset revision.");
        if (!long.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out var revision)
            || revision < 0)
            throw new InvalidDataException("foundation_metadata.business_data_revision is malformed or negative.");
        return revision;
    }

    private static void AdvanceBusinessDataRevision(SqliteConnection connection, SqliteTransaction transaction, long priorRevision)
    {
        var nextRevision = checked(priorRevision + 1);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO foundation_metadata(key,value) VALUES('business_data_revision',$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
        command.Parameters.AddWithValue("$value", nextRevision.ToString(CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static SortedDictionary<string, string> HashDirectoryFiles(string directory)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateFilesWithoutReparsePoints(directory))
        {
            var relative = Path.GetRelativePath(directory, path).Replace((char)92, '/');
            result.Add(relative, ComputeSha256(path));
        }
        return result;
    }

    private static SortedDictionary<string, string> HashFiles(IEnumerable<string> files)
    {
        var result = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in files) result.Add(Path.GetFullPath(path), ComputeSha256(path));
        return result;
    }

    private static bool DictionaryEqual(IReadOnlyDictionary<string, string> left, SortedDictionary<string, string> right)
    {
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase));
    }

    private static bool FileSetEquals(IReadOnlyList<ArchiveFileHash> left, IReadOnlyList<ArchiveFileHash> right)
    {
        if (left.Count != right.Count) return false;
        var rightByPath = right.ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        return left.All(item => rightByPath.TryGetValue(item.RelativePath, out var value)
            && item.Size == value.Size
            && string.Equals(item.Sha256, value.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RowsEqual(IReadOnlyList<List<SqliteCell>> left, IReadOnlyList<List<SqliteCell>> right)
    {
        return left.Count == right.Count && left.Zip(right).All(pair => pair.First.SequenceEqual(pair.Second));
    }

    private static bool MigrationRowsEqual(IReadOnlyList<MigrationRow> left, IReadOnlyList<MigrationRow> right)
        => left.SequenceEqual(right);

    private static List<string> EnumerateFilesWithoutReparsePoints(string directory)
    {
        EnsureNotReparsePoint(directory, directory);
        var result = new List<string>();
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                EnsureNotReparsePoint(entry, entry);
                if (Directory.Exists(entry)) pending.Push(entry);
                else if (File.Exists(entry)) result.Add(Path.GetFullPath(entry));
                else throw new IOException($"Archive/configuration entry '{entry}' is not a regular file or directory.");
            }
        }
        return result;
    }

    private static string[] EnumerateDirectoriesDeepestFirst(string rootDirectory)
    {
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(current))
            {
                EnsureNotReparsePoint(child, "Archive subdirectory");
                directories.Add(Path.GetFullPath(child));
                pending.Push(child);
            }
        }

        return directories
            .OrderByDescending(path => path.Length)
            .ToArray();
    }

    private static string SafeChildPath(string directory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("An archived relative path is empty or rooted.");
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace((char)92, Path.DirectorySeparatorChar);
        if (normalized.Split(Path.DirectorySeparatorChar).Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("An archived relative path contains an unsafe component.");
        var rootPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(directory, normalized));
        if (!result.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An archived path escapes its validated root.");
        return result;
    }

    private static void EnsureNotReparsePoint(string path, string description)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"The {description} is a reparse point. The cutover utility refuses redirected paths.");
    }

    private static void EnsurePathAncestorsNotReparsePoint(string path, string description)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
                EnsureNotReparsePoint(current, description);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent)
                || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }

    private static void EnsureSameVolume(string first, string second, string third)
    {
        var firstRoot = Path.GetPathRoot(Path.GetFullPath(first));
        if (firstRoot is null
            || !string.Equals(firstRoot, Path.GetPathRoot(Path.GetFullPath(second)), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(firstRoot, Path.GetPathRoot(Path.GetFullPath(third)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The local rollback backup and active Archive must be on the same volume for rollback-safe staging.");
    }
    private static int ReadInt(JsonElement element, string property) => element.GetProperty(property).GetInt32();
    private static long ReadLong(JsonElement element, string property) => element.GetProperty(property).GetInt64();

    private static Guid ReadGuid(JsonElement element, string property)
    {
        var text = ReadString(element, property);
        return Guid.TryParse(text, out var value)
            ? value
            : throw new InvalidDataException($"Authority/configuration field '{property}' is not a valid GUID.");
    }

    private static string ReadString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Authority/configuration field '{property}' is missing or is not text.");
        return value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException($"Authority/configuration field '{property}' is empty.");
    }

    private static bool IsNullOrMissing(JsonElement element, string property)
        => !element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null;

    private sealed record ValidatedAuthority(int SchemaVersion, string PhaseName, string LineagePath, string DevicePath)
    {
        public IReadOnlyList<string> SystemIdentityPaths => [LineagePath, DevicePath];
    }

    private sealed record DatabaseSnapshot(
        int SchemaVersion,
        string Integrity,
        int ForeignKeyViolationCount,
        IReadOnlyList<OrderStatusSourceCount> OrdersByStatusAndSource,
        IReadOnlyDictionary<string, long> TableCounts,
        IReadOnlyList<List<SqliteCell>> BusinessSettings,
        IReadOnlyList<MigrationRow> Migrations,
        long BusinessDataRevision);

    private sealed record SqliteCell(string Column, string StorageType, string Value);
    private sealed record MigrationRow(int Version, string Name, string AppliedAtUtc);
    private sealed record ArchiveFileHash(string RelativePath, long Size, string Sha256);

    private sealed record BackupManifest(
        string State,
        DateTimeOffset CreatedAtUtc,
        CutoverPreflightReport Preflight,
        long BusinessDataRevisionBefore,
        long? BusinessDataRevisionAfter,
        string LiveDatabaseBackupSha256,
        IReadOnlyList<ArchiveFileHash> ArchiveFiles,
        IReadOnlyDictionary<string, string> ConfigurationHashes,
        IReadOnlyDictionary<string, string> SystemIdentityHashes,
        string Note)
    {
        public string? ArchiveBackupDirectory { get; init; }
    }
}
