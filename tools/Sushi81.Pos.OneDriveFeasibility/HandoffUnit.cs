using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Sushi81.Pos.OneDriveFeasibility;

public static class HandoffUnitValidator
{
    public static async Task<HandoffValidationResult> ValidateAsync(
        string snapshotPath,
        string markerPath,
        long? expectedGeneration = null,
        long? expectedHandoffVersion = null,
        string? expectedLineageId = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(snapshotPath))
        {
            return HandoffValidationResult.Fail("missing-snapshot", "The snapshot is missing.");
        }

        if (!File.Exists(markerPath))
        {
            return HandoffValidationResult.Fail("missing-marker", "The ready marker is missing.");
        }

        var snapshotName = Path.GetFileName(snapshotPath);
        var markerName = Path.GetFileName(markerPath);
        const string snapshotSuffix = ".snapshot.db";
        const string markerSuffix = ".ready.json";
        if (!snapshotName.EndsWith(snapshotSuffix, StringComparison.Ordinal)
            || !markerName.EndsWith(markerSuffix, StringComparison.Ordinal)
            || !string.Equals(
                snapshotName[..^snapshotSuffix.Length],
                markerName[..^markerSuffix.Length],
                StringComparison.Ordinal))
        {
            return HandoffValidationResult.Fail("identity-mismatch", "Snapshot and ready marker filenames do not identify the same immutable handoff unit.");
        }

        var (metadata, metadataError) = await HandoffMetadataCodec.ReadStrictAsync(markerPath, cancellationToken);
        if (metadata is null)
        {
            var code = metadataError?.Contains("missing", StringComparison.OrdinalIgnoreCase) == true
                ? "missing-marker"
                : "malformed-metadata";
            return HandoffValidationResult.Fail(code, metadataError ?? "The ready marker is invalid.");
        }

        if (expectedGeneration is not null && metadata.Generation != expectedGeneration)
        {
            return HandoffValidationResult.Fail("generation-mismatch", "The ready marker generation does not match the expected generation.");
        }

        if (expectedHandoffVersion is not null && metadata.HandoffVersion != expectedHandoffVersion)
        {
            return HandoffValidationResult.Fail("handoff-version-mismatch", "The ready marker handoff version does not match the expected version.");
        }

        if (expectedLineageId is not null && !string.Equals(metadata.LineageId, expectedLineageId, StringComparison.Ordinal))
        {
            return HandoffValidationResult.Fail("lineage-mismatch", "The ready marker lineage does not match the expected lineage.");
        }

        var fileLength = new FileInfo(snapshotPath).Length;
        if (fileLength != metadata.SnapshotByteLength)
        {
            return HandoffValidationResult.Fail("size-mismatch", "The snapshot byte length does not match the ready marker.");
        }

        var checksum = await HandoffMetadataCodec.ComputeSha256Async(snapshotPath, cancellationToken);
        if (!string.Equals(checksum, metadata.SnapshotChecksum, StringComparison.OrdinalIgnoreCase))
        {
            return HandoffValidationResult.Fail("checksum-mismatch", "The snapshot checksum does not match the ready marker.");
        }

        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = snapshotPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var integrity = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return HandoffValidationResult.Fail("sqlite-integrity-failure", $"SQLite integrity_check returned '{integrity}'.");
            }
        }
        catch (SqliteException exception)
        {
            return HandoffValidationResult.Fail("sqlite-integrity-failure", $"SQLite validation failed: {exception.Message}");
        }

        return HandoffValidationResult.Pass(metadata);
    }
}

public sealed record SyntheticPublicationRequest(
    string HandoffDirectory,
    string SourceDeviceId,
    string LineageId,
    long Generation,
    long HandoffVersion,
    TimeSpan Timeout,
    TimeSpan PollInterval);

public sealed record SyntheticPublicationResult(
    bool Succeeded,
    string Code,
    string Message,
    string? SnapshotPath = null,
    string? MarkerPath = null,
    HandoffMetadata? Metadata = null,
    IReadOnlyList<CloudFileObservation>? SnapshotObservations = null,
    IReadOnlyList<CloudFileObservation>? MarkerObservations = null);

