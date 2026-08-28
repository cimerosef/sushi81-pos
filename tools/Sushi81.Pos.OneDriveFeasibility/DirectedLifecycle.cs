using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

/// <summary>
/// Append-only completion history for the synthetic continuous lifecycle. It
/// prevents a target-to-next-source promotion from deleting or resetting the
/// prior acquisition and provides the monotonic handoff-version boundary.
/// </summary>
public sealed record DirectedLifecycleLedgerEntry(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("revision")] long Revision,
    [property: JsonPropertyName("lineageId")] string LineageId,
    [property: JsonPropertyName("generation")] long Generation,
    [property: JsonPropertyName("handoffVersion")] long HandoffVersion,
    [property: JsonPropertyName("transferId")] string TransferId,
    [property: JsonPropertyName("sourceDeviceId")] string SourceDeviceId,
    [property: JsonPropertyName("targetDeviceId")] string TargetDeviceId,
    [property: JsonPropertyName("snapshotPath")] string SnapshotPath,
    [property: JsonPropertyName("snapshotChecksum")] string SnapshotChecksum,
    [property: JsonPropertyName("snapshotByteLength")] long SnapshotByteLength,
    [property: JsonPropertyName("completedAtUtc")] DateTimeOffset CompletedAtUtc)
{
    public const int CurrentFormatVersion = 1;

    public bool IsValid => FormatVersion == CurrentFormatVersion
        && Revision >= 1
        && Guid.TryParse(LineageId, out _)
        && Generation >= 0
        && HandoffVersion >= 1
        && Guid.TryParse(TransferId, out _)
        && !string.IsNullOrWhiteSpace(SourceDeviceId)
        && !string.IsNullOrWhiteSpace(TargetDeviceId)
        && !string.Equals(SourceDeviceId, TargetDeviceId, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(SnapshotPath)
        && Path.IsPathFullyQualified(SnapshotPath)
        && SnapshotChecksum is { Length: 64 } checksum
        && checksum.All(Uri.IsHexDigit)
        && SnapshotByteLength > 0
        && CompletedAtUtc > DateTimeOffset.UnixEpoch
        && CompletedAtUtc.Offset == TimeSpan.Zero;

    public bool Matches(DirectedTransferIdentity transfer) =>
        IsValid
        && transfer.IsValid
        && LineageId == transfer.LineageId
        && Generation == transfer.Generation
        && HandoffVersion == transfer.HandoffVersion
        && TransferId == transfer.TransferId
        && SourceDeviceId == transfer.SourceDeviceId
        && TargetDeviceId == transfer.TargetDeviceId;
}

public sealed class DirectedLifecycleLedgerStore(string ledgerPath)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };

    public string LedgerPath { get; } = Path.GetFullPath(ledgerPath);

    public IReadOnlyList<DirectedLifecycleLedgerEntry> Load()
    {
        if (!File.Exists(LedgerPath)) return [];

        var entries = new List<DirectedLifecycleLedgerEntry>();
        try
        {
            foreach (var line in File.ReadLines(LedgerPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var entry = JsonSerializer.Deserialize<DirectedLifecycleLedgerEntry>(line, JsonOptions);
                if (entry is null || !entry.IsValid) throw new InvalidDataException("Lifecycle ledger contains an invalid entry; progression is blocked.");
                entries.Add(entry);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Lifecycle ledger is malformed; progression is blocked.", exception);
        }

        ValidateHistory(entries);
        return entries;
    }

    public void Append(DirectedLifecycleLedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsValid) throw new InvalidDataException("Refusing to append an invalid lifecycle entry.");

        var entries = Load().ToList();
        var sameTransfer = entries.SingleOrDefault(existing => existing.TransferId == entry.TransferId);
        if (sameTransfer is not null)
        {
            if (sameTransfer == entry) return;
            throw new InvalidDataException("A transfer ID is already recorded with different immutable content.");
        }

        var latest = entries
            .Where(existing => existing.LineageId == entry.LineageId && existing.Generation == entry.Generation)
            .OrderByDescending(existing => existing.HandoffVersion)
            .FirstOrDefault();
        if (latest is not null
            && (entry.HandoffVersion != latest.HandoffVersion + 1
                || !string.Equals(entry.SourceDeviceId, latest.TargetDeviceId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("Lifecycle handoff version or source progression is stale, replayed or not target-directed.");
        }

        var directory = Path.GetDirectoryName(LedgerPath) ?? throw new InvalidDataException("Lifecycle ledger path has no directory.");
        Directory.CreateDirectory(directory);
        using var stream = new FileStream(LedgerPath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream);
        writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void ValidateHistory(IReadOnlyList<DirectedLifecycleLedgerEntry> entries)
    {
        var duplicateRevisions = entries.GroupBy(entry => entry.Revision).Any(group => group.Count() > 1);
        if (duplicateRevisions) throw new InvalidDataException("Lifecycle ledger revisions are not unique.");

        var duplicateTransfers = entries.GroupBy(entry => entry.TransferId).Any(group => group.Count() > 1);
        if (duplicateTransfers) throw new InvalidDataException("Lifecycle ledger transfer IDs are not unique.");

        foreach (var group in entries.GroupBy(entry => (entry.LineageId, entry.Generation)))
        {
            var ordered = group.OrderBy(entry => entry.HandoffVersion).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].HandoffVersion != ordered[index - 1].HandoffVersion + 1
                    || ordered[index].SourceDeviceId != ordered[index - 1].TargetDeviceId)
                {
                    throw new InvalidDataException("Lifecycle ledger progression is not monotonic and target-directed.");
                }
            }
        }
    }
}

