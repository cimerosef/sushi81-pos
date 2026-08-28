namespace Sushi81.Pos.OneDriveFeasibility;

public enum DirectedTransferFailurePoint
{
    BeforeMarkerPublication,
    BeforeReadyMarker,
    BeforeGrantMarker,
    BeforeMarkerSynchronization,
    BeforeReleasedCommit
}

public interface IDirectedTransferFailureInjector
{
    void OnFailurePoint(DirectedTransferFailurePoint point);
}

public sealed record DirectedTransferOperationResult(
    bool Succeeded,
    string Code,
    string Message,
    DurableAuthorityState? State = null,
    DirectedTargetValidationResult? Validation = null,
    DurableTargetAcquisitionState? TargetState = null,
    IReadOnlyList<ArtifactSyncObservation>? SyncObservations = null);

/// <summary>
/// Synthetic directed-transfer state machine. It persists relinquishment before publishing
/// any release marker and permanently blocks the source after that durable transition.
/// </summary>
public sealed class DirectedHandoffCoordinator(
    DurableAuthorityStateStore stateStore,
    IDirectedTransferFailureInjector? failureInjector = null,
    IArtifactSyncObserver? syncObserver = null,
    DirectedLifecycleLedgerStore? lifecycleLedger = null,
    DurableLocalAuthorityCursorStore? localCursorStore = null)
{
    private readonly IArtifactSyncObserver syncObserver = syncObserver ?? new DeterministicArtifactSyncObserver();
    public DirectedLifecycleLedgerStore? LifecycleLedger { get; } = lifecycleLedger;
    public DurableLocalAuthorityCursorStore LocalCursorStore { get; } = localCursorStore
        ?? new DurableLocalAuthorityCursorStore(Path.Combine(
            Path.GetDirectoryName(stateStore.StatePath) ?? ".",
            "local-authority-cursor.json"));

    public DurableAuthorityState Current => stateStore.Load();

    /// <summary>
    /// Central durable write decision for the source-side feasibility model.
    /// Unknown, closed, prepared or relinquished states are never writable.
    /// </summary>
    public bool MayBusinessWrite(string deviceId)
    {
        var state = TryLoad();
        if (state is null || !string.Equals(state.DeviceId, deviceId, StringComparison.Ordinal)) return false;
        try
        {
            return new DirectedLifecycleAuthorityGate(deviceId, localCursorStore: LocalCursorStore)
                .EvaluateSource(state)
                .MayWrite;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public DirectedTransferOperationResult ReopenRetainedAuthority()
    {
        var state = TryLoad();
        if (state is null)
        {
            return Failure("state-unresolved", "Durable authority state cannot be loaded; remaining non-writable.");
        }

        if (state.Mode == DirectedAuthorityMode.Released)
        {
            return ReconcileCursor(state, DurableLocalAuthorityRole.Released, state.Transfer);
        }

        if (state.Mode == DirectedAuthorityMode.Authoritative && !state.ClosedWithAuthority
            && TryLoadCursor() is { CurrentRole: DurableLocalAuthorityRole.Authoritative or DurableLocalAuthorityRole.InitialAuthoritative })
        {
            return Success("already-open", "Retained source authority is already reopened.", state);
        }

        if (state.Mode != DirectedAuthorityMode.Authoritative || !state.ClosedWithAuthority)
        {
            return Failure("reopen-forbidden", "Only a closed retained-authority state can be reopened.", state);
        }

        var next = state with
        {
            Revision = state.Revision + 1,
            ClosedWithAuthority = false,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var cursor = TryLoadCursor();
        var role = cursor is { HighWaterHandoffVersion: 0 } ? DurableLocalAuthorityRole.InitialAuthoritative : DurableLocalAuthorityRole.Authoritative;
        var lineage = cursor is { HighWaterHandoffVersion: 0 } ? Guid.Empty.ToString("D") : cursor?.LineageId;
        var generation = cursor is { HighWaterHandoffVersion: 0 } ? 0 : cursor?.Generation;
        return PersistStateThenCursor(next, ToCursor(next, role, lineage, generation, null, cursor?.HighWaterHandoffVersion ?? 0), "reopened", "Retained source authority reopened after restart.");
    }

    public DirectedTransferOperationResult InitializeAuthoritative(
        string deviceId,
        IEnumerable<string> pairedDeviceIds,
        DirectedLocalAuthorityCursor? authorityCursor = null)
    {
        if (File.Exists(stateStore.StatePath))
        {
            var existing = TryLoad();
            return existing is null
                ? Failure("state-unresolved", "Existing durable authority state is invalid; initialization is blocked.")
                : Failure(existing.Mode is DirectedAuthorityMode.RelinquishedBlocked or DirectedAuthorityMode.Released
                    ? "source-blocked"
                    : "already-initialized",
                    "Durable authority state already exists; initialization cannot reset or replace it.", existing);
        }

        var existingCursor = TryLoadCursor();
        if (LocalCursorStore.Exists && existingCursor is null)
        {
            return Failure("local-authority-unresolved", "The local participation/current-authority cursor is malformed; initialization is blocked.");
        }
        if (existingCursor is not null && existingCursor.CurrentRole != DurableLocalAuthorityRole.InitializationPending)
        {
            return Failure("local-authority-unresolved", "This device has prior local participation evidence; missing source state cannot reinitialize authority.");
        }
        if (HasLocalTargetEvidence())
        {
            return Failure("local-authority-unresolved", "Target/history evidence exists in this device directory; missing source state cannot reinitialize authority.");
        }

        var paired = pairedDeviceIds?.ToArray() ?? throw new ArgumentNullException(nameof(pairedDeviceIds));
        var pendingCursor = existingCursor ?? new DurableLocalAuthorityCursorState(
            DurableLocalAuthorityCursorState.CurrentFormatVersion,
            1,
            deviceId,
            Guid.Empty.ToString("D"),
            0,
            0,
            DurableLocalAuthorityRole.InitializationPending,
            null,
            DateTimeOffset.UtcNow);
        if (!string.Equals(pendingCursor.DeviceId, deviceId, StringComparison.Ordinal))
        {
            return Failure("local-authority-unresolved", "The local participation cursor belongs to another device; initialization is blocked.");
        }

        try
        {
            if (existingCursor is null) LocalCursorStore.Save(pendingCursor);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return Failure("initialize-cursor-failed", exception.Message);
        }

        var state = new DurableAuthorityState(
            DurableAuthorityState.CurrentFormatVersion,
            0,
            DirectedAuthorityMode.Authoritative,
            deviceId,
            paired,
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            authorityCursor);
        try
        {
            stateStore.Save(state);
            var initializedCursor = pendingCursor with
            {
                Revision = pendingCursor.Revision + 1,
                CurrentRole = DurableLocalAuthorityRole.InitialAuthoritative,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            LocalCursorStore.Save(initializedCursor);
            return Success("initialized", "Synthetic authority state initialized with a durable local participation cursor.", state);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return Failure("initialize-failed", exception.Message, TryLoad());
        }
    }

    public DirectedTransferOperationResult PrepareTransfer(DirectedTransferIdentity transfer)
    {
        var state = TryLoad();
        if (state is null)
        {
            return Failure("state-unresolved", "Durable authority state cannot be loaded; remaining non-writable.");
        }

        var cursor = TryLoadCursor();
        if (LocalCursorStore.Exists && cursor is null)
        {
            return Failure("local-authority-unresolved", "The local participation/current-authority cursor is malformed; transfer preparation is blocked.", state);
        }
        if (cursor is null)
        {
            return Failure("local-authority-cursor-missing", "The local participation/current-authority cursor is missing; transfer preparation is blocked.", state);
        }
        if (state.Mode == DirectedAuthorityMode.Authoritative
            && cursor.CurrentRole == DurableLocalAuthorityRole.InitializationPending
            && state.Transfer is null
            && !state.ClosedWithAuthority)
        {
            var repaired = cursor with
            {
                Revision = cursor.Revision + 1,
                CurrentRole = DurableLocalAuthorityRole.InitialAuthoritative,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            try { LocalCursorStore.Save(repaired); cursor = repaired; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
            {
                return Failure("local-authority-cursor-failed", exception.Message, state);
            }
        }

        if (!transfer.IsValid || !string.Equals(transfer.SourceDeviceId, state.DeviceId, StringComparison.Ordinal)
            || !state.PairedDeviceIds.Contains(transfer.TargetDeviceId, StringComparer.Ordinal))
        {
            return Failure("invalid-target", "Transfer source/target is invalid or the target is not paired.", state);
        }

        if (state.Mode is DirectedAuthorityMode.RelinquishedBlocked or DirectedAuthorityMode.Released)
        {
            return Failure("source-blocked", "Source authority was durably relinquished and cannot be retargeted or cancelled.", state);
        }

        if (state.Mode != DirectedAuthorityMode.Authoritative && state.Mode != DirectedAuthorityMode.TransferPrepared)
        {
            return Failure("source-not-authoritative", "Only the current authoritative source may begin a normal directed transfer.", state);
        }

        if (state.Mode == DirectedAuthorityMode.Authoritative && state.ClosedWithAuthority)
        {
            return Failure("source-closed", "Retained authority must be explicitly reopened before starting a transfer.", state);
        }

        if (state.Transfer is not null && state.Transfer != transfer)
        {
            return Failure("immutable-transfer", "A different transfer cannot replace the prepared transfer.", state);
        }

        if (state.Mode == DirectedAuthorityMode.Authoritative
            && cursor.CurrentRole == DurableLocalAuthorityRole.InitialAuthoritative
            && (transfer.HandoffVersion != 1 || !cursor.IsVirginLineage))
        {
            return Failure("non-monotonic-transfer", "The initial local authority cursor permits only handoff version 1.", state);
        }

        if (state.Mode == DirectedAuthorityMode.Authoritative
            && cursor.CurrentRole == DurableLocalAuthorityRole.Authoritative
            && (cursor.LineageId != transfer.LineageId
                || cursor.Generation != transfer.Generation
                || transfer.HandoffVersion != cursor.HighWaterHandoffVersion + 1))
        {
            return Failure(
                "non-monotonic-transfer",
                $"The next local transfer must use lineage {cursor.LineageId}, generation {cursor.Generation} and handoff version {cursor.HighWaterHandoffVersion + 1}.",
                state);
        }

        if (state.Transfer == transfer && state.Mode == DirectedAuthorityMode.TransferPrepared)
        {
            if (cursor.CurrentRole == DurableLocalAuthorityRole.TransferPrepared && cursor.Matches(transfer))
            {
                return Success("already-prepared", "The same immutable transfer is already prepared.", state);
            }

            var preparedCursor = ToCursor(state, DurableLocalAuthorityRole.TransferPrepared, transfer.LineageId, transfer.Generation, transfer.TransferId, cursor.HighWaterHandoffVersion);
            try
            {
                LocalCursorStore.Save(preparedCursor);
                return Success("already-prepared", "The same immutable transfer is already prepared.", state);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
            {
                return Failure("local-authority-cursor-failed", exception.Message, state);
            }
        }

        var next = state with
        {
            Revision = state.Revision + 1,
            Mode = DirectedAuthorityMode.TransferPrepared,
            Transfer = transfer,
            ClosedWithAuthority = false,
            SnapshotEvidence = null,
            MarkerEvidence = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var nextCursor = ToCursor(
            next,
            DurableLocalAuthorityRole.TransferPrepared,
            transfer.LineageId,
            transfer.Generation,
            transfer.TransferId,
            cursor.HighWaterHandoffVersion);
        return PersistStateThenCursor(next, nextCursor, "prepared", "Directed transfer prepared; source remains authoritative until durable relinquishment.");
    }

    public DirectedTransferOperationResult RetainClose()
    {
        var state = TryLoad();
        if (state is null)
        {
            return Failure("state-unresolved", "Durable authority state cannot be loaded; remaining non-writable.");
        }

        if (state.Mode is DirectedAuthorityMode.RelinquishedBlocked or DirectedAuthorityMode.Released)
        {
            return Failure("source-blocked", "Retain-close is unavailable after durable relinquishment.", state);
        }

        if (state.Mode is not (DirectedAuthorityMode.Authoritative or DirectedAuthorityMode.TransferPrepared))
        {
            return Failure("source-not-authoritative", "Only an authoritative source can retain-close.", state);
        }

        // Retain-close before the irreversible relinquishment point safely
        // cancels any prepared transfer and leaves a clean reopenable state.
        var next = state with
        {
            Revision = state.Revision + 1,
            Mode = DirectedAuthorityMode.Authoritative,
            Transfer = null,
            SnapshotEvidence = null,
            MarkerEvidence = null,
            ClosedWithAuthority = true,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var cursor = TryLoadCursor();
        if (cursor is null)
        {
            return Failure("local-authority-cursor-missing", "The local participation/current-authority cursor is missing; retain-close is blocked.", state);
        }
        var retainedCursor = ToCursor(
            next,
            DurableLocalAuthorityRole.ClosedWithRetainedAuthority,
            cursor.LineageId,
            cursor.Generation,
            null,
            cursor.HighWaterHandoffVersion);
        return PersistStateThenCursor(next, retainedCursor, "retained", "Close retained source authority; any uncommitted transfer was safely cancelled and no marker was published.");
    }

    public DirectedTransferOperationResult AbortBeforeRelinquishment()
    {
        var state = TryLoad();
        if (state is null)
        {
            return Failure("state-unresolved", "Durable authority state cannot be loaded; remaining non-writable.");
        }

        if (state.Mode is DirectedAuthorityMode.RelinquishedBlocked or DirectedAuthorityMode.Released)
        {
            return Failure("abort-forbidden", "A durably relinquished source cannot rollback, retarget or cancel.", state);
        }

        var next = state with
        {
            Revision = state.Revision + 1,
            Mode = DirectedAuthorityMode.Authoritative,
            Transfer = null,
            ClosedWithAuthority = false,
            SnapshotEvidence = null,
            MarkerEvidence = null,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var cursor = TryLoadCursor();
        if (cursor is null)
        {
            return Failure("local-authority-cursor-missing", "The local participation/current-authority cursor is missing; abort is blocked.", state);
        }
        var role = cursor.HighWaterHandoffVersion == 0
            ? DurableLocalAuthorityRole.InitialAuthoritative
            : DurableLocalAuthorityRole.Authoritative;
        var abortedLineage = cursor.HighWaterHandoffVersion == 0 ? Guid.Empty.ToString("D") : cursor.LineageId;
        var abortedGeneration = cursor.HighWaterHandoffVersion == 0 ? 0 : cursor.Generation;
        var abortedCursor = ToCursor(next, role, abortedLineage, abortedGeneration, null, cursor.HighWaterHandoffVersion);
        return PersistStateThenCursor(next, abortedCursor, "aborted", "Transfer safely aborted before durable relinquishment; source remains authoritative.");
    }

    public async Task<DirectedTransferOperationResult> DurablyRelinquishAsync(
        DirectedSnapshotEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        var state = TryLoad();
        if (state is null)
        {
            return Failure("state-unresolved", "Durable authority state cannot be loaded; remaining non-writable.");
        }

        if (state.Mode == DirectedAuthorityMode.Released)
        {
            return ReconcileCursor(state, DurableLocalAuthorityRole.Released, state.Transfer);
        }

        if (state.Mode == DirectedAuthorityMode.RelinquishedBlocked)
        {
            return ReconcileCursor(state, DurableLocalAuthorityRole.RelinquishedBlocked, state.Transfer);
        }

        DirectedSnapshotEvidence? verifiedEvidence = null;
        if (evidence is not null)
        {
            try
            {
                verifiedEvidence = await DirectedSnapshotEvidence.CaptureAsync(
                    evidence.Transfer,
                    evidence.SnapshotPath,
                    evidence.SyncConfirmed,
                    cancellationToken);
            }
            catch (IOException)
            {
                verifiedEvidence = null;
            }
        }

        if (evidence is null
            || !evidence.IsValid
            || verifiedEvidence is null
            || !verifiedEvidence.IsValid
            || evidence != verifiedEvidence
            || state.Transfer is null
            || evidence.Transfer != state.Transfer)
        {
            return Failure("invalid-snapshot-evidence", "Relinquishment requires a valid same-transfer SQLite integrity, checksum, length and synchronized-state evidence.", state);
        }

        if (state.Mode != DirectedAuthorityMode.TransferPrepared || state.Transfer is not { IsValid: true })
        {
            return Failure("transfer-not-prepared", "A valid directed transfer must be prepared before relinquishment.", state);
        }

        var next = state with
        {
            Revision = state.Revision + 1,
            Mode = DirectedAuthorityMode.RelinquishedBlocked,
            ClosedWithAuthority = false,
            AuthorityCursor = new DirectedLocalAuthorityCursor(
                verifiedEvidence.Transfer.LineageId,
                verifiedEvidence.Transfer.Generation,
                verifiedEvidence.Transfer.HandoffVersion),
            SnapshotEvidence = verifiedEvidence,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        // Save is the durable relinquishment boundary. If it fails, the prior authoritative state remains.
        await Task.CompletedTask.WaitAsync(cancellationToken);
        var relinquishedCursor = ToCursor(
            next,
            DurableLocalAuthorityRole.RelinquishedBlocked,
            verifiedEvidence.Transfer.LineageId,
            verifiedEvidence.Transfer.Generation,
            verifiedEvidence.Transfer.TransferId,
            verifiedEvidence.Transfer.HandoffVersion);
        return PersistStateThenCursor(next, relinquishedCursor, "relinquished", "Source authority is durably relinquished and permanently blocked.");
    }

    public async Task<DirectedTransferOperationResult> PublishReleaseMarkersAsync(
        string handoffDirectory,
        string snapshotPath,
        CancellationToken cancellationToken = default)
    {
        var state = TryLoad();
        if (state is null)
        {
            return Failure("state-unresolved", "Durable authority state cannot be loaded; markers are not published.");
        }

        if (state.Mode == DirectedAuthorityMode.Released)
        {
            return ReconcileCursor(state, DurableLocalAuthorityRole.Released, state.Transfer);
        }

        if (state.Mode != DirectedAuthorityMode.RelinquishedBlocked || state.Transfer is not { IsValid: true } transfer)
        {
            return Failure("relinquishment-required", "Ready and grant markers require successful durable relinquishment.", state);
        }

        try
        {
            var currentEvidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshotPath, syncConfirmed: true, cancellationToken);
            if (!currentEvidence.IsValid || state.SnapshotEvidence is not { IsValid: true } snapshotEvidence || currentEvidence != snapshotEvidence)
            {
                return Failure("snapshot-evidence-mismatch", "The retry snapshot does not match the durably recorded immutable snapshot evidence.", state);
            }

            failureInjector?.OnFailurePoint(DirectedTransferFailurePoint.BeforeMarkerPublication);
            var result = await DirectedTransferMarkerPublisher.PublishAsync(transfer, handoffDirectory, snapshotPath, failureInjector, cancellationToken);
            if (!result.Succeeded)
            {
                return result with { State = state };
            }

            var baseName = "directed-" + transfer.TransferId;
            var readyPath = Path.Combine(handoffDirectory, baseName + ".ready.json");
            var grantPath = Path.Combine(handoffDirectory, baseName + ".grant.json");
            failureInjector?.OnFailurePoint(DirectedTransferFailurePoint.BeforeMarkerSynchronization);
            var readySync = syncObserver.Observe(readyPath);
            var grantSync = syncObserver.Observe(grantPath);
            if (!readySync.IsConfirmedInSync || !grantSync.IsConfirmedInSync)
            {
                return Failure("marker-not-synchronized", $"Target-bound marker synchronization is incomplete (ready={readySync.Status}, grant={grantSync.Status}).", state);
            }

            failureInjector?.OnFailurePoint(DirectedTransferFailurePoint.BeforeReleasedCommit);
            var markerEvidence = new DirectedMarkerEvidence(
                readyPath,
                grantPath,
                snapshotEvidence.SnapshotChecksum,
                snapshotEvidence.SnapshotByteLength,
                readySync.IsConfirmedInSync,
                grantSync.IsConfirmedInSync);
            var released = state with
            {
                Revision = state.Revision + 1,
                Mode = DirectedAuthorityMode.Released,
                MarkerEvidence = markerEvidence,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            var releasedCursor = ToCursor(
                released,
                DurableLocalAuthorityRole.Released,
                transfer.LineageId,
                transfer.Generation,
                transfer.TransferId,
                transfer.HandoffVersion);
            return PersistStateThenCursor(released, releasedCursor, "released", "Target-bound markers are synchronized and source transfer completion is durably recorded.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return Failure("marker-publication-failed", exception.Message, state);
        }
    }

    private DurableAuthorityState? TryLoad()
    {
        try { return stateStore.Load(); }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
    }

    private DirectedTransferOperationResult PersistStateThenCursor(
        DurableAuthorityState next,
        DurableLocalAuthorityCursorState nextCursor,
        string code,
        string message)
    {
        try
        {
            stateStore.Save(next);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return Failure("durable-write-failed", exception.Message, TryLoad());
        }

        try
        {
            LocalCursorStore.Save(nextCursor);
            return Success(code, message, next);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return Failure("local-authority-cursor-failed", exception.Message, next);
        }
    }

    private DirectedTransferOperationResult ReconcileCursor(
        DurableAuthorityState state,
        DurableLocalAuthorityRole role,
        DirectedTransferIdentity? transfer)
    {
        var cursor = TryLoadCursor();
        if (cursor is null)
        {
            return Failure("local-authority-cursor-missing", "The local participation/current-authority cursor is missing; durable source state remains blocked.", state);
        }
        if (!string.Equals(cursor.DeviceId, state.DeviceId, StringComparison.Ordinal))
        {
            return Failure("local-authority-cursor-inconsistent", "The local participation cursor belongs to another device.", state);
        }
        if (transfer is null || !transfer.IsValid)
        {
            return Failure("local-authority-cursor-inconsistent", "Durable source state has no valid transfer for cursor recovery.", state);
        }

        var expected = ToCursor(state, role, transfer.LineageId, transfer.Generation, transfer.TransferId, transfer.HandoffVersion);
        if (cursor == expected || cursor.Matches(transfer) && cursor.CurrentRole == role && cursor.HighWaterHandoffVersion == transfer.HandoffVersion)
        {
            return Success(role == DurableLocalAuthorityRole.Released ? "already-released" : "already-relinquished", "The same durable source transition is already recorded; source remains blocked.", state);
        }

        var recoverable = role switch
        {
            DurableLocalAuthorityRole.RelinquishedBlocked => cursor.CurrentRole is DurableLocalAuthorityRole.TransferPrepared or DurableLocalAuthorityRole.RelinquishedBlocked,
            DurableLocalAuthorityRole.Released => cursor.CurrentRole is DurableLocalAuthorityRole.RelinquishedBlocked or DurableLocalAuthorityRole.Released,
            _ => false
        };
        if (!recoverable)
        {
            return Failure("local-authority-cursor-inconsistent", "The local participation cursor cannot be reconciled with durable source state.", state);
        }

        try
        {
            LocalCursorStore.Save(expected with { Revision = cursor.Revision + 1 });
            return Success(role == DurableLocalAuthorityRole.Released ? "already-released" : "already-relinquished", "Durable source state was reconciled with its local participation cursor; source remains blocked.", state);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return Failure("local-authority-cursor-failed", exception.Message, state);
        }
    }

    private DurableLocalAuthorityCursorState? TryLoadCursor()
    {
        if (!LocalCursorStore.Exists) return null;
        try { return LocalCursorStore.Load(); }
        catch (InvalidDataException) { return null; }
        catch (IOException) { return null; }
    }

    private bool HasLocalTargetEvidence()
    {
        var directory = Path.GetDirectoryName(stateStore.StatePath);
        return directory is not null
            && Directory.Exists(directory)
            && Directory.EnumerateFiles(directory, "target*.json", SearchOption.TopDirectoryOnly).Any();
    }

    private DurableLocalAuthorityCursorState ToCursor(
        DurableAuthorityState state,
        DurableLocalAuthorityRole role,
        string? lineageId,
        long? generation,
        string? transferId,
        long highWater)
    {
        var cursor = TryLoadCursor();
        return new DurableLocalAuthorityCursorState(
            DurableLocalAuthorityCursorState.CurrentFormatVersion,
            (cursor?.Revision ?? 0) + 1,
            state.DeviceId,
            lineageId ?? Guid.Empty.ToString("D"),
            generation ?? 0,
            highWater,
            role,
            transferId,
            DateTimeOffset.UtcNow);
    }

    private static DirectedTransferOperationResult Success(string code, string message, DurableAuthorityState state) => new(true, code, message, state);
    private static DirectedTransferOperationResult Failure(string code, string message, DurableAuthorityState? state = null) => new(false, code, message, state);
}
