using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

/// <summary>
/// File-backed OneDrive System membership/seed seam. It is deliberately not wired into
/// authority startup: membership and seeds are never an authority grant or a distributed lock.
/// </summary>
public sealed class JsonSystemMetadataStore : ISystemMetadataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string oneDriveRoot;
    private readonly TimeProvider clock;

    public JsonSystemMetadataStore(string configuredOneDriveRoot, TimeProvider? clock = null)
    {
        if (string.IsNullOrWhiteSpace(configuredOneDriveRoot))
            throw new ArgumentException("A configured OneDrive root is required.", nameof(configuredOneDriveRoot));

        oneDriveRoot = Path.GetFullPath(configuredOneDriveRoot);
        if (Path.GetPathRoot(oneDriveRoot) is null)
            throw new ArgumentException("The configured OneDrive root must be an absolute path.", nameof(configuredOneDriveRoot));

        this.clock = clock ?? TimeProvider.System;
    }

    public string SystemDirectoryPath => Path.Combine(oneDriveRoot, SystemMetadataContract.SystemDirectoryName);

    public async Task<SystemLineageMetadata> EnsureCurrentLineageAsync(
        Guid lineageId,
        long generation,
        CancellationToken cancellationToken = default)
    {
        if (lineageId == Guid.Empty) throw new ArgumentException("A lineage ID is required.", nameof(lineageId));
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);

        var expected = new SystemLineageMetadata(
            SystemMetadataContract.SchemaVersion,
            SystemMetadataContract.ProtocolVersion,
            lineageId,
            generation,
            clock.GetUtcNow());
        expected.Validate();
        var path = Path.Combine(
            SystemDirectoryPath,
            SystemMetadataContract.LineageDirectoryName,
            SystemMetadataContract.LineageFileName);
        var result = await WriteOrReuseJsonAsync(
            path,
            expected,
            static (existing, requested) => existing.LineageId == requested.LineageId
                && existing.CurrentGeneration == requested.CurrentGeneration,
            cancellationToken);
        result.Value.Validate();
        return result.Value;
    }

    public async Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(
            SystemDirectoryPath,
            SystemMetadataContract.LineageDirectoryName,
            SystemMetadataContract.LineageFileName);
        var lineage = await ReadJsonAsync<SystemLineageMetadata>(path, cancellationToken);
        lineage.Validate();
        return lineage;
    }

    public async Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(
        Guid deviceId,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (deviceId == Guid.Empty) throw new ArgumentException("A device ID is required.", nameof(deviceId));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A device display name is required.", nameof(displayName));

        var lineage = await ReadLineageAsync(cancellationToken);
        var normalizedDisplayName = displayName.Trim();
        var artifact = new DeviceRegistrationArtifact(
            SystemMetadataContract.SchemaVersion,
            SystemMetadataContract.ProtocolVersion,
            SystemMetadataContract.DeviceArtifactKind,
            deviceId,
            normalizedDisplayName,
            lineage.LineageId,
            lineage.CurrentGeneration,
            clock.GetUtcNow());
        artifact.Validate();

        await RejectContradictoryDeviceIdentityAsync(artifact, cancellationToken);

        var path = DeviceArtifactPath(artifact.LineageId, artifact.Generation, artifact.DeviceId);
        var registration = await WriteOrReuseJsonAsync(
            path,
            artifact,
            static (existing, expected) => existing.DeviceId == expected.DeviceId
                && existing.LineageId == expected.LineageId
                && existing.Generation == expected.Generation,
            cancellationToken);

        ValidatedReadOnlySeed? seed = null;
        try
        {
            seed = await FindValidatedReadOnlySeedAsync(lineage.LineageId, lineage.CurrentGeneration, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidDataException or SystemMetadataUnavailableException)
        {
            // A missing/corrupt optional seed keeps the new device paired and read-only. The
            // caller can surface the diagnostic and still enter the separately fenced DR flow.
        }

        return new DeviceSelfJoinResult(registration.Value, registration.Created, seed);
    }

    public async Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(
        Guid lineageId,
        long generation,
        CancellationToken cancellationToken = default)
    {
        ValidateBinding(lineageId, generation);
        var lineage = await ReadLineageAsync(cancellationToken);
        if (lineage.LineageId != lineageId || lineage.CurrentGeneration != generation)
            throw new InvalidDataException("The requested device generation is not the configured current lineage generation.");

        var directory = DevicesDirectoryPath(generation);
        if (!Directory.Exists(directory)) return Array.Empty<DeviceRegistrationArtifact>();

        var devices = new List<DeviceRegistrationArtifact>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.device.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var device = await ReadJsonAsync<DeviceRegistrationArtifact>(path, cancellationToken);
            device.Validate();
            if (device.LineageId != lineageId || device.Generation != generation)
                throw new InvalidDataException($"The registration artifact '{path}' is for a different lineage or generation.");

            var expectedFileName = SystemMetadataContract.DeviceFileName(device.DeviceId);
            if (!string.Equals(Path.GetFileName(path), expectedFileName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The registration artifact '{path}' is not bound to its device identity.");

            devices.Add(device);
        }

        return devices.OrderBy(device => device.DeviceId).ToArray();
    }

    public async Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(
        Guid lineageId,
        long generation,
        CancellationToken cancellationToken = default)
    {
        ValidateBinding(lineageId, generation);
        var lineage = await ReadLineageAsync(cancellationToken);
        if (lineage.LineageId != lineageId || lineage.CurrentGeneration != generation)
            throw new InvalidDataException("The requested seed generation is not the configured current lineage generation.");

        var directory = SeedsDirectoryPath(generation);
        if (!Directory.Exists(directory)) return null;

        var candidates = new List<ValidatedReadOnlySeed>();
        foreach (var metadataPath in Directory.EnumerateFiles(directory, "*.seed.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await ReadJsonAsync<ReadOnlySeedMetadata>(metadataPath, cancellationToken);
            metadata.Validate();
            if (metadata.LineageId != lineageId || metadata.Generation != generation)
                throw new InvalidDataException($"The seed metadata '{metadataPath}' is for a different lineage or generation.");

            var expectedMetadataName = SystemMetadataContract.SeedFileName(metadata.SeedId);
            if (!string.Equals(Path.GetFileName(metadataPath), expectedMetadataName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The seed metadata '{metadataPath}' is not bound to its seed identity.");

            var payloadPath = Path.Combine(directory, metadata.PayloadFileName);
            await ValidatePayloadAsync(payloadPath, metadata, cancellationToken);
            candidates.Add(new ValidatedReadOnlySeed(metadata, payloadPath));
        }

        return candidates
            .OrderByDescending(seed => seed.Metadata.BusinessRevision)
            .ThenByDescending(seed => seed.Metadata.CreatedAtUtc)
            .ThenBy(seed => seed.Metadata.SeedId)
            .FirstOrDefault();
    }

    public async Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(
        ReadOnlySeedMetadata metadata,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        metadata.Validate();
        var lineage = await ReadLineageAsync(cancellationToken);
        if (metadata.LineageId != lineage.LineageId || metadata.Generation != lineage.CurrentGeneration)
            throw new InvalidDataException("A seed may only be published for the configured current lineage generation.");

        var actualHash = Convert.ToHexString(SHA256.HashData(payload.Span));
        if (metadata.PayloadSize != payload.Length
            || !string.Equals(metadata.PayloadSha256, actualHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The seed metadata does not match the supplied payload.");

        var directory = SeedsDirectoryPath(metadata.Generation);
        Directory.CreateDirectory(directory);
        var payloadPath = Path.Combine(directory, metadata.PayloadFileName);
        await WriteOrReuseBytesAsync(payloadPath, payload, cancellationToken);

        var metadataPath = Path.Combine(directory, SystemMetadataContract.SeedFileName(metadata.SeedId));
        var write = await WriteOrReuseJsonAsync(
            metadataPath,
            metadata,
            static (existing, expected) => existing == expected,
            cancellationToken);

        return new ReadOnlySeedPublicationResult(write.Value, write.Created);
    }

    private async Task RejectContradictoryDeviceIdentityAsync(
        DeviceRegistrationArtifact expected,
        CancellationToken cancellationToken)
    {
        var devicesRoot = Path.Combine(SystemDirectoryPath, SystemMetadataContract.DevicesDirectoryName);
        if (!Directory.Exists(devicesRoot)) return;

        foreach (var path in Directory.EnumerateFiles(devicesRoot, "*.device.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await ReadJsonAsync<DeviceRegistrationArtifact>(path, cancellationToken);
            existing.Validate();
            if (existing.DeviceId == expected.DeviceId && existing.LineageId != expected.LineageId)
                throw new InvalidDataException("The immutable device identity is already bound to another lineage.");
            if (existing.DeviceId == expected.DeviceId && existing.Generation > expected.Generation)
                throw new InvalidDataException("The device registration is ahead of the configured lineage generation.");
        }
    }

    private static async Task ValidatePayloadAsync(
        string payloadPath,
        ReadOnlySeedMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(payloadPath))
            throw new InvalidDataException($"The read-only seed payload '{payloadPath}' is missing.");

        var payload = await File.ReadAllBytesAsync(payloadPath, cancellationToken);
        var actualHash = Convert.ToHexString(SHA256.HashData(payload));
        if (payload.LongLength != metadata.PayloadSize
            || !string.Equals(actualHash, metadata.PayloadSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The read-only seed payload '{payloadPath}' failed size or SHA-256 validation.");

        await ValidateSqlitePayloadAsync(payloadPath, cancellationToken);
    }

    private static async Task ValidateSqlitePayloadAsync(string payloadPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = payloadPath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false
            }.ToString());
            await connection.OpenAsync(cancellationToken);

            await using var integrityCommand = connection.CreateCommand();
            integrityCommand.CommandText = "PRAGMA integrity_check;";
            var integrity = Convert.ToString(await integrityCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The read-only seed payload '{payloadPath}' failed SQLite integrity validation.");

            await using var schemaCommand = connection.CreateCommand();
            schemaCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
            var schemaExists = Convert.ToInt32(await schemaCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
            if (!schemaExists)
                throw new InvalidDataException($"The read-only seed payload '{payloadPath}' does not contain the expected application schema marker.");
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException($"The read-only seed payload '{payloadPath}' is not a readable SQLite database.", exception);
        }
    }

    private string DevicesDirectoryPath(long generation) => Path.Combine(
        SystemDirectoryPath,
        SystemMetadataContract.DevicesDirectoryName,
        generation.ToString(CultureInfo.InvariantCulture));

    private string DeviceArtifactPath(Guid lineageId, long generation, Guid deviceId) => Path.Combine(
        DevicesDirectoryPath(generation),
        SystemMetadataContract.DeviceFileName(deviceId));

    private string SeedsDirectoryPath(long generation) => Path.Combine(
        SystemDirectoryPath,
        SystemMetadataContract.SeedsDirectoryName,
        generation.ToString(CultureInfo.InvariantCulture));

    private static void ValidateBinding(Guid lineageId, long generation)
    {
        if (lineageId == Guid.Empty) throw new ArgumentException("A lineage ID is required.", nameof(lineageId));
        ArgumentOutOfRangeException.ThrowIfLessThan(generation, 1);
    }

    private static async Task<(T Value, bool Created)> WriteOrReuseJsonAsync<T>(
        string path,
        T expected,
        Func<T, T, bool> isSameBinding,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            var existing = await ReadJsonAsync<T>(path, cancellationToken);
            if (!isSameBinding(existing, expected))
                throw new InvalidDataException($"The existing metadata artifact '{path}' contradicts the requested binding.");
            return (existing, false);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, expected, SerializerOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, path, overwrite: false);
                var persisted = await ReadJsonAsync<T>(path, cancellationToken);
                return (persisted, true);
            }
            catch (IOException) when (File.Exists(path))
            {
                var existing = await ReadJsonAsync<T>(path, cancellationToken);
                if (!isSameBinding(existing, expected))
                    throw new InvalidDataException($"The concurrently-created metadata artifact '{path}' contradicts the requested binding.");
                return (existing, false);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task WriteOrReuseBytesAsync(
        string path,
        ReadOnlyMemory<byte> expected,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            var existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (!existing.AsSpan().SequenceEqual(expected.Span))
                throw new InvalidDataException($"The existing seed payload '{path}' contradicts the requested payload.");
            return;
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(expected, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, path, overwrite: false);
            }
            catch (IOException) when (File.Exists(path))
            {
                var existing = await File.ReadAllBytesAsync(path, cancellationToken);
                if (!existing.AsSpan().SequenceEqual(expected.Span))
                    throw new InvalidDataException($"The concurrently-created seed payload '{path}' contradicts the requested payload.");
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) throw new SystemMetadataUnavailableException($"The metadata artifact '{path}' is unavailable.");
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
                ?? throw new InvalidDataException($"The metadata artifact '{path}' contains no object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The metadata artifact '{path}' is malformed.", exception);
        }
    }
}
