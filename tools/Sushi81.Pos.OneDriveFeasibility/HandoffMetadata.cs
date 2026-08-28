using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record HandoffMetadata(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("sourceDeviceId")] string SourceDeviceId,
    [property: JsonPropertyName("checksumAlgorithm")] string ChecksumAlgorithm,
    [property: JsonPropertyName("snapshotChecksum")] string SnapshotChecksum,
    [property: JsonPropertyName("snapshotByteLength")] long SnapshotByteLength,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("markerType")] string MarkerType,
    [property: JsonPropertyName("markerState")] string MarkerState);

public sealed record HandoffValidationResult(bool IsValid, string Code, string Message, HandoffMetadata? Metadata = null)
{
    public static HandoffValidationResult Fail(string code, string message) => new(false, code, message);
    public static HandoffValidationResult Pass(HandoffMetadata metadata) => new(true, "valid", "The immutable snapshot and matching ready marker are valid.", metadata);
}

public static class HandoffMetadataCodec
{
    public const int CurrentFormatVersion = 1;
    public const string ReadyMarkerType = "ready";
    public const string ReleasedMarkerState = "released";
    public const string Sha256Algorithm = "SHA-256";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public static async Task WriteAsync(string path, HandoffMetadata metadata, CancellationToken cancellationToken = default)
    {
        ValidateMetadata(metadata);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, metadata, Options, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<(HandoffMetadata? Metadata, string? Error)> ReadStrictAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return (null, "The ready marker is missing.");
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var metadata = await JsonSerializer.DeserializeAsync<HandoffMetadata>(stream, Options, cancellationToken);
            if (metadata is null)
            {
                return (null, "The ready marker is empty.");
            }

            ValidateMetadata(metadata);
            return (metadata, null);
        }
        catch (JsonException exception)
        {
            return (null, $"Malformed ready marker: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            return (null, exception.Message);
        }
        catch (IOException exception)
        {
            return (null, $"Ready marker cannot be read: {exception.Message}");
        }
    }

    public static void ValidateMetadata(HandoffMetadata? metadata)
    {
        if (metadata is null)
        {
            throw new InvalidDataException("Metadata is missing.");
        }

        if (metadata.FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException($"Unsupported metadata format version: {metadata.FormatVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (!Guid.TryParse(metadata.LineageId, out _)
            || metadata.Generation < 0
            || metadata.HandoffVersion < 1
            || string.IsNullOrWhiteSpace(metadata.SourceDeviceId)
            || metadata.SourceDeviceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(metadata.ChecksumAlgorithm, Sha256Algorithm, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.SnapshotChecksum)
            || metadata.SnapshotChecksum is null
            || metadata.SnapshotChecksum.Length != 64
            || !metadata.SnapshotChecksum.All(Uri.IsHexDigit)
            || metadata.SnapshotByteLength < 1
            || metadata.CreatedAtUtc <= DateTimeOffset.UnixEpoch
            || metadata.CreatedAtUtc.Offset != TimeSpan.Zero
            || !string.Equals(metadata.MarkerType, ReadyMarkerType, StringComparison.Ordinal)
            || !string.Equals(metadata.MarkerState, ReleasedMarkerState, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Ready marker metadata is invalid or incomplete.");
        }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
