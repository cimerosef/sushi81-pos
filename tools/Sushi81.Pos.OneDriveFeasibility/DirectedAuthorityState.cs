using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

public enum DirectedAuthorityMode
{
    Uninitialized,
    Authoritative,
    TransferPrepared,
    RelinquishedBlocked,
    Released,
    RecoveryRequired
}

public sealed record DirectedTransferIdentity(
    [property: JsonPropertyName("transferId")] string TransferId,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("sourceDeviceId")] string SourceDeviceId,
    [property: JsonPropertyName("targetDeviceId")] string TargetDeviceId)
{
    public bool IsValid => Guid.TryParse(TransferId, out _)
        && Guid.TryParse(LineageId, out _)
        && Generation >= 0
        && HandoffVersion >= 1
        && !string.IsNullOrWhiteSpace(SourceDeviceId)
        && !string.IsNullOrWhiteSpace(TargetDeviceId)
        && !string.Equals(SourceDeviceId, TargetDeviceId, StringComparison.Ordinal);
}

public sealed record DirectedMarkerEvidence(
    [property: JsonPropertyName("readyMarkerPath")] string ReadyMarkerPath,
    [property: JsonPropertyName("grantMarkerPath")] string GrantMarkerPath,
    [property: JsonPropertyName("snapshotChecksum")] string SnapshotChecksum,
    [property: JsonPropertyName("snapshotByteLength")] long SnapshotByteLength,
    [property: JsonPropertyName("readySyncConfirmed")] bool ReadySyncConfirmed,
    [property: JsonPropertyName("grantSyncConfirmed")] bool GrantSyncConfirmed)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(ReadyMarkerPath)
        && !string.IsNullOrWhiteSpace(GrantMarkerPath)
        && SnapshotChecksum is { Length: 64 } checksum
        && checksum.All(Uri.IsHexDigit)
        && SnapshotByteLength > 0
        && ReadySyncConfirmed
        && GrantSyncConfirmed;
}

/// <summary>
/// Device-local authority cursor.  It is deliberately independent from the
/// optional append-only audit ledger: a device can decide whether it may write
/// using only this cursor and its local target cursor.
/// </summary>
public sealed record DirectedLocalAuthorityCursor(
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion)
{
    public bool IsValid => Guid.TryParse(LineageId, out _)
        && Generation >= 0
        && HandoffVersion >= 1;

    public bool Matches(DirectedTransferIdentity transfer) =>
        IsValid
        && transfer.IsValid
        && LineageId == transfer.LineageId
        && Generation == transfer.Generation
        && HandoffVersion == transfer.HandoffVersion;
}

public sealed record DurableAuthorityState(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("mode")] DirectedAuthorityMode Mode,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("pairedDeviceIds")] string[] PairedDeviceIds,
    [property: JsonPropertyName("transfer")] DirectedTransferIdentity? Transfer,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("closedWithAuthority")] bool ClosedWithAuthority,
    [property: JsonPropertyName("snapshotEvidence")] DirectedSnapshotEvidence? SnapshotEvidence = null,
    [property: JsonPropertyName("markerEvidence")] DirectedMarkerEvidence? MarkerEvidence = null,
    [property: JsonPropertyName("authorityCursor")] DirectedLocalAuthorityCursor? AuthorityCursor = null,
    [property: JsonPropertyName("grantReceipt")] GitHubAssetReceipt? GrantReceipt = null)
{
    public const int CurrentFormatVersion = 1;

    public bool IsValid
    {
        get
        {
            if (FormatVersion != CurrentFormatVersion || Revision < 0 || string.IsNullOrWhiteSpace(DeviceId)
                || PairedDeviceIds is null
                || PairedDeviceIds.Any(id => string.IsNullOrWhiteSpace(id))
                || PairedDeviceIds.Distinct(StringComparer.Ordinal).Count() != PairedDeviceIds.Length
                || !PairedDeviceIds.Contains(DeviceId, StringComparer.Ordinal)
                || UpdatedAtUtc <= DateTimeOffset.UnixEpoch || UpdatedAtUtc.Offset != TimeSpan.Zero)
            {
                return false;
            }

            if (AuthorityCursor is { IsValid: false }) return false;
            if (GrantReceipt is { IsValid: false }) return false;

            if (Mode is DirectedAuthorityMode.Authoritative or DirectedAuthorityMode.Uninitialized)
            {
                return Transfer is null && SnapshotEvidence is null && MarkerEvidence is null && GrantReceipt is null;
            }

            if (Transfer is not { IsValid: true } transfer
                || !string.Equals(transfer.SourceDeviceId, DeviceId, StringComparison.Ordinal))
            {
                return false;
            }

            if (Mode == DirectedAuthorityMode.TransferPrepared)
            {
                return SnapshotEvidence is null && MarkerEvidence is null && GrantReceipt is null
                    && (AuthorityCursor is null
                        || AuthorityCursor.LineageId == transfer.LineageId
                        && AuthorityCursor.Generation == transfer.Generation
                        && AuthorityCursor.HandoffVersion < transfer.HandoffVersion);
            }

            if (SnapshotEvidence is not { IsValid: true } evidence || evidence.Transfer != transfer)
            {
                return false;
            }

            if (Mode == DirectedAuthorityMode.RelinquishedBlocked)
            {
                return MarkerEvidence is null && GrantReceipt is null
                    && AuthorityCursor is { } relinquishedCursor
                    && relinquishedCursor.Matches(transfer);
            }

            return Mode == DirectedAuthorityMode.Released
                && MarkerEvidence is { IsValid: true } marker
                && marker.SnapshotChecksum == evidence.SnapshotChecksum
                && marker.SnapshotByteLength == evidence.SnapshotByteLength
                && AuthorityCursor is { } releasedCursor
                && releasedCursor.Matches(transfer);
        }
    }
}

public interface IDurableAuthorityStateFailureInjector
{
    void BeforeCommit(DurableAuthorityState nextState);
}

public sealed class DurableAuthorityStateStore(
    string statePath,
    IDurableAuthorityStateFailureInjector? failureInjector = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public string StatePath { get; } = Path.GetFullPath(statePath);

    public DurableAuthorityState Load()
    {
        if (!File.Exists(StatePath))
        {
            throw new InvalidDataException("Durable authority state is missing; authority is unresolved.");
        }

        try
        {
            using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            var state = JsonSerializer.Deserialize<DurableAuthorityState>(stream, JsonOptions);
            if (state is null || !state.IsValid)
            {
                throw new InvalidDataException("Durable authority state is invalid; authority is blocked.");
            }

            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Durable authority state is malformed; authority is blocked.", exception);
        }
    }

    public void Save(DurableAuthorityState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsValid)
        {
            throw new InvalidDataException("Refusing to persist invalid durable authority state.");
        }

        failureInjector?.BeforeCommit(state);
        var directory = Path.GetDirectoryName(StatePath) ?? throw new InvalidDataException("Authority state path has no directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = StatePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, state, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(StatePath))
            {
                File.Replace(temporaryPath, StatePath, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, StatePath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
