using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

public enum DurableTargetAcquisitionStatus
{
    Acquired
}

/// <summary>
/// The durable target-side fact that gates business writes.  It repeats every
/// directed identity and immutable snapshot fact so a target cannot infer
/// authority from an in-memory validation flag or an arbitrary visible file.
/// </summary>
public sealed record DurableTargetAcquisitionState(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("sourceDeviceId")] string SourceDeviceId,
    [property: JsonPropertyName("targetDeviceId")] string TargetDeviceId,
    [property: JsonPropertyName("transferId")] string TransferId,
    [property: JsonPropertyName("snapshotPath")] string SnapshotPath,
    [property: JsonPropertyName("snapshotChecksum")] string SnapshotChecksum,
    [property: JsonPropertyName("snapshotByteLength")] long SnapshotByteLength,
    [property: JsonPropertyName("status")] DurableTargetAcquisitionStatus Status,
    [property: JsonPropertyName("updatedAtUtc")] DateTimeOffset UpdatedAtUtc)
{
    public const int CurrentFormatVersion = 1;

    public bool IsValid => FormatVersion == CurrentFormatVersion
        && Revision >= 1
        && !string.IsNullOrWhiteSpace(DeviceId)
        && Guid.TryParse(LineageId, out _)
        && Generation >= 0
        && HandoffVersion >= 1
        && !string.IsNullOrWhiteSpace(SourceDeviceId)
        && !string.IsNullOrWhiteSpace(TargetDeviceId)
        && !string.Equals(SourceDeviceId, TargetDeviceId, StringComparison.Ordinal)
        && string.Equals(DeviceId, TargetDeviceId, StringComparison.Ordinal)
        && Guid.TryParse(TransferId, out _)
        && !string.IsNullOrWhiteSpace(SnapshotPath)
        && Path.IsPathFullyQualified(SnapshotPath)
        && SnapshotChecksum is { Length: 64 } checksum
        && checksum.All(Uri.IsHexDigit)
        && SnapshotByteLength > 0
        && Status == DurableTargetAcquisitionStatus.Acquired
        && UpdatedAtUtc > DateTimeOffset.UnixEpoch
        && UpdatedAtUtc.Offset == TimeSpan.Zero;

    public bool Matches(DirectedTransferIdentity transfer, string localDeviceId) =>
        IsValid
        && string.Equals(DeviceId, localDeviceId, StringComparison.Ordinal)
        && string.Equals(LineageId, transfer.LineageId, StringComparison.Ordinal)
        && Generation == transfer.Generation
        && HandoffVersion == transfer.HandoffVersion
        && string.Equals(SourceDeviceId, transfer.SourceDeviceId, StringComparison.Ordinal)
        && string.Equals(TargetDeviceId, transfer.TargetDeviceId, StringComparison.Ordinal)
        && string.Equals(TransferId, transfer.TransferId, StringComparison.Ordinal);
}

public interface IDurableTargetAcquisitionFailureInjector
{
    void BeforeCommit(DurableTargetAcquisitionState nextState);
}

public sealed class DurableTargetAcquisitionStore(
    string statePath,
    IDurableTargetAcquisitionFailureInjector? failureInjector = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false,
        Converters = { new JsonStringEnumConverter() }
    };

    public string StatePath { get; } = Path.GetFullPath(statePath);

    public DurableTargetAcquisitionState Load()
    {
        if (!File.Exists(StatePath))
        {
            throw new InvalidDataException("Durable target acquisition state is missing; target remains blocked.");
        }

        try
        {
            using var stream = new FileStream(StatePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096);
            var state = JsonSerializer.Deserialize<DurableTargetAcquisitionState>(stream, JsonOptions);
            if (state is null || !state.IsValid)
            {
                throw new InvalidDataException("Durable target acquisition state is invalid; target remains blocked.");
            }

            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Durable target acquisition state is malformed; target remains blocked.", exception);
        }
    }

    public void Save(DurableTargetAcquisitionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!state.IsValid)
        {
            throw new InvalidDataException("Refusing to persist invalid durable target acquisition state.");
        }

        failureInjector?.BeforeCommit(state);
        var directory = Path.GetDirectoryName(StatePath) ?? throw new InvalidDataException("Target state path has no directory.");
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

