using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.PreProductionCutoverReset;

/// <summary>Owner-only, fail-closed reset of the synthetic pre-production business dataset.</summary>
public sealed class PreProductionCutoverService
{
    public const string ConfirmationToken = "DELETE-ALL-TEST-BUSINESS-DATA";
    private const int RequiredDatabaseSchemaVersion = 11;
    private const int CanonicalAuthoritySchemaVersion = 2;
    private const string AcceptedOneDriveProtocol = "M07";
    private const string CutoverBackupDirectoryName = "CutoverBackups";
    private const string AuthorityStateFileName = "authority-state.json";
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
        var systemHashes = HashDirectoryFiles(authority.SystemDirectoryPath);
        var archiveFiles = EnumerateArchiveFiles();
        var database = ReadAndValidateDatabase();
        if (authority.Protocol.BusinessRevision > database.BusinessDataRevision)
            throw new InvalidDataException("Authority BusinessRevision is greater than the canonical live database revision; refusing contradictory authority metadata.");
        _ = checked(database.BusinessDataRevision + 1);
        _ = checked(authority.Protocol.Revision + 1);
        var report = new CutoverPreflightReport(
            root, request.Execute ? "EXECUTE" : "DRY RUN", application.SourceHeadSha, application.ProductVersion,
            authority.SchemaVersion, authority.PhaseName, authority.Protocol.Revision, authority.Protocol.BusinessRevision,
            database.SchemaVersion, database.Integrity,
            database.ForeignKeyViolationCount, database.OrdersByStatusAndSource, database.TableCounts,
            database.BusinessSettings.Count, database.BusinessDataRevision, archiveFiles.Count,
            configurationHashes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        reportWriter?.Invoke(report);

        if (!request.Execute)
            return new(false, report, null, null, database.BusinessDataRevision, database.BusinessDataRevision,
                "Read-only dry run complete. No backup, database, configuration, authority, or Archive files were changed.",
                authority.Protocol.Revision, authority.Protocol.Revision,
                authority.Protocol.BusinessRevision, authority.Protocol.BusinessRevision, null);
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
        var authorityPath = Path.Combine(configDirectory, AuthorityStateFileName);
        var settingsPath = Path.Combine(configDirectory, "local-settings.json");
        if (!File.Exists(authorityPath) || !File.Exists(settingsPath))
            throw new InvalidDataException("Canonical authority-state.json and local-settings.json are required; missing local configuration fails closed.");

        EnsureNotReparsePoint(authorityPath, "authority-state.json");
        var authorityBytes = File.ReadAllBytes(authorityPath);
        using var authorityDocument = JsonDocument.Parse(authorityBytes);
        var authorityRoot = authorityDocument.RootElement;
        if (authorityRoot.ValueKind != JsonValueKind.Object || authorityRoot.TryGetProperty("state", out _)
            || ReadInt(authorityRoot, "schemaVersion") != CanonicalAuthoritySchemaVersion)
            throw new InvalidDataException("Authority state must use canonical schema 2 without a competing coarse state field.");

        var document = DeserializeCanonicalAuthority(authorityBytes, authorityPath);
        var protocol = document.Protocol!;
        protocol.Validate();
        if (protocol.Phase is not (AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority)
            || protocol.Transfer is not null || protocol.Recovery is not null)
            throw new InvalidDataException("Authority must be settled retained authority (Authoritative/ClosedRetainedAuthority with desktop closed) with no active Transfer or Recovery evidence.");

        if (document.SchemaVersion != CanonicalAuthoritySchemaVersion
            || document.State != WriteAuthorityState.Authoritative || document.UpdatedAtUtc == default)
            throw new InvalidDataException("Canonical authority document has an invalid schema, derived write state, or update timestamp.");

        var deviceId = protocol.DeviceId;
        var lineageId = protocol.LineageId!.Value;
        var generation = protocol.Generation;

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
        return new(document, authorityPath, Path.GetRelativePath(configDirectory, authorityPath).Replace((char)92, '/'),
            ComputeSha256(authorityPath), systemDirectory, lineagePath, devicePath);
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
            ReadBusinessDataRevision(connection, null), ComputeSha256(liveDatabasePath));
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
        var backupAuthorityPath = Path.Combine(backupDirectory, AuthorityStateFileName);
        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        var archiveMutationStarted = false;
        var commitAttempted = false;
        var authorityMutationStarted = false;
        var databaseBackupHash = string.Empty;
        string? authorityStateAfterSha256 = null;
        long? authorityProtocolRevisionAfter = null;
        long? authorityBusinessRevisionAfter = null;
        var targetDatabaseRevision = checked(database.BusinessDataRevision + 1);
        var targetAuthorityProtocolRevision = checked(authority.Protocol.Revision + 1);
        var manifest = new BackupManifest(
            "Preparing", host.UtcNow, report, database.BusinessDataRevision, null, string.Empty,
            archiveFiles, configurationHashes, systemHashes, "Backup is not yet complete.")
        {
            ArchiveBackupDirectory = "Archive",
            AuthorityStateBackupFileName = AuthorityStateFileName,
            AuthorityStateBackupSha256 = authority.StateSha256,
            AuthorityStateBeforeSha256 = authority.StateSha256,
            AuthorityProtocolRevisionBefore = authority.Protocol.Revision,
            AuthorityBusinessRevisionBefore = authority.Protocol.BusinessRevision,
            RollbackProvenance = "Restore live.db from live.db; restore Archive from Archive/ using the recorded file hashes; restore Config/authority-state.json from authority-state.json and verify AuthorityStateBackupSha256. All backups are private local owner data and must never be uploaded."
        };