public static class SyntheticHandoffPublisher
{
    public static async Task<SyntheticPublicationResult> PublishAsync(
        SyntheticPublicationRequest request,
        ICloudFileStateReader stateReader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(stateReader);
        HandoffMetadata metadata;
        try
        {
            metadata = new HandoffMetadata(
                HandoffMetadataCodec.CurrentFormatVersion,
                request.LineageId,
                request.Generation,
                request.HandoffVersion,
                request.SourceDeviceId,
                HandoffMetadataCodec.Sha256Algorithm,
                new string('0', 64),
                1,
                DateTimeOffset.UtcNow,
                HandoffMetadataCodec.ReadyMarkerType,
                HandoffMetadataCodec.ReleasedMarkerState);
            HandoffMetadataCodec.ValidateMetadata(metadata);
        }
        catch (InvalidDataException exception)
        {
            return new(false, "invalid-request", exception.Message);
        }

        Directory.CreateDirectory(request.HandoffDirectory);
        var stem = $"handoff-{metadata.LineageId}-g{metadata.Generation:D10}-v{metadata.HandoffVersion:D20}";
        var snapshotPath = Path.Combine(request.HandoffDirectory, $"{stem}.snapshot.db");
        var markerPath = Path.Combine(request.HandoffDirectory, $"{stem}.ready.json");
        if (File.Exists(snapshotPath) || File.Exists(markerPath))
        {
            return new(false, "immutable-unit-exists", "An immutable handoff unit with this identity already exists.", snapshotPath, markerPath);
        }

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.OneDriveFeasibility", Guid.NewGuid().ToString("N"));
        var stagingPath = Path.Combine(stagingDirectory, "synthetic.snapshot.db");
        var snapshotObservations = new List<CloudFileObservation>();
        var markerObservations = new List<CloudFileObservation>();
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await CreateSyntheticDatabaseAsync(stagingPath, cancellationToken);
            var integrity = await ReadIntegrityAsync(stagingPath, cancellationToken);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return new(false, "sqlite-integrity-failure", $"Synthetic snapshot integrity failed: {integrity}");
            }

            var checksum = await HandoffMetadataCodec.ComputeSha256Async(stagingPath, cancellationToken);
            metadata = metadata with
            {
                SnapshotChecksum = checksum,
                SnapshotByteLength = new FileInfo(stagingPath).Length
            };
            HandoffMetadataCodec.ValidateMetadata(metadata);

            File.Move(stagingPath, snapshotPath);
            File.SetAttributes(snapshotPath, File.GetAttributes(snapshotPath) | FileAttributes.ReadOnly);

            var snapshotWait = await WaitForInSyncAsync(snapshotPath, stateReader, request, snapshotObservations, cancellationToken);
            if (!snapshotWait)
            {
                return new(false, "snapshot-not-synchronized", "Snapshot did not reach a confirmed Cloud Files IN_SYNC state before timeout.", snapshotPath, markerPath, metadata, snapshotObservations, markerObservations);
            }

            // This is the ordering barrier: the marker is created only after the snapshot wait succeeds.
            await HandoffMetadataCodec.WriteAsync(markerPath, metadata, cancellationToken);
            File.SetAttributes(markerPath, File.GetAttributes(markerPath) | FileAttributes.ReadOnly);

            var markerWait = await WaitForInSyncAsync(markerPath, stateReader, request, markerObservations, cancellationToken);
            if (!markerWait)
            {
                return new(false, "marker-not-synchronized", "Ready marker did not reach a confirmed Cloud Files IN_SYNC state before timeout.", snapshotPath, markerPath, metadata, snapshotObservations, markerObservations);
            }

            var validation = await HandoffUnitValidator.ValidateAsync(snapshotPath, markerPath, request.Generation, request.HandoffVersion, request.LineageId, cancellationToken);
            return validation.IsValid
                ? new(true, "released", validation.Message, snapshotPath, markerPath, metadata, snapshotObservations, markerObservations)
                : new(false, validation.Code, validation.Message, snapshotPath, markerPath, metadata, snapshotObservations, markerObservations);
        }
        catch (IOException exception)
        {
            return new(false, "io-error", exception.Message, snapshotPath, markerPath, metadata, snapshotObservations, markerObservations);
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
        }
    }

    private static async Task<bool> WaitForInSyncAsync(
        string path,
        ICloudFileStateReader stateReader,
        SyntheticPublicationRequest request,
        List<CloudFileObservation> observations,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + request.Timeout;
        do
        {
            var observation = stateReader.Observe(path);
            observations.Add(observation);
            if (observation.IsConfirmedInSync)
            {
                return true;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(request.PollInterval, cancellationToken);
        }
        while (true);
    }

    private static async Task CreateSyntheticDatabaseAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=DELETE; CREATE TABLE synthetic_probe (id INTEGER PRIMARY KEY, value TEXT NOT NULL); INSERT INTO synthetic_probe(value) VALUES ('M02 synthetic payload');";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> ReadIntegrityAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }
}
