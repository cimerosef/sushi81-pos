using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record DirectedTransferMarker(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("transferId")] string TransferId,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("sourceDeviceId")] string SourceDeviceId,
    [property: JsonPropertyName("targetDeviceId")] string TargetDeviceId,
    [property: JsonPropertyName("snapshotChecksum")] string SnapshotChecksum,
    [property: JsonPropertyName("snapshotByteLength")] long SnapshotByteLength,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("markerType")] string MarkerType,
    [property: JsonPropertyName("markerState")] string MarkerState)
{
    public bool IsValid => FormatVersion == DurableAuthorityState.CurrentFormatVersion
        && Guid.TryParse(TransferId, out _)
        && Guid.TryParse(LineageId, out _)
        && Generation >= 0
        && HandoffVersion >= 1
        && !string.IsNullOrWhiteSpace(SourceDeviceId)
        && !string.IsNullOrWhiteSpace(TargetDeviceId)
        && !string.Equals(SourceDeviceId, TargetDeviceId, StringComparison.Ordinal)
        && SnapshotChecksum is { Length: 64 } checksum
        && checksum.All(Uri.IsHexDigit)
        && SnapshotByteLength > 0
        && CreatedAtUtc > DateTimeOffset.UnixEpoch
        && CreatedAtUtc.Offset == TimeSpan.Zero
        && (MarkerType is "ready" or "grant")
        && MarkerState == "released";
}

public sealed record DirectedTargetValidationResult(
    bool IsValid,
    string Code,
    string Message,
    DirectedTransferMarker? Ready = null,
    DirectedTransferMarker? Grant = null);

