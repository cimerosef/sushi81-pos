using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Infrastructure.Sqlite;

namespace Sushi81.Pos.Infrastructure.Recovery;

/// <summary>Creates independently validated SQLite backup units and retains five valid recent units.</summary>
public class SqliteLocalRecoverySnapshotService(
    IAppPaths paths,
    SqliteConnectionFactory connectionFactory,
    IBusinessClock clock) : ILocalRecoverySnapshotService
{
    private const int RetentionCount = 5;
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public async Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.Sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(change), "Durable-change sequence cannot be negative.");
        }

        paths.EnsureInitialized();
        var createdAtUtc = clock.UtcNow;
        var stem = BuildStem(createdAtUtc, change.Sequence);
        var stagingDirectory = Path.Combine(paths.TempDirectory, $"{stem}.staging");
        var stagingDatabasePath = Path.Combine(stagingDirectory, "snapshot.db");
        var stagingMetadataPath = Path.Combine(stagingDirectory, "metadata.json");
        var finalDirectory = Path.Combine(paths.RecoveryDirectory, stem);
        var finalDatabasePath = Path.Combine(finalDirectory, "snapshot.db");
        var finalMetadataPath = Path.Combine(finalDirectory, "metadata.json");

        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await BackupToStagingAsync(stagingDatabasePath, cancellationToken);
            await EnsureIntegrityAsync(stagingDatabasePath, cancellationToken);
            var checksum = await ComputeSha256Async(stagingDatabasePath, cancellationToken);
            var schemaVersion = await GetSchemaVersionAsync(stagingDatabasePath, cancellationToken);
            var metadata = new RecoverySnapshotMetadata(checksum, createdAtUtc, schemaVersion, change.Sequence);
            await WriteMetadataAsync(stagingMetadataPath, metadata, cancellationToken);
            await VerifyAsync(stagingDatabasePath, stagingMetadataPath, cancellationToken);

            Directory.Move(stagingDirectory, finalDirectory);
            await VerifyAsync(finalDatabasePath, finalMetadataPath, cancellationToken);
            await ApplyRetentionAsync(cancellationToken);
            return new RecoverySnapshotResult(finalDatabasePath, finalMetadataPath, checksum, createdAtUtc, change.Sequence, schemaVersion);
        }
        finally
        {
            DeleteDirectoryIfExists(stagingDirectory);
        }
    }

    public static async Task<RecoverySnapshotMetadata> VerifyAsync(
        string databasePath,
        string metadataPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath) || !File.Exists(metadataPath))
        {
            throw new RecoverySnapshotValidationException("A recovery snapshot is incomplete: its database or metadata file is missing.");
        }

        RecoverySnapshotMetadata metadata;
        try
        {
            await using var metadataStream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            metadata = await JsonSerializer.DeserializeAsync<RecoverySnapshotMetadata>(metadataStream, SerializerOptions, cancellationToken)
                ?? throw new RecoverySnapshotValidationException("Recovery snapshot metadata is empty.");
        }
        catch (JsonException exception)
        {
            throw new RecoverySnapshotValidationException($"Recovery snapshot metadata is malformed: {exception.Message}");
        }

        var computedChecksum = await ComputeSha256Async(databasePath, cancellationToken);
        if (!string.Equals(metadata.Sha256, computedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new RecoverySnapshotValidationException("Recovery snapshot checksum does not match its metadata.");
        }

        await EnsureIntegrityAsync(databasePath, cancellationToken);
        return metadata;
    }

    /// <summary>Returns the highest sequence in independently validated local recovery units.</summary>
    public static async Task<long> GetHighestValidatedSequenceAsync(IAppPaths paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        paths.EnsureInitialized();
        var highest = 0L;
        foreach (var recoveryDirectory in Directory.EnumerateDirectories(paths.RecoveryDirectory, "recovery-*"))
        {
            try
            {
                var metadata = await VerifyAsync(
                    Path.Combine(recoveryDirectory, "snapshot.db"),
                    Path.Combine(recoveryDirectory, "metadata.json"),
                    cancellationToken);
                highest = Math.Max(highest, metadata.DurableChangeSequence);
            }
            catch (RecoverySnapshotValidationException)
            {
                // Corrupt/incomplete units are not evidence of a committed recovery sequence.
            }
        }

        return highest;
    }

    protected virtual async Task BackupToStagingAsync(string stagingDatabasePath, CancellationToken cancellationToken)
    {
        await using var source = await connectionFactory.OpenLiveConnectionAsync(cancellationToken);
        await using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = stagingDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        await destination.OpenAsync(cancellationToken);
        source.BackupDatabase(destination);
    }

    private static async Task EnsureIntegrityAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(databasePath, cancellationToken);
        var integrity = await SqliteConnectionFactory.ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken);
        if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new RecoverySnapshotValidationException($"SQLite integrity_check failed for recovery snapshot: {integrity}");
        }
    }

    private static async Task<int> GetSchemaVersionAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = await SqliteConnectionFactory.OpenReadOnlyConnectionAsync(databasePath, cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
        {
            return 0;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static async Task WriteMetadataAsync(string metadataPath, RecoverySnapshotMetadata metadata, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(metadataPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, metadata, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task ApplyRetentionAsync(CancellationToken cancellationToken)
    {
        var validSnapshots = new List<(string DatabasePath, string MetadataPath, RecoverySnapshotMetadata Metadata)>();
        foreach (var recoveryDirectory in Directory.EnumerateDirectories(paths.RecoveryDirectory, "recovery-*"))
        {
            var databasePath = Path.Combine(recoveryDirectory, "snapshot.db");
            var metadataPath = Path.Combine(recoveryDirectory, "metadata.json");
            try
            {
                var metadata = await VerifyAsync(databasePath, metadataPath, cancellationToken);
                validSnapshots.Add((databasePath, metadataPath, metadata));
            }
            catch (RecoverySnapshotValidationException)
            {
                // Incomplete/corrupt units are never considered valid recovery points and are preserved for diagnostics.
            }
        }

        foreach (var obsolete in validSnapshots
                     .OrderByDescending(snapshot => snapshot.Metadata.CreatedAtUtc)
                     .ThenByDescending(snapshot => snapshot.Metadata.DurableChangeSequence)
                     .Skip(RetentionCount))
        {
            Directory.Delete(Path.GetDirectoryName(obsolete.DatabasePath)!, recursive: true);
        }
    }

    private static string BuildStem(DateTimeOffset createdAtUtc, long sequence) =>
        $"recovery-{createdAtUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfffffff'Z'", CultureInfo.InvariantCulture)}-{sequence:D20}-{Guid.NewGuid():N}";

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
