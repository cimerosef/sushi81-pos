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

public sealed record DurableAuthorityState(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("mode")] DirectedAuthorityMode Mode,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("pairedDeviceIds")] string[] PairedDeviceIds,
    [property: JsonPropertyName("transfer")] DirectedTransferIdentity? Transfer,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc,
    [property: JsonPropertyName("closedWithAuthority")] bool ClosedWithAuthority,
    [property: JsonPropertyName("snapshotEvidence")] DirectedSnapshotEvidence? SnapshotEvidence = null)
{
    public const int CurrentFormatVersion = 1;

    public bool IsValid => FormatVersion == CurrentFormatVersion
        && Revision >= 0
        && !string.IsNullOrWhiteSpace(DeviceId)
        && PairedDeviceIds is not null
        && PairedDeviceIds.All(id => !string.IsNullOrWhiteSpace(id))
        && PairedDeviceIds.Distinct(StringComparer.Ordinal).Count() == PairedDeviceIds.Length
        && PairedDeviceIds.Contains(DeviceId, StringComparer.Ordinal)
        && UpdatedAtUtc > DateTimeOffset.UnixEpoch
        && UpdatedAtUtc.Offset == TimeSpan.Zero
        && (Mode is DirectedAuthorityMode.Authoritative or DirectedAuthorityMode.Uninitialized
            ? Transfer is null && SnapshotEvidence is null
            : Transfer is { IsValid: true } transfer
                && string.Equals(transfer.SourceDeviceId, DeviceId, StringComparison.Ordinal)
                && (Mode is DirectedAuthorityMode.TransferPrepared
                    ? SnapshotEvidence is null
                    : SnapshotEvidence is { IsValid: true } evidence && evidence.Transfer == transfer));
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
