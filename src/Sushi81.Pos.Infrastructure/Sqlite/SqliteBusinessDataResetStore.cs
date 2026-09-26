using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Maintenance;

namespace Sushi81.Pos.Infrastructure.Sqlite;

/// <summary>Creates a retained SQLite/Archive recovery unit before the explicitly authorized reset.</summary>
public sealed class SqliteBusinessDataResetStore(
    IAppPaths paths,
    SqliteConnectionFactory connectionFactory,
    SqliteTransactionRunner transactionRunner,
    TimeProvider? timeProvider = null,
    Func<string, Exception?>? failureInjector = null) : IBusinessDataResetStore
{
    public const int RequiredSchemaVersion = 11;

    private static readonly string[] ResetTables =
    [
        "export_emissions",
        "export_batch_orders",
        "export_batches",
        "annual_archive_order_proofs",
        "annual_archive_completions",
        "payment_adjustments",
        "order_item_adjustments",
        "order_items",
        "order_tax_breakdown",
        "orders",
        "order_reference_sequences",
        "options",
        "option_groups",
        "products",
        "categories"
    ];

    private static readonly System.Text.RegularExpressions.Regex CanonicalArchiveName = new(
        "^sushi81-archive-(?<year>[0-9]{4})\\.db$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
    private static readonly JsonSerializerOptions BackupManifestJsonOptions = new() { WriteIndented = true };

    private readonly IAppPaths paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly SqliteConnectionFactory connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    private readonly SqliteTransactionRunner transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    private readonly TimeProvider timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Func<string, Exception?>? failureInjector = failureInjector;

    public async Task<BusinessDataResetPreview> PreviewAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        var schemaVersion = await ScalarLongAsync(connection, null, "SELECT COALESCE(MAX(version),0) FROM schema_migrations;", cancellationToken);
        if (schemaVersion != RequiredSchemaVersion)
            throw new InvalidDataException("The live database schema is not the supported M13 schema.");

        var archiveFiles = await EnumerateFilesSafelyAsync(paths.ArchiveDirectory, cancellationToken);
        var archiveYears = archiveFiles
            .Select(file => CanonicalArchiveName.Match(Path.GetFileName(file.RelativePath)))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture))
            .Distinct()
            .Count();

        return new BusinessDataResetPreview(
            await CountAsync(connection, "orders", cancellationToken),
            await CountAsync(connection, "products", cancellationToken),
            await CountAsync(connection, "categories", cancellationToken),
            await CountAsync(connection, "option_groups", cancellationToken),
            await CountAsync(connection, "options", cancellationToken),
            await CountAsync(connection, "export_batches", cancellationToken),
            await ScalarLongAsync(connection, null, "SELECT COUNT(*) FROM export_batches WHERE status='PREPARED';", cancellationToken),
            checked(
                await ScalarLongAsync(connection, null, "SELECT COUNT(*) FROM annual_archive_completions;", cancellationToken)
                + await ScalarLongAsync(connection, null, "SELECT COUNT(*) FROM annual_archive_order_proofs;", cancellationToken)),
            archiveYears,
            archiveFiles.Count,
            await CountAsync(connection, "order_reference_sequences", cancellationToken));
    }

    public async Task<BusinessDataResetStoreResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        paths.EnsureInitialized();
        var preview = await PreviewAsync(cancellationToken);
        if (!preview.HasBusinessData)
            return new BusinessDataResetStoreResult(BusinessDataResetStatus.AlreadyEmpty);

        var backupId = $"{timeProvider.GetUtcNow():yyyyMMdd'T'HHmmssfff'Z'}-{Guid.NewGuid():N}";
        var backupRoot = Path.GetFullPath(Path.Combine(paths.RootDirectory, "MaintenanceBackups", backupId));
        var backupDatabasePath = Path.Combine(backupRoot, "live.db");
        var archiveBackupPath = Path.Combine(backupRoot, "Archive");
        var archiveManifestPath = Path.Combine(backupRoot, "manifest.json");
        MaintenanceBackupManifest manifest;

        try
        {
            Directory.CreateDirectory(backupRoot);
            Directory.CreateDirectory(archiveBackupPath);
            Probe("backup.before-database");
            await CreateStandaloneDatabaseBackupAsync(backupDatabasePath, cancellationToken);
            var backupValidation = await ValidateDatabaseAsync(backupDatabasePath, expectedRevision: null, cancellationToken);
            var archiveFiles = await CopyArchiveToBackupAsync(archiveBackupPath, cancellationToken);
            manifest = new MaintenanceBackupManifest(
                1,
                timeProvider.GetUtcNow(),
                backupValidation.SchemaVersion,
                backupValidation.BusinessRevision,
                await HashFileAsync(backupDatabasePath, cancellationToken),
                archiveFiles);
            await File.WriteAllTextAsync(archiveManifestPath, JsonSerializer.Serialize(manifest, BackupManifestJsonOptions), cancellationToken);
            Probe("backup.complete");
        }
        catch
        {
            // Staging has not touched live.db or Archive. Leave any partial private staging for diagnosis.
            return new BusinessDataResetStoreResult(BusinessDataResetStatus.FailedWithoutMutation, backupId);
        }

        var transactionCommitted = false;
        try
        {
            Probe("transaction.before");
            var changed = await transactionRunner.ExecuteAsync(async (applicationTransaction, token) =>
            {
                var sqliteTransaction = (SqliteApplicationTransaction)applicationTransaction;
                var rowsBefore = await ReadResettableRowsAsync(sqliteTransaction.Connection, sqliteTransaction.Transaction, token);
                var filesToRemove = manifest.ArchiveFiles.Count;
                if (rowsBefore == 0 && filesToRemove == 0)
                    return false;

                if (rowsBefore == 0)
                {
                    // A file-only reset still changes the durable business package. Touching a preserved
                    // settings value gives the transaction runner its normal one-step revision signal.
                    await ExecuteAsync(sqliteTransaction.Connection, sqliteTransaction.Transaction,
                        "UPDATE business_settings SET pickup_discount_rate=pickup_discount_rate WHERE singleton_id=1;", token);
                }

                foreach (var table in ResetTables)
                {
                    await ExecuteAsync(sqliteTransaction.Connection, sqliteTransaction.Transaction, $"DELETE FROM {table};", token);
                }

                Probe("transaction.before-commit");
                return true;
            }, cancellationToken);

            if (!changed)
                return new BusinessDataResetStoreResult(BusinessDataResetStatus.AlreadyEmpty, backupId);
            transactionCommitted = true;
            Probe("archive.remove.before");
            await ClearDirectoryContentsAsync(paths.ArchiveDirectory, cancellationToken);
            Probe("archive.remove.after");
            Probe("validation.before");
            await ValidateResetDatabaseAsync(backupDatabasePath, manifest.BusinessRevision, cancellationToken);
            if ((await EnumerateFilesSafelyAsync(paths.ArchiveDirectory, cancellationToken)).Count != 0)
                throw new IOException("The active annual archive directory is not empty after reset.");

            return new BusinessDataResetStoreResult(BusinessDataResetStatus.Reset, backupId);
        }
        catch
        {
            if (!transactionCommitted)
                return new BusinessDataResetStoreResult(BusinessDataResetStatus.FailedWithoutMutation, backupId);

            try
            {
                await RestoreDatabaseAsync(backupDatabasePath, manifest.DatabaseSha256, CancellationToken.None);
                await RestoreArchiveAsync(archiveBackupPath, manifest.ArchiveFiles, CancellationToken.None);
                return new BusinessDataResetStoreResult(BusinessDataResetStatus.FailedAndRestored, backupId);
            }
            catch
            {
                // The private pre-reset unit remains available for controller-guided recovery.
                return new BusinessDataResetStoreResult(BusinessDataResetStatus.RecoveryRequired, backupId);
            }
        }
    }

    private async Task CreateStandaloneDatabaseBackupAsync(string destinationPath, CancellationToken cancellationToken)
    {
        await using var source = await connectionFactory.OpenLiveConnectionAsync(cancellationToken);
        var destinationBuilder = new SqliteConnectionStringBuilder
        {
            DataSource = destinationPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 5
        };
        await using var destination = new SqliteConnection(destinationBuilder.ToString());
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
        await destination.CloseAsync();
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length == 0)
            throw new InvalidDataException("SQLite did not create the standalone maintenance backup.");
    }

    private static async Task<DatabaseValidation> ValidateDatabaseAsync(
        string databasePath,
        long? expectedRevision,
        CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(databasePath, cancellationToken);
        var integrity = await SqliteConnectionFactory.ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The maintenance database integrity check failed.");
        if (await SqliteConnectionFactory.ExecuteScalarIntAsync(connection, "SELECT COUNT(*) FROM pragma_foreign_key_check;", cancellationToken) != 0)
            throw new InvalidDataException("The maintenance database foreign-key check failed.");

        var schema = await ScalarLongAsync(connection, null, "SELECT COALESCE(MAX(version),0) FROM schema_migrations;", cancellationToken);
        if (schema != RequiredSchemaVersion)
            throw new InvalidDataException("The maintenance database has an unsupported schema version.");
        var revisionText = await SqliteConnectionFactory.ExecuteScalarStringAsync(connection,
            "SELECT value FROM foundation_metadata WHERE key='business_data_revision';", cancellationToken);
        if (!long.TryParse(revisionText, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 0)
            throw new InvalidDataException("The maintenance database business revision is malformed.");
        if (expectedRevision is { } expected && revision != expected)
            throw new InvalidDataException("The maintenance database business revision does not match the expected value.");
        if (await ScalarLongAsync(connection, null, "SELECT COUNT(*) FROM business_settings WHERE singleton_id=1;", cancellationToken) != 1)
            throw new InvalidDataException("The preserved business-settings row is missing.");
        return new DatabaseValidation((int)schema, revision);
    }

    private async Task ValidateResetDatabaseAsync(string backupDatabasePath, long originalRevision, CancellationToken cancellationToken)
    {
        var backupState = await ValidateDatabaseAsync(backupDatabasePath, originalRevision, cancellationToken);
        var liveState = await ValidateDatabaseAsync(connectionFactory.LiveDatabasePath, checked(originalRevision + 1), cancellationToken);
        await using var live = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        foreach (var table in ResetTables)
        {
            if (await CountAsync(live, table, cancellationToken) != 0)
                throw new InvalidDataException($"The reset left rows in {table}.");
        }

        var backupPreserved = await ReadPreservedStateHashAsync(backupDatabasePath, cancellationToken);
        var livePreserved = await ReadPreservedStateHashAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(backupPreserved, livePreserved))
            throw new InvalidDataException("A preserved settings or foundation record changed during reset.");
        if (backupState.SchemaVersion != liveState.SchemaVersion)
            throw new InvalidDataException("The live schema changed during reset.");
    }

    private async Task RestoreDatabaseAsync(string backupDatabasePath, string expectedSha256, CancellationToken cancellationToken)
    {
        var actualSha256 = await HashFileAsync(backupDatabasePath, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualSha256), Convert.FromHexString(expectedSha256)))
            throw new InvalidDataException("The retained pre-reset SQLite backup no longer matches its recorded hash.");

        await using var source = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(backupDatabasePath, cancellationToken);
        await using var destination = await connectionFactory.OpenLiveConnectionAsync(cancellationToken);
        source.BackupDatabase(destination);
        await destination.CloseAsync();
        var validation = await ValidateDatabaseAsync(connectionFactory.LiveDatabasePath, null, cancellationToken);
        var backup = await ValidateDatabaseAsync(backupDatabasePath, null, cancellationToken);
        var backupPreserved = await ReadPreservedStateHashAsync(backupDatabasePath, cancellationToken);
        var restoredPreserved = await ReadPreservedStateHashAsync(connectionFactory.LiveDatabasePath, cancellationToken);
        if (validation.BusinessRevision != backup.BusinessRevision
            || !CryptographicOperations.FixedTimeEquals(backupPreserved, restoredPreserved))
            throw new InvalidDataException("The pre-reset SQLite database could not be restored exactly.");
    }

    private async Task RestoreArchiveAsync(string backupPath, IReadOnlyList<ArchiveFileEntry> expectedFiles, CancellationToken cancellationToken)
    {
        await ClearDirectoryContentsAsync(paths.ArchiveDirectory, cancellationToken);
        foreach (var entry in expectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = ResolveContainedPath(backupPath, entry.RelativePath);
            var destination = ResolveContainedPath(paths.ArchiveDirectory, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: false);
        }

        var restored = await EnumerateFilesSafelyAsync(paths.ArchiveDirectory, cancellationToken);
        if (restored.Count != expectedFiles.Count)
            throw new InvalidDataException("The pre-reset Archive files could not be restored completely.");
        foreach (var entry in expectedFiles)
        {
            var file = restored.SingleOrDefault(candidate => string.Equals(candidate.RelativePath, entry.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (file is null || file.Length != entry.Length
                || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(file.Sha256), Convert.FromHexString(entry.Sha256)))
                throw new InvalidDataException("A pre-reset Archive file did not restore with its original hash.");
        }
    }

    private async Task<IReadOnlyList<ArchiveFileEntry>> CopyArchiveToBackupAsync(string backupPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(backupPath);
        var sourceFiles = await EnumerateFilesSafelyAsync(paths.ArchiveDirectory, cancellationToken);
        var result = new List<ArchiveFileEntry>(sourceFiles.Count);
        foreach (var source in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = ResolveContainedPath(paths.ArchiveDirectory, source.RelativePath);
            var destination = ResolveContainedPath(backupPath, source.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(input, destination, overwrite: false);
            var hash = await HashFileAsync(destination, cancellationToken);
            if (source.Length != new FileInfo(destination).Length
                || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(source.Sha256), Convert.FromHexString(hash)))
                throw new IOException("An Archive file changed while its maintenance backup was staged.");
            result.Add(source with { Sha256 = hash });
        }
        return result;
    }

    private static async Task<IReadOnlyList<ArchiveFileEntry>> EnumerateFilesSafelyAsync(string root, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return [];
        EnsureNotReparsePoint(root);
        var result = new List<ArchiveFileEntry>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(root));
        while (pending.TryPop(out var directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Archive reparse points are not supported by the reset operation.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                var relative = Path.GetRelativePath(root, entry);
                result.Add(new ArchiveFileEntry(relative, new FileInfo(entry).Length, await HashFileAsync(entry, cancellationToken)));
            }
        }
        return result.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task ClearDirectoryContentsAsync(string root, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        EnsureNotReparsePoint(root);
        foreach (var entry in Directory.EnumerateFileSystemEntries(root).ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Archive reparse points are not supported by the reset operation.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                await ClearDirectoryContentsAsync(entry, cancellationToken);
                Directory.Delete(entry, recursive: false);
            }
            else
                File.Delete(entry);
        }
    }

    private static void EnsureNotReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Archive reparse points are not supported by the reset operation.");
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("An Archive backup path escaped its private root.");
        return fullPath;
    }

    private static async Task<long> ReadResettableRowsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        long result = 0;
        foreach (var table in ResetTables)
            result = checked(result + await CountAsync(connection, table, transaction, cancellationToken));
        return result;
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string table, CancellationToken cancellationToken) =>
        await CountAsync(connection, table, null, cancellationToken);

    private static async Task<long> CountAsync(SqliteConnection connection, string table, SqliteTransaction? transaction, CancellationToken cancellationToken) =>
        await ScalarLongAsync(connection, transaction, $"SELECT COUNT(*) FROM {table};", cancellationToken);

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadPreservedStateHashAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(databasePath, cancellationToken);
        var builder = new StringBuilder();
        await AppendRowsAsync(connection, builder, "business_settings", "SELECT * FROM business_settings ORDER BY singleton_id;", cancellationToken);
        await AppendRowsAsync(connection, builder, "schema_migrations", "SELECT * FROM schema_migrations ORDER BY version;", cancellationToken);
        await AppendRowsAsync(connection, builder, "foundation_metadata", "SELECT key,value FROM foundation_metadata WHERE key <> 'business_data_revision' ORDER BY key;", cancellationToken);
        return SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static async Task AppendRowsAsync(SqliteConnection connection, StringBuilder builder, string table, string sql, CancellationToken cancellationToken)
    {
        builder.Append(table).Append('\n');
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            for (var index = 0; index < reader.FieldCount; index++)
            {
                builder.Append(reader.GetName(index)).Append('=');
                if (reader.IsDBNull(index)) builder.Append("<null>");
                else if (reader.GetValue(index) is byte[] bytes) builder.Append(Convert.ToHexString(bytes));
                else builder.Append(Convert.ToString(reader.GetValue(index), CultureInfo.InvariantCulture));
                builder.Append('\u001f');
            }
            builder.Append('\n');
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void Probe(string stage)
    {
        if (failureInjector?.Invoke(stage) is { } exception) throw exception;
    }

    private sealed record DatabaseValidation(int SchemaVersion, long BusinessRevision);

    private sealed record ArchiveFileEntry(string RelativePath, long Length, string Sha256);

    private sealed record MaintenanceBackupManifest(
        int FormatVersion,
        DateTimeOffset CreatedAtUtc,
        int SchemaVersion,
        long BusinessRevision,
        string DatabaseSha256,
        IReadOnlyList<ArchiveFileEntry> ArchiveFiles);
}
