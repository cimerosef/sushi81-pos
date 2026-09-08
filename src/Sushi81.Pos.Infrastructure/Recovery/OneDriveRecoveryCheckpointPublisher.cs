using System.Security.Cryptography;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Infrastructure.Authority;

namespace Sushi81.Pos.Infrastructure.Recovery;

/// <summary>
/// Publishes recovery-only OneDrive checkpoint units. A checkpoint is never a grant,
/// membership approval or ordinary read-only hydration source.
/// </summary>
public sealed class OneDriveRecoveryCheckpointPublisher(
    string configuredOneDriveRoot,
    IAuthorityStateStore authorityStore,
    WriteAuthorityGuard guard,
    ILocalRecoverySnapshotService snapshots,
    IBusinessClock clock,
    IBusinessRevisionReader? revisionReader = null) : IRecoveryCheckpointPublisher, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private readonly string root = ValidateRoot(configuredOneDriveRoot);

    public async Task<RecoveryCheckpointPublicationResult> PublishAsync(long businessRevision, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(businessRevision);
        await publishGate.WaitAsync(cancellationToken);
        string? stagingDirectory = null;
        try
        {
            var document = await authorityStore.LoadAsync(cancellationToken)
                ?? throw new InvalidDataException("No canonical authority state is available for checkpoint publication.");
            var source = document.Protocol ?? throw new InvalidDataException("Checkpoint publication requires canonical authority metadata.");
            source.Validate();
            if (source.Phase != AuthorityPhase.Authoritative || guard.State != WriteAuthorityState.Authoritative)
                throw new WriteAuthorityException(guard.State);
            var canonicalBusinessRevision = revisionReader is null
                ? businessRevision
                : await revisionReader.ReadAsync(cancellationToken);
            if (canonicalBusinessRevision != businessRevision || source.BusinessRevision > businessRevision)
                throw new InvalidDataException("The checkpoint revision does not match the canonical business-data watermark.");
            if (source.LineageId is not { } lineageId)
                throw new InvalidDataException("An authoritative checkpoint source is missing lineage identity.");

            var local = await snapshots.CreateAsync(new DurableChange(businessRevision, clock.UtcNow), cancellationToken);
            var localSize = new FileInfo(local.DatabasePath).Length;
            var localHash = await ComputeSha256Async(local.DatabasePath, cancellationToken);
            if (localSize <= 0 || !string.Equals(localHash, local.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The local checkpoint snapshot changed after creation.");
            if (await SqliteBusinessRevisionStore.ReadFromDatabaseAsync(local.DatabasePath, cancellationToken) != businessRevision)
                throw new InvalidDataException("The checkpoint snapshot does not contain the requested business-data revision.");
            await ValidateSqliteAsync(local.DatabasePath, cancellationToken);

            var checkpointId = Guid.NewGuid();
            var metadata = new RecoveryCheckpointMetadata(
                1, "M07", checkpointId, lineageId, source.Generation, source.DeviceId,
                businessRevision, source.HandoffVersion, clock.UtcNow, "checkpoint.db", localSize, localHash);
            metadata.Validate();
            var generationRoot = Path.Combine(root, "DisasterRecovery", "Checkpoints", source.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            stagingDirectory = Path.Combine(generationRoot, $".{checkpointId:N}.staging");
            var finalDirectory = Path.Combine(generationRoot, checkpointId.ToString("N"));
            Directory.CreateDirectory(generationRoot);
            Directory.CreateDirectory(stagingDirectory);
            await CopyDurablyAsync(local.DatabasePath, Path.Combine(stagingDirectory, metadata.DatabaseFileName), cancellationToken);
            await WriteJsonDurablyAsync(Path.Combine(stagingDirectory, "metadata.json"), metadata, cancellationToken);

            var latest = await authorityStore.LoadAsync(cancellationToken);
            if (latest?.Protocol is null || latest.Protocol.Phase != AuthorityPhase.Authoritative
                || latest.Protocol.DeviceId != source.DeviceId || latest.Protocol.LineageId != source.LineageId
                || latest.Protocol.Generation != source.Generation || guard.State != WriteAuthorityState.Authoritative)
                throw new InvalidDataException("Authority changed while the recovery checkpoint was being prepared.");

            Directory.Move(stagingDirectory, finalDirectory);
            stagingDirectory = null;
            await ValidateUnitAsync(finalDirectory, metadata, cancellationToken);
            var retentionCompleted = await ApplyRetentionAsync(generationRoot, cancellationToken);
            return new RecoveryCheckpointPublicationResult(
                metadata,
                Path.Combine(finalDirectory, metadata.DatabaseFileName),
                Path.Combine(finalDirectory, "metadata.json"),
                retentionCompleted);
        }
        finally
        {
            if (stagingDirectory is not null && Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
            publishGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        publishGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string ValidateRoot(string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("The configured OneDrive root must be an absolute path.", nameof(value));
        return Path.GetFullPath(value);
    }

    private static async Task<bool> ApplyRetentionAsync(string generationRoot, CancellationToken cancellationToken)
    {
        try
        {
            var valid = new List<(string Directory, RecoveryCheckpointMetadata Metadata)>();
            foreach (var directory in Directory.EnumerateDirectories(generationRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var metadata = await ReadMetadataAsync(Path.Combine(directory, "metadata.json"), cancellationToken);
                    await ValidateUnitAsync(directory, metadata, cancellationToken);
                    valid.Add((directory, metadata));
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Incomplete/corrupt or temporarily unreadable units are not retention
                    // candidates and remain for diagnostics/retry.
                }
            }

            foreach (var obsolete in valid
                .OrderByDescending(item => item.Metadata.BusinessRevision)
                .ThenByDescending(item => item.Metadata.HandoffVersion)
                .ThenByDescending(item => item.Metadata.CheckpointId)
                .Skip(5))
            {
                Directory.Delete(obsolete.Directory, recursive: true);
            }
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return false;
        }
    }

    private static async Task<RecoveryCheckpointMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var metadata = await JsonSerializer.DeserializeAsync<RecoveryCheckpointMetadata>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("The checkpoint metadata is empty.");
            metadata.Validate();
            return metadata;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The checkpoint metadata is malformed.", exception);
        }
    }

    private static async Task ValidateUnitAsync(string directory, RecoveryCheckpointMetadata metadata, CancellationToken cancellationToken)
    {
        metadata.Validate();
        var databasePath = Path.Combine(directory, metadata.DatabaseFileName);
        if (!File.Exists(databasePath)) throw new InvalidDataException("The checkpoint database is missing.");
        var size = new FileInfo(databasePath).Length;
        var hash = await ComputeSha256Async(databasePath, cancellationToken);
        if (size != metadata.DatabaseSize || !string.Equals(hash, metadata.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The checkpoint size or digest is invalid.");
        await ValidateSqliteAsync(databasePath, cancellationToken);
    }

    private static async Task CopyDurablyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
        output.Flush(flushToDisk: true);
    }

    private static async Task WriteJsonDurablyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static async Task ValidateSqliteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The checkpoint SQLite integrity check failed.");
            await using var schema = connection.CreateCommand();
            schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
            if (Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture) != 1)
                throw new InvalidDataException("The checkpoint lacks the expected application schema marker.");
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("The checkpoint is not a valid SQLite application database.", exception);
        }
    }
}