public sealed class DirectedContinuousLifecycleCoordinator(
    DirectedLifecycleLedgerStore? ledger,
    string localDeviceId,
    DurableLocalAuthorityCursorStore? localCursorStore = null)
{
    /// <summary>Optional append-only diagnostic history; never required for local authority safety.</summary>
    public DirectedLifecycleLedgerStore? Ledger { get; } = ledger;
    public string LocalDeviceId { get; } = string.IsNullOrWhiteSpace(localDeviceId)
        ? throw new ArgumentException("A local device ID is required.", nameof(localDeviceId))
        : localDeviceId;
    public DurableLocalAuthorityCursorStore? LocalCursorStore { get; } = localCursorStore;

    public DirectedTransferOperationResult ValidateNextTransfer(
        DirectedTransferIdentity transfer,
        IEnumerable<string> pairedDeviceIds,
        DurableAuthorityState? localSourceState = null)
    {
        var paired = pairedDeviceIds?.ToHashSet(StringComparer.Ordinal)
            ?? throw new ArgumentNullException(nameof(pairedDeviceIds));
        if (!transfer.IsValid || !paired.Contains(transfer.SourceDeviceId) || !paired.Contains(transfer.TargetDeviceId)
            || !paired.Contains(LocalDeviceId) || transfer.SourceDeviceId != LocalDeviceId)
        {
            return new(false, "unpaired-or-invalid-progression", "The next transfer is not a valid exact local-source member of the paired set.");
        }

        if (localSourceState is not null)
        {
            if (!localSourceState.IsValid
                || localSourceState.Mode != DirectedAuthorityMode.Authoritative
                || localSourceState.AuthorityCursor is not { } cursor
                || cursor.LineageId != transfer.LineageId
                || cursor.Generation != transfer.Generation
                || cursor.HandoffVersion + 1 != transfer.HandoffVersion)
            {
                return new(false, "stale-or-replayed-version", "The local authoritative cursor does not permit this exact next handoff version.");
            }

            return new(true, "next-transfer-valid", "The next transfer is monotonic from the device-local authority cursor.");
        }

        if (Ledger is null)
        {
            return transfer.HandoffVersion == 1
                ? new(true, "first-transfer-valid", "The first local transfer establishes the lineage cursor.")
                : new(false, "local-cursor-required", "A later transfer requires the current device-local authority cursor.");
        }

        IReadOnlyList<DirectedLifecycleLedgerEntry> entries;
        try { entries = Ledger.Load(); }
        catch (InvalidDataException exception) { return new(false, "lifecycle-state-unresolved", exception.Message); }

        var latest = entries
            .Where(entry => entry.LineageId == transfer.LineageId && entry.Generation == transfer.Generation)
            .OrderByDescending(entry => entry.HandoffVersion)
            .FirstOrDefault();
        var expectedVersion = latest?.HandoffVersion + 1 ?? 1;
        if (transfer.HandoffVersion != expectedVersion)
        {
            return new(false, "stale-or-replayed-version", $"Expected monotonic handoff version {expectedVersion}, received {transfer.HandoffVersion}.");
        }

        if (latest is not null && latest.TargetDeviceId != LocalDeviceId)
        {
            return new(false, "wrong-next-source", "Only the previously acquired target may become the next source.");
        }

        return new(true, "next-transfer-valid", "The next transfer is monotonic, same-lineage/generation and target-directed.");
    }

    public DirectedTransferOperationResult RecordCompletedTransfer(
        DurableAuthorityStateStore sourceStateStore,
        DurableTargetAcquisitionState acquisition)
    {
        ArgumentNullException.ThrowIfNull(sourceStateStore);
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!acquisition.IsValid)
        {
            return new(false, "invalid-target-state", "Only valid durable target acquisition may complete a lifecycle leg.");
        }

        DurableAuthorityState source;
        try { source = sourceStateStore.Load(); }
        catch (InvalidDataException exception) { return new(false, "source-state-unresolved", exception.Message); }
        if (source.Mode != DirectedAuthorityMode.Released || source.Transfer is not { IsValid: true } transfer
            || source.SnapshotEvidence is not { IsValid: true } evidence
            || acquisition.LineageId != transfer.LineageId
            || acquisition.Generation != transfer.Generation
            || acquisition.HandoffVersion != transfer.HandoffVersion
            || acquisition.TransferId != transfer.TransferId
            || acquisition.SourceDeviceId != transfer.SourceDeviceId
            || acquisition.TargetDeviceId != transfer.TargetDeviceId
            || acquisition.SnapshotChecksum != evidence.SnapshotChecksum
            || acquisition.SnapshotByteLength != evidence.SnapshotByteLength)
        {
            return new(false, "lifecycle-completion-mismatch", "Released source and durable target acquisition do not describe the same transfer.");
        }

        if (Ledger is null)
        {
            return new(false, "audit-ledger-unconfigured", "No diagnostic ledger is configured; local authority safety does not require one.");
        }

        var entries = Ledger.Load();
        var revision = entries.Count == 0 ? 1 : entries.Max(entry => entry.Revision) + 1;
        var entry = new DirectedLifecycleLedgerEntry(
            DirectedLifecycleLedgerEntry.CurrentFormatVersion,
            revision,
            acquisition.LineageId,
            acquisition.Generation,
            acquisition.HandoffVersion,
            acquisition.TransferId,
            acquisition.SourceDeviceId,
            acquisition.TargetDeviceId,
            acquisition.SnapshotPath,
            acquisition.SnapshotChecksum,
            acquisition.SnapshotByteLength,
            DateTimeOffset.UtcNow);
        try
        {
            Ledger.Append(entry);
            return new(true, "lifecycle-leg-complete", "Released source and durable target acquisition were appended without deleting prior state.");
        }
        catch (InvalidDataException exception)
        {
            return new(false, "lifecycle-ledger-write-failed", exception.Message);
        }
    }

    /// <summary>
    /// Records audit history from the target's local durable cursor only. This
    /// overload is intentionally suitable for a distributed test or operator
    /// diagnostic: it never reads a source device's local state.
    /// </summary>
    public DirectedTransferOperationResult RecordCompletedTransfer(DurableTargetAcquisitionState acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (Ledger is null)
        {
            return new(false, "audit-ledger-unconfigured", "No diagnostic ledger is configured; local authority safety does not require one.");
        }
        if (!acquisition.IsValid)
        {
            return new(false, "invalid-target-state", "Only valid durable target acquisition may be recorded in audit history.");
        }

        var entries = Ledger.Load();
        var revision = entries.Count == 0 ? 1 : entries.Max(entry => entry.Revision) + 1;
        var entry = new DirectedLifecycleLedgerEntry(
            DirectedLifecycleLedgerEntry.CurrentFormatVersion,
            revision,
            acquisition.LineageId,
            acquisition.Generation,
            acquisition.HandoffVersion,
            acquisition.TransferId,
            acquisition.SourceDeviceId,
            acquisition.TargetDeviceId,
            acquisition.SnapshotPath,
            acquisition.SnapshotChecksum,
            acquisition.SnapshotByteLength,
            DateTimeOffset.UtcNow);
        try
        {
            Ledger.Append(entry);
            return new(true, "audit-leg-recorded", "The durable target cursor was appended to optional audit history.");
        }
        catch (InvalidDataException exception)
        {
            return new(false, "lifecycle-ledger-write-failed", exception.Message);
        }
    }

    public DirectedTransferOperationResult PromoteAcquiredTargetToSource(
        DurableTargetAcquisitionState acquisition,
        string authorityStatePath,
        IEnumerable<string> pairedDeviceIds)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        var paired = pairedDeviceIds?.ToArray() ?? throw new ArgumentNullException(nameof(pairedDeviceIds));
        if (!acquisition.IsValid || acquisition.DeviceId != LocalDeviceId || !paired.Contains(LocalDeviceId, StringComparer.Ordinal))
        {
            return new(false, "promotion-forbidden", "Only the exact durably acquired target may become the next source.");
        }

        try
        {
            var authority = new DurableAuthorityStateStore(authorityStatePath);
            var cursorStore = LocalCursorStore ?? new DurableLocalAuthorityCursorStore(Path.Combine(
                Path.GetDirectoryName(authority.StatePath) ?? ".",
                "local-authority-cursor.json"));
            if (!cursorStore.Exists)
            {
                return new(false, "local-authority-cursor-missing", "The local participation/current-authority cursor is missing; target promotion is blocked.", TargetState: acquisition);
            }
            DurableLocalAuthorityCursorState cursor;
            try { cursor = cursorStore.Load(); }
            catch (InvalidDataException exception)
            {
                return new(false, "local-authority-cursor-unresolved", exception.Message, TargetState: acquisition);
            }
            if (!string.Equals(cursor.DeviceId, LocalDeviceId, StringComparison.Ordinal))
            {
                return new(false, "local-authority-cursor-inconsistent", "The local participation cursor belongs to another device.", TargetState: acquisition);
            }
            var acquisitionIdentity = new DirectedTransferIdentity(
                    acquisition.TransferId,
                    acquisition.LineageId,
                    acquisition.Generation,
                    acquisition.HandoffVersion,
                    acquisition.SourceDeviceId,
                    acquisition.TargetDeviceId);
            if (cursor.CurrentRole is not (DurableLocalAuthorityRole.AcquiredTarget or DurableLocalAuthorityRole.Authoritative or DurableLocalAuthorityRole.Released))
            {
                return new(false, "local-authority-cursor-inconsistent", "Target promotion requires a consistent local acquired-target or released cursor.", TargetState: acquisition);
            }

            if (File.Exists(authority.StatePath))
            {
                var existing = authority.Load();
                if (existing.DeviceId != LocalDeviceId)
                    return new(false, "promotion-state-collision", "An existing authority state belongs to another device and cannot be replaced.", existing);
                if (existing.Mode == DirectedAuthorityMode.Authoritative)
                {
                    if (existing.AuthorityCursor is { } currentCursor
                        && currentCursor.LineageId == acquisition.LineageId
                        && currentCursor.Generation == acquisition.Generation
                        && currentCursor.HandoffVersion == acquisition.HandoffVersion)
                    {
                        var cursorMatchesAcquisition = cursor.CurrentRole == DurableLocalAuthorityRole.Authoritative
                            ? cursor.LineageId == acquisition.LineageId
                                && cursor.Generation == acquisition.Generation
                                && cursor.HighWaterHandoffVersion == acquisition.HandoffVersion
                            : cursor.CurrentRole == DurableLocalAuthorityRole.AcquiredTarget
                                && cursor.Matches(acquisitionIdentity);
                        if (!cursorMatchesAcquisition)
                        {
                            return new(false, "promotion-state-collision", "The local cursor does not match the authoritative source state.", existing, TargetState: acquisition);
                        }
                        if (cursor.CurrentRole != DurableLocalAuthorityRole.Authoritative)
                        {
                            var finalized = cursor with
                            {
                                Revision = cursor.Revision + 1,
                                CurrentRole = DurableLocalAuthorityRole.Authoritative,
                                HighWaterHandoffVersion = acquisition.HandoffVersion,
                                CurrentTransferId = null,
                                UpdatedAtUtc = DateTimeOffset.UtcNow
                            };
                            cursorStore.Save(finalized);
                        }
                        return new(true, "already-promoted", "The acquired target is already the authoritative next source.", existing);
                    }
                    return new(false, "promotion-state-collision", "An existing authoritative state does not match the local acquired-target cursor.", existing, TargetState: acquisition);
                }
                if (existing.Mode != DirectedAuthorityMode.Released
                    || existing.Transfer is not { IsValid: true } releasedTransfer
                    || releasedTransfer.LineageId != acquisition.LineageId
                    || releasedTransfer.Generation != acquisition.Generation
                    || existing.AuthorityCursor is not { } releasedCursor
                    || cursor.CurrentRole != DurableLocalAuthorityRole.AcquiredTarget
                    || cursor.LineageId != releasedTransfer.LineageId
                    || cursor.Generation != releasedTransfer.Generation
                    || releasedCursor.HandoffVersion + 1 != acquisition.HandoffVersion
                    || !string.Equals(releasedTransfer.TargetDeviceId, acquisition.SourceDeviceId, StringComparison.Ordinal))
                {
                    return new(false, "stale-target-promotion", "The target cursor is not the exact next successor to this device's released local cursor.", existing, null, acquisition);
                }

                // The source file is a current cursor. Its prior Released
                // evidence remains represented by the optional append-only
                // audit history while this atomic write advances the cursor.
                var advanced = new DurableAuthorityState(
                    DurableAuthorityState.CurrentFormatVersion,
                    existing.Revision + 1,
                    DirectedAuthorityMode.Authoritative,
                    LocalDeviceId,
                    paired,
                    null,
                    DateTimeOffset.UtcNow,
                    false,
                    null,
                    null,
                    new DirectedLocalAuthorityCursor(acquisition.LineageId, acquisition.Generation, acquisition.HandoffVersion));
                authority.Save(advanced);
                cursorStore.Save(cursor with
                {
                    Revision = cursor.Revision + 1,
                    CurrentRole = DurableLocalAuthorityRole.Authoritative,
                    HighWaterHandoffVersion = acquisition.HandoffVersion,
                    CurrentTransferId = null,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
                return new(true, "target-promoted-to-source", "Latest durable target promoted to the next source; prior source evidence remains in local durable history.", advanced);
            }

            if (acquisition.HandoffVersion != 1
                || cursor.CurrentRole != DurableLocalAuthorityRole.AcquiredTarget
                || !cursor.Matches(acquisitionIdentity))
            {
                return new(false, "promotion-local-cursor-missing", "A later target cannot be promoted without this device's prior local authority cursor.", TargetState: acquisition);
            }

            var promoted = new DurableAuthorityState(
                DurableAuthorityState.CurrentFormatVersion,
                0,
                DirectedAuthorityMode.Authoritative,
                LocalDeviceId,
                paired,
                null,
                DateTimeOffset.UtcNow,
                false,
                null,
                null,
                new DirectedLocalAuthorityCursor(acquisition.LineageId, acquisition.Generation, acquisition.HandoffVersion));
            authority.Save(promoted);
            cursorStore.Save(cursor with
            {
                Revision = cursor.Revision + 1,
                CurrentRole = DurableLocalAuthorityRole.Authoritative,
                HighWaterHandoffVersion = acquisition.HandoffVersion,
                CurrentTransferId = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
            return new(true, "target-promoted-to-source", "Durably acquired target promoted to the next source without deleting acquisition or audit history.", promoted);
        }
        catch (InvalidDataException exception)
        {
            return new(false, "lifecycle-state-unresolved", exception.Message);
        }
        catch (IOException exception)
        {
            return new(false, "promotion-durable-write-failed", exception.Message, TargetState: acquisition);
        }
    }
}

/// <summary>
/// Centralized lifecycle-aware business-write decision. Durable source and
/// target records are interpreted as a device-local high-water/cursor. The
/// optional ledger is diagnostic history only and is deliberately not read by
/// the safety-critical decision.
/// </summary>
public sealed class DirectedLifecycleAuthorityGate(
    string localDeviceId,
    DirectedLifecycleLedgerStore? lifecycleLedger = null,
    DurableAuthorityStateStore? sourceStateStore = null,
    DurableTargetAcquisitionStore? targetStateStore = null,
    DurableLocalAuthorityCursorStore? localCursorStore = null)
{
    public string LocalDeviceId { get; } = string.IsNullOrWhiteSpace(localDeviceId)
        ? throw new ArgumentException("A local device ID is required.", nameof(localDeviceId))
        : localDeviceId;
    /// <summary>Optional test/audit history; never a safety input.</summary>
    public DirectedLifecycleLedgerStore? AuditLedger { get; } = lifecycleLedger;
    public DurableAuthorityStateStore? SourceStateStore { get; } = sourceStateStore;
    public DurableTargetAcquisitionStore? TargetStateStore { get; } = targetStateStore;
    public DurableLocalAuthorityCursorStore? LocalCursorStore { get; } = localCursorStore;

    public DirectedLifecycleAuthorityDecision Evaluate(DirectedTransferIdentity transfer)
    {
        DurableAuthorityState? source = null;
        DurableTargetAcquisitionState? target = null;
        DurableLocalAuthorityCursorState? cursor = null;
        try
        {
            if (SourceStateStore is not null && File.Exists(SourceStateStore.StatePath)) source = SourceStateStore.Load();
            if (TargetStateStore is not null && File.Exists(TargetStateStore.StatePath)) target = TargetStateStore.Load();
            cursor = LoadCursor();
        }
        catch (InvalidDataException exception)
        {
            return new(false, "durable-state-unresolved", exception.Message);
        }

        return Evaluate(transfer, source, target, cursor);
    }

    public DirectedLifecycleAuthorityDecision EvaluateSource(DurableAuthorityState sourceState)
    {
        ArgumentNullException.ThrowIfNull(sourceState);
        DurableLocalAuthorityCursorState? cursor;
        try { cursor = LoadCursor(); }
        catch (InvalidDataException exception) { return new(false, "local-authority-cursor-unresolved", exception.Message); }
        return EvaluateSource(sourceState, cursor);
    }

    public DirectedLifecycleAuthorityDecision EvaluateSource(
        DurableAuthorityState sourceState,
        DurableLocalAuthorityCursorState? localCursorState)
    {
        ArgumentNullException.ThrowIfNull(sourceState);
        if (!sourceState.IsValid || sourceState.Mode != DirectedAuthorityMode.Authoritative
            || sourceState.ClosedWithAuthority
            || !string.Equals(sourceState.DeviceId, LocalDeviceId, StringComparison.Ordinal))
        {
            return new(false, "source-not-current-authority", "Durable source state is not the current writable authority.");
        }

        if (localCursorState is null)
        {
            return new(false, "local-authority-cursor-missing", "The local participation/current-authority cursor is missing; source authority is unresolved.");
        }
        if (!IsConsistentAuthoritativeCursor(sourceState, localCursorState))
        {
            return new(false, "local-authority-cursor-inconsistent", "The local participation cursor does not consistently describe the durable source authority.");
        }

        return new(true, "current-source-authority", "The local durable source cursor is the current lifecycle authority holder.", LocalHighWaterVersion: localCursorState.HighWaterHandoffVersion);
    }

    public DirectedLifecycleAuthorityDecision Evaluate(
        DirectedTransferIdentity transfer,
        DurableAuthorityState? sourceState,
        DurableTargetAcquisitionState? targetState,
        DurableLocalAuthorityCursorState? localCursorState = null)
    {
        if (!transfer.IsValid || !string.Equals(LocalDeviceId, transfer.SourceDeviceId, StringComparison.Ordinal)
            && !string.Equals(LocalDeviceId, transfer.TargetDeviceId, StringComparison.Ordinal))
        {
            return new(false, "not-participant", "The local device is not an exact source or target for this transfer.");
        }

        if (targetState is null)
        {
            return new(false, "target-acquisition-missing", "No durable target acquisition is available for the current lifecycle cursor.");
        }
        if (!targetState.Matches(transfer, LocalDeviceId))
        {
            return new(false, "target-acquisition-stale", "The durable target cursor does not match the requested transfer and local target.");
        }

        if (localCursorState is null)
        {
            return new(false, "local-authority-cursor-missing", "The local participation/current-authority cursor is missing; target authority is unresolved.");
        }

        if (!localCursorState.IsValid || !string.Equals(localCursorState.DeviceId, LocalDeviceId, StringComparison.Ordinal))
        {
            return new(false, "local-authority-cursor-unresolved", "The local participation/current-authority cursor is invalid or belongs to another device.");
        }

        var localHighWater = Math.Max(localCursorState.HighWaterHandoffVersion, targetState.HandoffVersion);
        if (localCursorState.CurrentRole != DurableLocalAuthorityRole.AcquiredTarget
            || !localCursorState.Matches(transfer)
            || localCursorState.HighWaterHandoffVersion != targetState.HandoffVersion)
        {
            return new(false, "local-authority-cursor-inconsistent", "The local current cursor does not consistently describe the exact durable target acquisition.", LocalHighWaterVersion: localHighWater);
        }

        if (sourceState is null)
        {
            return new(true, "current-target-authority", "The exact durable target acquisition is backed by the local acquired-target cursor.", LocalHighWaterVersion: localHighWater);
        }

        if (!sourceState.IsValid || !string.Equals(sourceState.DeviceId, LocalDeviceId, StringComparison.Ordinal))
        {
            return new(false, "local-source-state-unresolved", "The local source state is invalid or belongs to another device.", LocalHighWaterVersion: localHighWater);
        }

        if (sourceState.Mode == DirectedAuthorityMode.Authoritative)
        {
            return new(false, "local-source-still-authoritative", "A device-local authoritative source cursor cannot simultaneously authorize a target cursor.", LocalHighWaterVersion: localHighWater);
        }
        if (sourceState.Mode != DirectedAuthorityMode.Released
            || sourceState.Transfer is not { IsValid: true } releasedTransfer
            || sourceState.AuthorityCursor is not { } releasedCursor
            || !releasedCursor.Matches(releasedTransfer))
        {
            return new(false, "local-source-not-released", "The local source state does not prove the exact predecessor was released.", LocalHighWaterVersion: localHighWater);
        }

        if (transfer.LineageId != releasedTransfer.LineageId
            || transfer.Generation != releasedTransfer.Generation
            || transfer.HandoffVersion != releasedTransfer.HandoffVersion + 1
            || !string.Equals(transfer.SourceDeviceId, releasedTransfer.TargetDeviceId, StringComparison.Ordinal))
        {
            return new(false, "target-acquisition-stale", "The target cursor is not the exact next local successor to the released source cursor.", LocalHighWaterVersion: localHighWater);
        }

        return new(true, "current-target-authority", "The target cursor is the exact next local successor to the released source cursor.", LocalHighWaterVersion: localHighWater);
    }

    private DurableLocalAuthorityCursorState? LoadCursor()
    {
        var store = EffectiveCursorStore();
        return store is not null && store.Exists ? store.Load() : null;
    }

    private DurableLocalAuthorityCursorStore? EffectiveCursorStore()
    {
        if (LocalCursorStore is not null) return LocalCursorStore;
        var statePath = SourceStateStore?.StatePath ?? TargetStateStore?.StatePath;
        return statePath is null
            ? null
            : new DurableLocalAuthorityCursorStore(Path.Combine(Path.GetDirectoryName(statePath) ?? ".", "local-authority-cursor.json"));
    }

    private bool IsConsistentAuthoritativeCursor(DurableAuthorityState sourceState, DurableLocalAuthorityCursorState cursor)
    {
        if (!cursor.IsValid || cursor.DeviceId != LocalDeviceId) return false;
        return sourceState.Mode switch
        {
            DirectedAuthorityMode.Authoritative when sourceState.AuthorityCursor is null =>
                cursor.CurrentRole == DurableLocalAuthorityRole.InitialAuthoritative && cursor.IsVirginLineage && cursor.HighWaterHandoffVersion == 0,
            DirectedAuthorityMode.Authoritative when sourceState.AuthorityCursor is { } authorityCursor =>
                cursor.CurrentRole == DurableLocalAuthorityRole.Authoritative
                && cursor.LineageId == authorityCursor.LineageId
                && cursor.Generation == authorityCursor.Generation
                && cursor.HighWaterHandoffVersion == authorityCursor.HandoffVersion
                && cursor.CurrentTransferId is null,
            _ => false
        };
    }
}

public sealed record DirectedLifecycleAuthorityDecision(
    bool MayWrite,
    string Code,
    string Message,
    DirectedLifecycleLedgerEntry? CurrentEntry = null,
    long? LocalHighWaterVersion = null);
