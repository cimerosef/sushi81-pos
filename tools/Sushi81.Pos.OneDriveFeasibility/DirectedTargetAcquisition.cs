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
    IArtifactSyncObserver? syncObserver = null,
    DirectedLifecycleLedgerStore? lifecycleLedger = null,
    DurableAuthorityStateStore? sourceStateStore = null,
    DurableLocalAuthorityCursorStore? localCursorStore = null)
{
    public DurableTargetAcquisitionStore StateStore { get; } = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    public string LocalDeviceId { get; } = string.IsNullOrWhiteSpace(localDeviceId)
        ? throw new ArgumentException("A local target device ID is required.", nameof(localDeviceId))
        : localDeviceId;
    public IReadOnlySet<string>? PairedDeviceIds { get; } = pairedDeviceIds?.ToHashSet(StringComparer.Ordinal);
    /// <summary>Optional diagnostic history; never consulted for write authority.</summary>
    public DirectedLifecycleLedgerStore? LifecycleLedger { get; } = lifecycleLedger;
    public DurableAuthorityStateStore? SourceStateStore { get; } = sourceStateStore;
    public DurableLocalAuthorityCursorStore LocalCursorStore { get; } = localCursorStore
        ?? new DurableLocalAuthorityCursorStore(Path.Combine(
            Path.GetDirectoryName(stateStore.StatePath) ?? ".",
            "local-authority-cursor.json"));
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

    public bool MayBusinessWrite(DirectedTransferIdentity transfer)
    {
        if (transfer is null || !transfer.IsValid || !IsPaired(transfer)) return false;
        try
        {
            var state = Current;
            if (state is null) return false;
            var sourceState = SourceStateStore is not null && File.Exists(SourceStateStore.StatePath)
                ? SourceStateStore.Load()
                : null;
            var localCursor = LocalCursorStore.Exists ? LocalCursorStore.Load() : null;
            return new DirectedLifecycleAuthorityGate(LocalDeviceId, sourceStateStore: SourceStateStore, targetStateStore: StateStore, localCursorStore: LocalCursorStore)
                .Evaluate(transfer, sourceState, state, localCursor)
                .MayWrite;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

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

        DurableLocalAuthorityCursorState? localCursor = null;
        if (LocalCursorStore.Exists)
        {
            try { localCursor = LocalCursorStore.Load(); }
            catch (InvalidDataException exception)
            {
                return new(false, "local-authority-unresolved", exception.Message);
            }
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

            if (existing.Matches(expectedTransfer, LocalDeviceId)
                && localCursor is { CurrentRole: DurableLocalAuthorityRole.AcquiredTarget }
                && localCursor.Matches(expectedTransfer)
                && localCursor.HighWaterHandoffVersion == existing.HandoffVersion
                && IsCurrentLocalCursor(expectedTransfer, existing, localCursor))
            {
                return new(true, "already-acquired", "The exact target acquisition is already durably recorded.", TargetState: existing);
            }

            if (existing.Matches(expectedTransfer, LocalDeviceId)
                && localCursor is not { CurrentRole: DurableLocalAuthorityRole.AcquisitionPending }
                && localCursor is not { CurrentRole: DurableLocalAuthorityRole.AcquiredTarget })
            {
                return new(false, "local-authority-cursor-missing", "The target evidence exists without a complete current local cursor; retry cannot infer a virgin device.");
            }

            var pendingExactRetry = existing.Matches(expectedTransfer, LocalDeviceId)
                && localCursor is { CurrentRole: DurableLocalAuthorityRole.AcquisitionPending }
                && localCursor.Matches(expectedTransfer);
            if (!pendingExactRetry && !CanAdvanceCursor(existing, expectedTransfer, out var cursorFailure))
            {
                return new(false, cursorFailure, "A different or stale durable target acquisition cannot replace the current cursor.");
            }
        }
        else if (localCursor is not null)
        {
            var releasedPredecessor = SourceStateStore is not null && File.Exists(SourceStateStore.StatePath)
                ? TryLoadReleasedPredecessor(SourceStateStore, localCursor, expectedTransfer)
                : false;
            if (localCursor.CurrentRole != DurableLocalAuthorityRole.AcquisitionPending
                && !releasedPredecessor
                && !(localCursor.CurrentRole == DurableLocalAuthorityRole.AcquiredTarget && localCursor.Matches(expectedTransfer)))
            {
                return new(false, "local-authority-unresolved", "Existing local participation state cannot be replaced by a new target acquisition.");
            }
        }
        else if (expectedTransfer.HandoffVersion != 1)
        {
            return new(false, "local-authority-cursor-missing", "A first target acquisition must establish local handoff version 1.");
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

        if (existing is not null)
        {
            next = next with { Revision = existing.Revision + 1 };
        }

        try
        {
            var sourceState = SourceStateStore is not null && File.Exists(SourceStateStore.StatePath)
                ? SourceStateStore.Load()
                : null;
            var previousHighWater = localCursor?.HighWaterHandoffVersion ?? 0;
            var pendingCursor = localCursor is { CurrentRole: DurableLocalAuthorityRole.AcquisitionPending }
                ? localCursor
                : new DurableLocalAuthorityCursorState(
                    DurableLocalAuthorityCursorState.CurrentFormatVersion,
                    (localCursor?.Revision ?? 0) + 1,
                    LocalDeviceId,
                    expectedTransfer.LineageId,
                    expectedTransfer.Generation,
                    previousHighWater,
                    DurableLocalAuthorityRole.AcquisitionPending,
                    expectedTransfer.TransferId,
                    DateTimeOffset.UtcNow);
            if (!pendingCursor.Matches(expectedTransfer) || pendingCursor.DeviceId != LocalDeviceId)
            {
                return new(false, "local-authority-cursor-inconsistent", "The pending local cursor does not match the exact target transfer.", TargetState: next, Validation: validation, SyncObservations: syncObservations);
            }
            if (!LocalCursorStore.Exists || localCursor?.CurrentRole != DurableLocalAuthorityRole.AcquisitionPending)
            {
                LocalCursorStore.Save(pendingCursor);
                localCursor = pendingCursor;
            }

            var finalCursor = pendingCursor with
            {
                Revision = pendingCursor.Revision + 1,
                HighWaterHandoffVersion = expectedTransfer.HandoffVersion,
                CurrentRole = DurableLocalAuthorityRole.AcquiredTarget,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            var localDecision = new DirectedLifecycleAuthorityGate(LocalDeviceId, sourceStateStore: SourceStateStore, targetStateStore: StateStore)
                .Evaluate(expectedTransfer, sourceState, next, finalCursor);
            if (!localDecision.MayWrite)
            {
                return new(false, localDecision.Code, localDecision.Message, TargetState: next, Validation: validation, SyncObservations: syncObservations);
            }
        }
        catch (InvalidDataException exception)
        {
            return new(false, "local-state-unresolved", exception.Message, Validation: validation, SyncObservations: syncObservations);
        }

        try
        {
            StateStore.Save(next);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return new(false, "target-durable-write-failed", exception.Message, Validation: validation, SyncObservations: syncObservations);
        }

        try
        {
            var pending = LocalCursorStore.Load();
            var finalCursor = pending with
            {
                Revision = pending.Revision + 1,
                HighWaterHandoffVersion = expectedTransfer.HandoffVersion,
                CurrentRole = DurableLocalAuthorityRole.AcquiredTarget,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            LocalCursorStore.Save(finalCursor);
            return new(true, "target-acquired", "Exact target validation and durable acquisition completed with a durable local cursor.", Validation: validation, TargetState: next, SyncObservations: syncObservations);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return new(false, "local-authority-cursor-failed", exception.Message, Validation: validation, TargetState: next, SyncObservations: syncObservations);
        }
    }

    private bool IsPaired(DirectedTransferIdentity transfer) =>
        PairedDeviceIds is null
        || PairedDeviceIds.Contains(transfer.SourceDeviceId)
        && PairedDeviceIds.Contains(transfer.TargetDeviceId)
        && PairedDeviceIds.Contains(LocalDeviceId);

    private bool IsCurrentLocalCursor(
        DirectedTransferIdentity transfer,
        DurableTargetAcquisitionState state,
        DurableLocalAuthorityCursorState? localCursor = null)
    {
        try
        {
            var sourceState = SourceStateStore is not null && File.Exists(SourceStateStore.StatePath)
                ? SourceStateStore.Load()
                : null;
            localCursor ??= LocalCursorStore.Exists ? LocalCursorStore.Load() : null;
            return new DirectedLifecycleAuthorityGate(LocalDeviceId, sourceStateStore: SourceStateStore, targetStateStore: StateStore, localCursorStore: LocalCursorStore)
                .Evaluate(transfer, sourceState, state, localCursor)
                .MayWrite;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private bool CanAdvanceCursor(
        DurableTargetAcquisitionState existing,
        DirectedTransferIdentity expectedTransfer,
        out string failureCode)
    {
        failureCode = "stale-target-state";
        if (existing.LineageId != expectedTransfer.LineageId || existing.Generation != expectedTransfer.Generation)
        {
            return false;
        }

        if (existing.DeviceId != LocalDeviceId || expectedTransfer.TargetDeviceId != LocalDeviceId
            || expectedTransfer.HandoffVersion <= existing.HandoffVersion
            || expectedTransfer.TransferId == existing.TransferId)
        {
            failureCode = "stale-target-state";
            return false;
        }

        if (SourceStateStore is null || !File.Exists(SourceStateStore.StatePath) || !LocalCursorStore.Exists)
        {
            failureCode = "local-authority-cursor-missing";
            return false;
        }

        DurableAuthorityState source;
        try { source = SourceStateStore.Load(); }
        catch (InvalidDataException)
        {
            failureCode = "local-source-state-unresolved";
            return false;
        }

        DurableLocalAuthorityCursorState cursor;
        try { cursor = LocalCursorStore.Load(); }
        catch (InvalidDataException)
        {
            failureCode = "local-authority-cursor-unresolved";
            return false;
        }

        if (source.Mode != DirectedAuthorityMode.Released
            || source.Transfer is not { IsValid: true } releasedTransfer
            || source.AuthorityCursor is not { } releasedCursor
            || !releasedCursor.Matches(releasedTransfer)
            || cursor.CurrentRole != DurableLocalAuthorityRole.Released
            || !cursor.Matches(releasedTransfer)
            || releasedTransfer.LineageId != expectedTransfer.LineageId
            || releasedTransfer.Generation != expectedTransfer.Generation
            || cursor.HighWaterHandoffVersion + 1 != expectedTransfer.HandoffVersion
            || releasedTransfer.TargetDeviceId != expectedTransfer.SourceDeviceId)
        {
            failureCode = "stale-target-state";
            return false;
        }

        failureCode = "target-cursor-advance-allowed";
        return true;
    }

    private static bool TryLoadReleasedPredecessor(
        DurableAuthorityStateStore sourceStateStore,
        DurableLocalAuthorityCursorState cursor,
        DirectedTransferIdentity expectedTransfer)
    {
        try
        {
            var source = sourceStateStore.Load();
            return source.Mode == DirectedAuthorityMode.Released
                && source.Transfer is { IsValid: true } releasedTransfer
                && source.AuthorityCursor is { } releasedCursor
                && releasedCursor.Matches(releasedTransfer)
                && cursor.CurrentRole == DurableLocalAuthorityRole.Released
                && cursor.Matches(releasedTransfer)
                && releasedTransfer.LineageId == expectedTransfer.LineageId
                && releasedTransfer.Generation == expectedTransfer.Generation
                && releasedTransfer.HandoffVersion + 1 == expectedTransfer.HandoffVersion
                && releasedTransfer.TargetDeviceId == expectedTransfer.SourceDeviceId;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