public static class DirectedTransferMarkerPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public static async Task<DirectedTransferOperationResult> PublishAsync(
        DirectedTransferIdentity transfer,
        string handoffDirectory,
        string snapshotPath,
        IDirectedTransferFailureInjector? failureInjector = null,
        CancellationToken cancellationToken = default)
    {
        if (!transfer.IsValid)
        {
            return new(false, "invalid-transfer", "Transfer identity is invalid.");
        }

        if (!File.Exists(snapshotPath))
        {
            return new(false, "missing-snapshot", "The directed transfer snapshot is missing.");
        }

        // Marker publication is itself a release boundary.  Refuse to publish
        // anything unless the immutable snapshot is a readable, integrity-checked
        // SQLite database with a stable checksum and length.
        var evidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshotPath, syncConfirmed: true, cancellationToken);
        if (!evidence.IsValid)
        {
            return new(false, "sqlite-integrity-failure", "The directed transfer snapshot failed SQLite integrity validation.");
        }

        Directory.CreateDirectory(handoffDirectory);
        var checksum = evidence.SnapshotChecksum;
        var length = evidence.SnapshotByteLength;
        var createdAtUtc = DateTimeOffset.UtcNow;
        var baseName = "directed-" + transfer.TransferId;
        var readyPath = Path.Combine(handoffDirectory, baseName + ".ready.json");
        var grantPath = Path.Combine(handoffDirectory, baseName + ".grant.json");
        var ready = new DirectedTransferMarker(1, transfer.TransferId, transfer.LineageId, transfer.Generation, transfer.HandoffVersion, transfer.SourceDeviceId, transfer.TargetDeviceId, checksum, length, createdAtUtc, "ready", "released");
        var grant = ready with { MarkerType = "grant" };

        failureInjector?.OnFailurePoint(DirectedTransferFailurePoint.BeforeReadyMarker);
        var readyResult = await CreateOrVerifyAsync(readyPath, ready, cancellationToken);
        if (!readyResult.Succeeded)
        {
            return new(false, readyResult.Code, readyResult.Message);
        }

        failureInjector?.OnFailurePoint(DirectedTransferFailurePoint.BeforeGrantMarker);
        var grantResult = await CreateOrVerifyAsync(grantPath, grant, cancellationToken);
        if (!grantResult.Succeeded)
        {
            return new(false, grantResult.Code, grantResult.Message);
        }

        return new(true, "markers-published", "Matching ready and grant markers were published after durable relinquishment.", Validation: new(true, "valid", "Markers are present and internally consistent.", ready, grant));
    }

    public static async Task<DirectedTargetValidationResult> ValidateAsync(
        string handoffDirectory,
        string snapshotPath,
        DirectedTransferIdentity expectedTransfer,
        string receivingDeviceId,
        CancellationToken cancellationToken = default)
    {
        if (!expectedTransfer.IsValid || !string.Equals(expectedTransfer.TargetDeviceId, receivingDeviceId, StringComparison.Ordinal))
        {
            return new(false, "wrong-target", "The receiving device is not the exact directed target.");
        }

        if (!File.Exists(snapshotPath))
        {
            return new(false, "missing-snapshot", "The directed transfer snapshot is missing.");
        }

        var evidence = await DirectedSnapshotEvidence.CaptureAsync(expectedTransfer, snapshotPath, syncConfirmed: true, cancellationToken);
        if (!evidence.IsValid)
        {
            return new(false, "sqlite-integrity-failure", "The directed transfer snapshot failed SQLite integrity validation.");
        }

        var baseName = "directed-" + expectedTransfer.TransferId;
        var ready = await ReadAsync(Path.Combine(handoffDirectory, baseName + ".ready.json"), cancellationToken);
        if (ready.Marker is null)
        {
            return new(false, ready.Code, ready.Message);
        }

        var grant = await ReadAsync(Path.Combine(handoffDirectory, baseName + ".grant.json"), cancellationToken);
        if (grant.Marker is null)
        {
            return new(false, grant.Code, grant.Message, ready.Marker);
        }

        if (!Matches(ready.Marker, expectedTransfer, "ready") || !Matches(grant.Marker, expectedTransfer, "grant"))
        {
            return new(false, "stale-or-mismatched", "Ready/grant metadata does not match the exact directed transfer.", ready.Marker, grant.Marker);
        }

        var checksum = await ComputeSha256Async(snapshotPath, cancellationToken);
        var length = new FileInfo(snapshotPath).Length;
        if (!string.Equals(checksum, ready.Marker.SnapshotChecksum, StringComparison.OrdinalIgnoreCase)
            || length != ready.Marker.SnapshotByteLength
            || !string.Equals(checksum, grant.Marker.SnapshotChecksum, StringComparison.OrdinalIgnoreCase)
            || length != grant.Marker.SnapshotByteLength)
        {
            return new(false, "snapshot-mismatch", "Snapshot checksum or byte length does not match the markers.", ready.Marker, grant.Marker);
        }

        return new(true, "valid", "Exact-target directed transfer markers and snapshot are valid.", ready.Marker, grant.Marker);
    }

    private static bool Matches(DirectedTransferMarker marker, DirectedTransferIdentity transfer, string markerType) => marker.IsValid
        && marker.MarkerType == markerType
        && marker.TransferId == transfer.TransferId
        && marker.LineageId == transfer.LineageId
        && marker.Generation == transfer.Generation
        && marker.HandoffVersion == transfer.HandoffVersion
        && marker.SourceDeviceId == transfer.SourceDeviceId
        && marker.TargetDeviceId == transfer.TargetDeviceId;

    private static async Task<(bool Succeeded, string Code, string Message)> CreateOrVerifyAsync(string path, DirectedTransferMarker expected, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            var existing = await ReadAsync(path, cancellationToken);
            return existing.Marker is not null && SameImmutableContent(existing.Marker, expected)
                ? (true, "already-published", "The immutable marker already matches this transfer.")
                : (false, "marker-collision", "An immutable marker already exists with different content.");
        }

        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, expected, JsonOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
        return (true, "published", "Marker published.");
    }

    private static bool SameImmutableContent(DirectedTransferMarker existing, DirectedTransferMarker expected) =>
        existing with { CreatedAtUtc = expected.CreatedAtUtc } == expected;

    private static async Task<(DirectedTransferMarker? Marker, string Code, string Message)> ReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return (null, "missing-marker", "A required directed transfer marker is missing.");
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var marker = await JsonSerializer.DeserializeAsync<DirectedTransferMarker>(stream, JsonOptions, cancellationToken);
            return marker is { IsValid: true }
                ? (marker, "valid", "Marker is valid.")
                : (null, "corrupt-marker", "A directed transfer marker is malformed or invalid.");
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return (null, "corrupt-marker", "A directed transfer marker cannot be read: " + exception.Message);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