public sealed class DirectedTargetAcquisitionCoordinator(
    DurableTargetAcquisitionStore stateStore,
    string localDeviceId,
    IEnumerable<string>? pairedDeviceIds = null,
    IArtifactSyncObserver? syncObserver = null)
{
    public DurableTargetAcquisitionStore StateStore { get; } = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    public string LocalDeviceId { get; } = string.IsNullOrWhiteSpace(localDeviceId)
        ? throw new ArgumentException("A local target device ID is required.", nameof(localDeviceId))
        : localDeviceId;
    public IReadOnlySet<string>? PairedDeviceIds { get; } = pairedDeviceIds?.ToHashSet(StringComparer.Ordinal);
    private IArtifactSyncObserver? SyncObserver { get; } = syncObserver;

    public DurableTargetAcquisitionState? Current
    {
        get
        {
            try { return StateStore.Load(); }
            catch (InvalidDataException) { return null; }
            catch (IOException) { return null; }
        }
    }

    public bool MayBusinessWrite(DirectedTransferIdentity transfer) =>
        transfer is not null && transfer.IsValid && IsPaired(transfer) && Current is { } state && state.Matches(transfer, LocalDeviceId);

    public async Task<DirectedTransferOperationResult> AcquireAsync(
        string handoffDirectory,
        string snapshotPath,
        DirectedTransferIdentity expectedTransfer,
        CancellationToken cancellationToken = default)
    {
        if (!expectedTransfer.IsValid || !string.Equals(expectedTransfer.TargetDeviceId, LocalDeviceId, StringComparison.Ordinal))
        {
            return new(false, "wrong-target", "Only the exact directed target may acquire this transfer.");
        }
        if (!IsPaired(expectedTransfer))
        {
            return new(false, "unpaired-target", "The source and target are not both present in the target's paired device set.");
        }

        DurableTargetAcquisitionState? existing = null;
        if (File.Exists(StateStore.StatePath))
        {
            try { existing = StateStore.Load(); }
            catch (InvalidDataException exception)
            {
                return new(false, "target-state-unresolved", exception.Message);
            }
            catch (IOException exception)
            {
                return new(false, "target-state-unresolved", exception.Message);
            }

            if (existing.Matches(expectedTransfer, LocalDeviceId))
            {
                return new(true, "already-acquired", "The exact target acquisition is already durably recorded.", TargetState: existing);
            }

            return new(false, "stale-target-state", "A different or stale durable target acquisition already exists; it cannot be overwritten.");
        }

        IReadOnlyList<ArtifactSyncObservation>? syncObservations = null;
        if (SyncObserver is not null)
        {
            syncObservations =
            [
                SyncObserver.Observe(snapshotPath),
                SyncObserver.Observe(Path.Combine(handoffDirectory, "directed-" + expectedTransfer.TransferId + ".ready.json")),
                SyncObserver.Observe(Path.Combine(handoffDirectory, "directed-" + expectedTransfer.TransferId + ".grant.json"))
            ];
            if (syncObservations.Any(observation => !observation.IsConfirmedInSync))
            {
                return new(false, "target-artifacts-not-synchronized", "Device B requires observer-confirmed IN_SYNC for the snapshot and both directed markers.", SyncObservations: syncObservations);
            }
        }

        var validation = await DirectedTransferMarkerPublisher.ValidateAsync(
            handoffDirectory,
            snapshotPath,
            expectedTransfer,
            LocalDeviceId,
            cancellationToken);
        if (!validation.IsValid || validation.Ready is null || validation.Grant is null)
        {
            return new(false, validation.Code, validation.Message, Validation: validation, SyncObservations: syncObservations);
        }

        var next = new DurableTargetAcquisitionState(
            DurableTargetAcquisitionState.CurrentFormatVersion,
            1,
            LocalDeviceId,
            expectedTransfer.LineageId,
            expectedTransfer.Generation,
            expectedTransfer.HandoffVersion,
            expectedTransfer.SourceDeviceId,
            expectedTransfer.TargetDeviceId,
            expectedTransfer.TransferId,
            Path.GetFullPath(snapshotPath),
            validation.Ready.SnapshotChecksum,
            validation.Ready.SnapshotByteLength,
            DurableTargetAcquisitionStatus.Acquired,
            DateTimeOffset.UtcNow);

        try
        {
            StateStore.Save(next);
            return new(true, "target-acquired", "Exact target validation and durable acquisition completed.", Validation: validation, TargetState: next, SyncObservations: syncObservations);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return new(false, "target-durable-write-failed", exception.Message, Validation: validation, SyncObservations: syncObservations);
        }
    }

    private bool IsPaired(DirectedTransferIdentity transfer) =>
        PairedDeviceIds is null
        || PairedDeviceIds.Contains(transfer.SourceDeviceId)
        && PairedDeviceIds.Contains(transfer.TargetDeviceId)
        && PairedDeviceIds.Contains(LocalDeviceId);
}