        try
        {
            EnsurePathAncestorsNotReparsePoint(backupParent, "cutover backup path");
            host.Checkpoint(CutoverCheckpoint.BeforeBackupCreation);
            Directory.CreateDirectory(backupDirectory);
            CopyFileSet(archiveFiles, archiveDirectory, backupArchiveDirectory);
            VerifyFileSet(archiveFiles, backupArchiveDirectory);
            CopyFileDurably(authority.AuthorityStatePath, backupAuthorityPath, overwrite: false);
            if (!string.Equals(ComputeSha256(backupAuthorityPath), authority.StateSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The exact private authority-state.json backup does not match the validated pre-cutover authority file.");
            CreateSqliteBackup(liveDatabasePath, backupDatabasePath);
            databaseBackupHash = ComputeSha256(backupDatabasePath);
            if (!string.Equals(databaseBackupHash, database.FileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The exact live.db backup does not match the preflight database bytes.");
            ValidateDatabaseFile(backupDatabasePath);
            manifest = manifest with
            {
                State = "BackupPrepared",
                LiveDatabaseBackupSha256 = databaseBackupHash,
                AuthorityStateBackupSha256 = ComputeSha256(backupAuthorityPath),
                Note = "Exact authority-state.json backup, SQLite-validated live.db backup, and local Archive file backup were created and verified before mutation."
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
            if (!string.Equals(ComputeSha256(liveDatabasePath), database.FileSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("live.db bytes changed after preflight and backup; refusing to start the reset transaction.");
            manifest = manifest with
            {
                State = "MutationInProgress",
                Note = "Verified backups are complete. Database, active Archive, and authority-state synchronization are now one rollback-coordinated cutover operation."
            };
            WriteManifest(manifestPath, manifest);

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
                        || currentRevision != database.BusinessDataRevision
                        || !TableCountsEqual(database.TableCounts, connection, transaction)
                        || !database.OrdersByStatusAndSource.SequenceEqual(ReadOrdersByStatusAndSource(connection, transaction)))
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
                    if (ReadBusinessDataRevision(connection, transaction) != targetDatabaseRevision)
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

            manifest = manifest with
            {
                State = "DatabaseArchiveCommittedAuthorityPending",
                BusinessDataRevisionAfter = targetDatabaseRevision,
                Note = "Database reset and Archive removal committed. The verified authority backup remains available until authority synchronization and final verification complete."
            };
            WriteManifest(manifestPath, manifest);

            VerifyPostCutoverDatabase(database, targetDatabaseRevision);

            if (EnumerateArchiveFiles().Count != 0)
                throw new InvalidDataException("Post-commit verification found files in the active Archive directory.");
            VerifySnapshotsUnchanged(configurationHashes, systemHashes, authority);

            var updatedAtUtc = host.UtcNow.ToUniversalTime();
            if (updatedAtUtc <= authority.Document.UpdatedAtUtc)
                throw new InvalidDataException("The cutover clock did not advance authority UpdatedAtUtc; refusing to write a non-advancing timestamp.");
            var synchronizedProtocol = authority.Protocol with
            {
                Revision = targetAuthorityProtocolRevision,
                BusinessRevision = targetDatabaseRevision
            };
            synchronizedProtocol.Validate();
            if (synchronizedProtocol.WriteState != WriteAuthorityState.Authoritative)
                throw new InvalidDataException("The synchronized canonical authority document does not derive an authoritative write state.");
            var synchronizedAuthority = new AuthorityStateDocument(
                CanonicalAuthoritySchemaVersion, synchronizedProtocol.WriteState, updatedAtUtc)
            {
                Protocol = synchronizedProtocol
            };
            var synchronizedAuthorityBytes = SerializeCanonicalAuthority(synchronizedAuthority);
            var expectedAuthorityStateSha256 = ComputeSha256(synchronizedAuthorityBytes);

            VerifySnapshotsUnchanged(configurationHashes, systemHashes, authority);
            EnsureDesktopClosed();
            host.Checkpoint(CutoverCheckpoint.BeforeAuthorityReplacement);
            authorityMutationStarted = true;
            WriteFileAtomically(authority.AuthorityStatePath, synchronizedAuthorityBytes);
            authorityStateAfterSha256 = ComputeSha256(authority.AuthorityStatePath);
            host.Checkpoint(CutoverCheckpoint.AfterAuthorityReplacement);
            host.Checkpoint(CutoverCheckpoint.BeforeAuthorityReadBack);
            var persistedAuthorityBytes = File.ReadAllBytes(authority.AuthorityStatePath);
            var persistedAuthority = DeserializeCanonicalAuthority(persistedAuthorityBytes, authority.AuthorityStatePath);
            if (!string.Equals(ComputeSha256(persistedAuthorityBytes), expectedAuthorityStateSha256, StringComparison.OrdinalIgnoreCase)
                || persistedAuthority != synchronizedAuthority)
                throw new InvalidDataException("The synchronized authority-state.json failed exact hash/model read-back validation.");
            if (persistedAuthority.Protocol!.BusinessRevision != targetDatabaseRevision
                || persistedAuthority.Protocol.Revision != targetAuthorityProtocolRevision
                || persistedAuthority.Protocol.Phase != authority.Protocol.Phase
                || persistedAuthority.Protocol.DeviceId != authority.Protocol.DeviceId
                || persistedAuthority.Protocol.LineageId != authority.Protocol.LineageId
                || !string.Equals(persistedAuthority.Protocol.DisplayName, authority.Protocol.DisplayName, StringComparison.Ordinal)
                || persistedAuthority.Protocol.Generation != authority.Protocol.Generation
                || persistedAuthority.Protocol.HandoffVersion != authority.Protocol.HandoffVersion
                || persistedAuthority.Protocol.LastRecovery != authority.Protocol.LastRecovery
                || persistedAuthority.State != WriteAuthorityState.Authoritative
                || persistedAuthority.UpdatedAtUtc != updatedAtUtc)
                throw new InvalidDataException("Authority read-back changed an identity/protocol invariant or did not reach the exact target revisions.");
            authorityStateAfterSha256 = ComputeSha256(authority.AuthorityStatePath);
            authorityProtocolRevisionAfter = persistedAuthority.Protocol.Revision;
            authorityBusinessRevisionAfter = persistedAuthority.Protocol.BusinessRevision;
            host.Checkpoint(CutoverCheckpoint.AfterAuthorityReadBack);

            VerifyPostCutoverDatabase(database, targetDatabaseRevision);
            if (EnumerateArchiveFiles().Count != 0)
                throw new InvalidDataException("Final cutover verification found files in the active Archive directory.");
            VerifySnapshotsUnchanged(configurationHashes, systemHashes, authority, authorityStateAfterSha256);
            EnsureDesktopClosed();

            manifest = manifest with
            {
                State = "AuthoritySynchronized",
                BusinessDataRevisionAfter = targetDatabaseRevision,
                AuthorityProtocolRevisionAfter = authorityProtocolRevisionAfter,
                AuthorityBusinessRevisionAfter = authorityBusinessRevisionAfter,
                AuthorityStateAfterSha256 = authorityStateAfterSha256,
                Note = "Database, Archive, authority revision synchronization, protocol identity invariants, non-authority Config hashes, and OneDrive System identity were verified."
            };
            WriteManifest(manifestPath, manifest);
            manifest = manifest with
            {
                State = "Complete",
                BusinessDataRevisionAfter = targetDatabaseRevision,
                AuthorityProtocolRevisionAfter = authorityProtocolRevisionAfter,
                AuthorityBusinessRevisionAfter = authorityBusinessRevisionAfter,
                AuthorityStateAfterSha256 = authorityStateAfterSha256,
                Note = "Cutover completed. Reset data was verified empty; business settings, schema history, synchronized authority revisions, authority identity, other configuration, and System membership were verified."
            };
            WriteManifest(manifestPath, manifest);
            return new(true, report, backupDirectory, databaseBackupHash,
                database.BusinessDataRevision, targetDatabaseRevision,
                "Cutover completed and verified. Test-era Recovery and remote disaster-recovery/handoff artifacts were preserved and may remain temporarily.",
                authority.Protocol.Revision, authorityProtocolRevisionAfter.Value,
                authority.Protocol.BusinessRevision, authorityBusinessRevisionAfter.Value,
                ComputeSha256(backupAuthorityPath));
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
            if (authorityMutationStarted)
            {
                try { RestoreAuthorityStateFromBackup(authority.AuthorityStatePath, backupAuthorityPath, authority.StateSha256); }
                catch (Exception exception) { rollbackFailures.Add("authority-state.json restoration failed: " + exception.Message); }
            }

            if (rollbackFailures.Count == 0 && File.Exists(manifestPath) && File.Exists(backupDatabasePath))
            {
                try { VerifyPreCutoverStateRestored(database, authority, configurationHashes, systemHashes, archiveFiles); }
                catch (Exception exception) { rollbackFailures.Add("pre-cutover state verification failed: " + exception.Message); }
            }

            if (rollbackFailures.Count == 0 && File.Exists(manifestPath))
            {
                try
                {
                    manifest = manifest with
                    {
                        State = "RolledBack",
                        BusinessDataRevisionAfter = database.BusinessDataRevision,
                        AuthorityProtocolRevisionAfter = authority.Protocol.Revision,
                        AuthorityBusinessRevisionAfter = authority.Protocol.BusinessRevision,
                        AuthorityStateAfterSha256 = authority.StateSha256,
                        Note = "Operation failed and exact pre-cutover database, Archive, and authority-state state was restored and verified. " + operationError.Message
                    };
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
        ValidatedAuthority authority,
        string? expectedAuthorityStateSha256 = null)
    {
        var expectedConfigurationHashes = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in originalConfigurationHashes) expectedConfigurationHashes.Add(pair.Key, pair.Value);
        if (expectedAuthorityStateSha256 is not null)
            expectedConfigurationHashes[authority.AuthorityStateRelativePath] = expectedAuthorityStateSha256;
        if (!DictionaryEqual(expectedConfigurationHashes, HashDirectoryFiles(configDirectory)))
            throw new InvalidDataException("A Config file hash changed unexpectedly during the cutover; only the authority protocol revision may be updated by this utility.");
        if (!DictionaryEqual(originalSystemHashes, HashDirectoryFiles(authority.SystemDirectoryPath)))
            throw new InvalidDataException("The OneDrive System file set or a file hash changed during the cutover.");
    }

    private void VerifyPostCutoverDatabase(DatabaseSnapshot before, long targetRevision)
    {
        if (!File.Exists(liveDatabasePath) || new FileInfo(liveDatabasePath).Length <= 0)
            throw new InvalidDataException("Post-cutover verification requires the existing non-empty live.db.");
        using var verification = OpenDatabase(liveDatabasePath, SqliteOpenMode.ReadOnly);
        if (!string.Equals(ReadIntegrity(verification, null), "ok", StringComparison.OrdinalIgnoreCase)
            || CountForeignKeyViolations(verification, null) != 0)
            throw new InvalidDataException("Post-cutover SQLite integrity or foreign-key verification failed.");
        if (ResetTablesInDeleteOrder.Any(table => ExecuteScalarLong(verification, null, $"SELECT COUNT(*) FROM \"{table}\";") != 0))
            throw new InvalidDataException("Post-cutover verification found rows in an approved reset table.");
        if (!RowsEqual(before.BusinessSettings, ReadBusinessSettings(verification, null))
            || !MigrationRowsEqual(before.Migrations, ReadMigrationRows(verification, null))
            || ReadBusinessDataRevision(verification, null) != targetRevision)
            throw new InvalidDataException("Post-cutover settings, schema history, or target business revision verification failed.");
    }

    private void VerifyPreCutoverStateRestored(
        DatabaseSnapshot database,
        ValidatedAuthority authority,
        IReadOnlyDictionary<string, string> configurationHashes,
        IReadOnlyDictionary<string, string> systemHashes,
        IReadOnlyList<ArchiveFileHash> archiveFiles)
    {
        EnsureDesktopClosed();
        if (!string.Equals(ComputeSha256(liveDatabasePath), database.FileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Rollback did not restore the exact pre-cutover live.db bytes.");
        var restoredDatabase = ReadAndValidateDatabase();
        if (restoredDatabase.SchemaVersion != database.SchemaVersion
            || !string.Equals(restoredDatabase.Integrity, database.Integrity, StringComparison.OrdinalIgnoreCase)
            || restoredDatabase.ForeignKeyViolationCount != database.ForeignKeyViolationCount
            || restoredDatabase.BusinessDataRevision != database.BusinessDataRevision
            || !TableCountSnapshotsEqual(database.TableCounts, restoredDatabase.TableCounts)
            || !RowsEqual(database.BusinessSettings, restoredDatabase.BusinessSettings)
            || !MigrationRowsEqual(database.Migrations, restoredDatabase.Migrations)
            || !database.OrdersByStatusAndSource.SequenceEqual(restoredDatabase.OrdersByStatusAndSource))
            throw new InvalidDataException("Rollback did not restore the exact pre-cutover SQLite logical state.");

        VerifyFileSet(archiveFiles, archiveDirectory);
        if (!FileSetEquals(archiveFiles, EnumerateArchiveFiles()))
            throw new InvalidDataException("Rollback did not restore the exact pre-cutover Archive file set.");
        if (!string.Equals(ComputeSha256(authority.AuthorityStatePath), authority.StateSha256, StringComparison.OrdinalIgnoreCase)
            || DeserializeCanonicalAuthority(File.ReadAllBytes(authority.AuthorityStatePath), authority.AuthorityStatePath) != authority.Document)
            throw new InvalidDataException("Rollback did not restore the exact pre-cutover authority-state.json.");
        VerifySnapshotsUnchanged(configurationHashes, systemHashes, authority);
    }

    private static bool TableCountsEqual(
        IReadOnlyDictionary<string, long> expected,
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        return expected.Count == ResetTablesInDeleteOrder.Length
            && ResetTablesInDeleteOrder.All(table => expected.TryGetValue(table, out var count)
                && ExecuteScalarLong(connection, transaction, $"SELECT COUNT(*) FROM \"{table}\";") == count);
    }

    private static bool TableCountSnapshotsEqual(
        IReadOnlyDictionary<string, long> left,
        IReadOnlyDictionary<string, long> right)
    {
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && pair.Value == value);
    }

    private static void CreateSqliteBackup(string sourcePath, string destinationPath)
    {
        CopyFileDurably(sourcePath, destinationPath, overwrite: false);
        ValidateDatabaseFile(destinationPath);
    }

    private void RestoreDatabaseFromBackup(string backupPath, string expectedSha256)
    {
        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length <= 0)
            throw new InvalidDataException("The verified live.db rollback backup is missing or empty.");
        if (!string.Equals(ComputeSha256(backupPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The verified live.db rollback backup changed after preflight; it was not restored.");
        ValidateDatabaseFile(backupPath);
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
        {
            if (File.Exists(liveDatabasePath + suffix))
                throw new IOException($"SQLite sidecar '{Path.GetFileName(liveDatabasePath + suffix)}' remains after the failed cutover; refusing to replace the database file.");
        }
        var backupBytes = File.ReadAllBytes(backupPath);
        WriteFileAtomically(liveDatabasePath, backupBytes);
        if (!string.Equals(ComputeSha256(liveDatabasePath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exact live.db rollback replacement failed its SHA-256 read-back check.");
    }

    private static void RestoreAuthorityStateFromBackup(string destinationPath, string backupPath, string expectedSha256)
    {
        if (!File.Exists(backupPath) || new FileInfo(backupPath).Length <= 0
            || !string.Equals(ComputeSha256(backupPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The verified local authority-state.json rollback backup is missing, empty, or changed.");
        WriteFileAtomically(destinationPath, File.ReadAllBytes(backupPath));
        if (!string.Equals(ComputeSha256(destinationPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Exact authority-state.json rollback replacement failed its SHA-256 read-back check.");
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
            else CopyFileDurably(sourcePath, destinationPath, overwrite: false);
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
            CopyFileDurably(sourcePath, destinationPath, overwrite: false);
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

    private static string ComputeSha256(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static AuthorityStateDocument DeserializeCanonicalAuthority(byte[] bytes, string path)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("state", out _)
                || ReadInt(root, "schemaVersion") != CanonicalAuthoritySchemaVersion)
                throw new InvalidDataException("Canonical authority-state.json must use schema 2 without a persisted coarse state field.");
            var protocol = root.GetProperty("protocol").Deserialize<AuthorityProtocolState>(JsonOptions)
                ?? throw new InvalidDataException("Canonical authority-state.json has no protocol state.");
            protocol.Validate();
            var updatedAtUtc = root.GetProperty("updatedAtUtc").GetDateTimeOffset();
            if (updatedAtUtc == default)
                throw new InvalidDataException("Canonical authority-state.json has no update timestamp.");
            return new AuthorityStateDocument(CanonicalAuthoritySchemaVersion, protocol.WriteState, updatedAtUtc)
            {
                Protocol = protocol
            };
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException($"Canonical authority-state.json '{path}' is malformed.", exception);
        }
    }

    private static byte[] SerializeCanonicalAuthority(AuthorityStateDocument document)
    {
        if (document.SchemaVersion != CanonicalAuthoritySchemaVersion || document.Protocol is null
            || document.State != document.Protocol.WriteState || document.UpdatedAtUtc == default)
            throw new InvalidDataException("Only a validated canonical schema-2 authority document can be persisted.");
        document.Protocol.Validate();
        return JsonSerializer.SerializeToUtf8Bytes(
            new { document.SchemaVersion, document.UpdatedAtUtc, document.Protocol }, JsonOptions);
    }

    private static void WriteFileAtomically(string destinationPath, byte[] bytes)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestinationPath)
            ?? throw new InvalidDataException("An atomic replacement path must have a parent directory.");
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("An atomic replacement parent directory does not exist.");
        EnsureNotReparsePoint(fullDestinationPath, "atomic replacement destination");
        var temporaryPath = Path.Combine(directory, "." + Path.GetFileName(fullDestinationPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, fullDestinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void CopyFileDurably(string sourcePath, string destinationPath, bool overwrite)
    {
        var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new FileStream(destinationPath, mode, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough);
        source.CopyTo(destination);
        destination.Flush(flushToDisk: true);
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

    private sealed record ValidatedAuthority(
        AuthorityStateDocument Document,
        string AuthorityStatePath,
        string AuthorityStateRelativePath,
        string StateSha256,
        string SystemDirectoryPath,
        string LineagePath,
        string DevicePath)
    {
        public int SchemaVersion => Document.SchemaVersion;
        public string PhaseName => Protocol.Phase.ToString();
        public AuthorityProtocolState Protocol => Document.Protocol!;
    }

    private sealed record DatabaseSnapshot(
        int SchemaVersion,
        string Integrity,
        int ForeignKeyViolationCount,
        IReadOnlyList<OrderStatusSourceCount> OrdersByStatusAndSource,
        IReadOnlyDictionary<string, long> TableCounts,
        IReadOnlyList<List<SqliteCell>> BusinessSettings,
        IReadOnlyList<MigrationRow> Migrations,
        long BusinessDataRevision,
        string FileSha256);

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
        public string? AuthorityStateBackupFileName { get; init; }
        public string? AuthorityStateBackupSha256 { get; init; }
        public string? AuthorityStateBeforeSha256 { get; init; }
        public string? AuthorityStateAfterSha256 { get; init; }
        public long? AuthorityProtocolRevisionBefore { get; init; }
        public long? AuthorityProtocolRevisionAfter { get; init; }
        public long? AuthorityBusinessRevisionBefore { get; init; }
        public long? AuthorityBusinessRevisionAfter { get; init; }
        public string? RollbackProvenance { get; init; }
    }
}
