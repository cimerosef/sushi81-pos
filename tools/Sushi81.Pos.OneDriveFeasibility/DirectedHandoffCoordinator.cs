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
    DirectedLifecycleLedgerStore? lifecycleLedger = null)
{
    private readonly IArtifactSyncObserver syncObserver = syncObserver ?? new DeterministicArtifactSyncObserver();
    public DirectedLifecycleLedgerStore? LifecycleLedger { get; } = lifecycleLedger;

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
            return new DirectedLifecycleAuthorityGate(deviceId)
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
            return Success("already-released", "The same directed transfer is already durably released.", state);
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
        return Persist(next, "reopened", "Retained source authority reopened after restart.");
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

        var paired = pairedDeviceIds?.ToArray() ?? throw new ArgumentNullException(nameof(pairedDeviceIds));
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
            return Success("initialized", "Synthetic authority state initialized.", state);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException)
        {
            return Failure("initialize-failed", exception.Message);
        }
    }

    public DirectedTransferOperationResult PrepareTransfer(DirectedTransferIdentity transfer)
    {
        var state = TryLoad();
        if (state is null)
        {
            return Failure("state-unresolved", "Durable authority state cannot be loaded; remaining non-writable.");
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

        if (state.AuthorityCursor is { } cursor
            && (cursor.LineageId != transfer.LineageId
                || cursor.Generation != transfer.Generation
                || transfer.HandoffVersion != cursor.HandoffVersion + 1))
        {
            return Failure(
                "non-monotonic-transfer",
                $"The next local transfer must use lineage {cursor.LineageId}, generation {cursor.Generation} and handoff version {cursor.HandoffVersion + 1}.",
                state);
        }

        if (state.Transfer == transfer && state.Mode == DirectedAuthorityMode.TransferPrepared)
        {
            return Success("already-prepared", "The same immutable transfer is already prepared.", state);
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
        return Persist(next, "prepared", "Directed transfer prepared; source remains authoritative until durable relinquishment.");
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
        return Persist(next, "retained", "Close retained source authority; any uncommitted transfer was safely cancelled and no marker was published.");
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
        return Persist(next, "aborted", "Transfer safely aborted before durable relinquishment; source remains authoritative.");
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
            return Success("already-released", "The same directed transfer is already durably released; source remains blocked.", state);
        }

        if (state.Mode == DirectedAuthorityMode.RelinquishedBlocked)
        {
            return Success("already-relinquished", "The same durable relinquishment is already recorded; source remains blocked.", state);
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
        return Persist(next, "relinquished", "Source authority is durably relinquished and permanently blocked.");
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
            return Success("already-released", "The same directed transfer is already durably released; source remains blocked.", state);
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
            return Persist(released, "released", "Target-bound markers are synchronized and source transfer completion is durably recorded.");
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

    private DirectedTransferOperationResult Persist(DurableAuthorityState next, string code, string message)
    {
        try
        {
            stateStore.Save(next);
            return Success(code, message, next);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return Failure("durable-write-failed", exception.Message, TryLoad());
        }
    }

    private static DirectedTransferOperationResult Success(string code, string message, DurableAuthorityState state) => new(true, code, message, state);
    private static DirectedTransferOperationResult Failure(string code, string message, DurableAuthorityState? state = null) => new(false, code, message, state);
}
