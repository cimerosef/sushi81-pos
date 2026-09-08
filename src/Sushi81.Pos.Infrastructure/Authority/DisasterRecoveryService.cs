using System.Security.Cryptography;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.Authority;

public sealed record DisasterRecoveryResult(
    bool Succeeded,
    bool RemainsReadOnly,
    string Diagnostic,
    RecoveryCandidate? Candidate = null)
{
    public static DisasterRecoveryResult Failure(string diagnostic, RecoveryCandidate? candidate = null) =>
        new(false, true, diagnostic, candidate);

    public static DisasterRecoveryResult Success(RecoveryCandidate candidate) =>
        new(true, false, "Disaster Recovery completed and this device is authoritative.", candidate);
}

/// <summary>
/// Main-owned DR state machine. It is the only desktop service allowed to advance recovery
/// through the production activation primitive; shell commands only request operations here.
/// </summary>
public sealed class DisasterRecoveryService(
    IAuthorityStateStore authorityStore,
    WriteAuthorityGuard guard,
    ISystemMetadataStore systemMetadata,
    IRecoveryCandidateDiscovery candidates,
    RecoveryActivationService? activation,
    IGitHubHandoffTransport? githubTransport,
    ILocalRecoverySnapshotService localSnapshots,
    IAppPaths paths,
    IBusinessClock clock) : IAsyncDisposable
{
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly DisasterRecoveryDatabaseInstaller installer = new(paths);

    public async Task<RecoveryCandidateDiscoveryResult> DiscoverCandidatesAsync(CancellationToken cancellationToken = default)
    {
        var state = await LoadProtocolAsync(cancellationToken);
        return await candidates.DiscoverAsync(state, cancellationToken);
    }

    public async Task<DisasterRecoveryEntryContext> GetEntryContextAsync(
        CancellationToken cancellationToken = default)
    {
        var state = await LoadProtocolAsync(cancellationToken);
        return CreateEntryContext(state);
    }

    public async Task<DisasterRecoveryResult> StartOrResumeAsync(
        string? selectedCandidateId,
        bool normalPathUnavailableConfirmed,
        bool quarantineConfirmed,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        AuthorityProtocolState? loadedState = null;
        try
        {
            var current = await LoadProtocolAsync(cancellationToken);
            loadedState = current;
            var resuming = current.Phase is AuthorityPhase.DisasterRecoveryPreparing or AuthorityPhase.DisasterRecoveryPending;
            if (resuming)
            {
                if (!quarantineConfirmed)
                    return DisasterRecoveryResult.Failure("Explicit old-device quarantine confirmation is required before Disaster Recovery.");
                if (current.Recovery is not { } existing)
                    return DisasterRecoveryResult.Failure("The durable Disaster Recovery state is incomplete.");
                selectedCandidateId = existing.CandidateId;
            }
            else
            {
                var context = CreateEntryContext(current);
                if (!context.IsEligibleForNewRecovery)
                    return DisasterRecoveryResult.Failure($"Disaster Recovery is unavailable during authority phase {current.Phase}.");
                if (!normalPathUnavailableConfirmed)
                    return DisasterRecoveryResult.Failure("An explicit normal-path-unavailable confirmation is required before Disaster Recovery.");
                if (!quarantineConfirmed)
                    return DisasterRecoveryResult.Failure("Explicit old-device quarantine confirmation is required before Disaster Recovery.");

                // Candidate freshness is a protocol invariant, not a presentation choice.
                // Rediscover immediately before durable Preparing and allow only the single
                // deterministic recommendation for this new recovery identity.
                var latest = await candidates.DiscoverAsync(current, cancellationToken);
                if (latest.Recommended is null || !latest.Contains(selectedCandidateId ?? string.Empty))
                    return DisasterRecoveryResult.Failure("No deterministic freshest validated recovery candidate is available.");
                if (!string.Equals(latest.Recommended.CandidateId, selectedCandidateId, StringComparison.Ordinal))
                    return DisasterRecoveryResult.Failure("The selected recovery candidate is not the deterministic freshest validated candidate.");
            }

            if (string.IsNullOrWhiteSpace(selectedCandidateId))
                return DisasterRecoveryResult.Failure("A validated recovery candidate must be selected.");

            var candidate = await candidates.FindExactAsync(current, selectedCandidateId, cancellationToken);
            if (candidate is null)
                return DisasterRecoveryResult.Failure("The selected recovery candidate is no longer available or valid.");
            if (candidate.LineageId != current.LineageId || candidate.Generation != current.Generation)
                return DisasterRecoveryResult.Failure("The selected recovery candidate is not from the current pre-recovery generation.", candidate);

            RecoveryActivationEvidence recovery;
            if (resuming)
            {
                recovery = current.Recovery!;
                if (!Matches(recovery, candidate))
                    return DisasterRecoveryResult.Failure("The exact persisted recovery candidate is no longer the selected evidence.", candidate);
            }
            else
            {
                if (activation is null || githubTransport is null)
                    return DisasterRecoveryResult.Failure("Online Disaster Recovery activation is not configured.", candidate);
                if (current.LineageId is not { } lineageId || current.Generation < 1)
                    return DisasterRecoveryResult.Failure("The local authority state has no valid recovery lineage.", candidate);

                recovery = new RecoveryActivationEvidence(
                    Guid.NewGuid(), current.DeviceId, lineageId, current.Generation, checked(current.Generation + 1),
                    candidate.CandidateId, candidate.PayloadSha256, candidate.BusinessRevision,
                    CandidateType: candidate.TypeName,
                    CandidateReference: candidate.StorageReference,
                    CandidateHandoffVersion: candidate.HandoffVersion,
                    CandidateCreatedAtUtc: candidate.CreatedAtUtc,
                    PriorTransfer: CreatePriorTransferEvidence(current));
                var preparing = current with
                {
                    Revision = checked(current.Revision + 1),
                    Phase = AuthorityPhase.DisasterRecoveryPreparing,
                    Recovery = recovery,
                    Transfer = null
                };
                guard.SetState(WriteAuthorityState.Transitioning);
                try { await PersistAsync(preparing, cancellationToken); }
                catch { guard.SetState(WriteAuthorityState.RecoveryRequired); throw; }
                current = preparing;
                loadedState = current;
            }

            if (activation is null)
                return DisasterRecoveryResult.Failure("Online Disaster Recovery activation is unavailable.", candidate);

            if (current.Phase == AuthorityPhase.DisasterRecoveryPreparing)
            {
                var request = new RecoveryActivationRequest(
                    recovery.RecoveryId, recovery.LineageId, recovery.PriorGeneration, recovery.NextGeneration,
                    recovery.DeviceId, recovery.CandidateId, recovery.CandidateSha256, recovery.BusinessRevision);
                var activationResult = await activation.TryAcquireAsync(request, cancellationToken);
                if (activationResult.Outcome == RecoveryActivationOutcome.BlockedNoWinner
                    && await TryCleanExactStarterAsync(request, cancellationToken))
                {
                    activationResult = await activation.TryAcquireAsync(request, cancellationToken);
                }
                if (activationResult.Outcome == RecoveryActivationOutcome.LostToExistingWinner)
                {
                    await FenceIfNewerGenerationAsync(current, recovery, cancellationToken);
                    return DisasterRecoveryResult.Failure("Another recovery device owns the next-generation activation; this device remains read-only.", candidate);
                }
                if (!activationResult.Accepted || activationResult.Receipt is null)
                    return DisasterRecoveryResult.Failure("Online activation did not produce a proven winner; this device remains read-only.", candidate);

                recovery = recovery with { Receipt = activationResult.Receipt };
                current = current with
                {
                    Revision = checked(current.Revision + 1),
                    Phase = AuthorityPhase.DisasterRecoveryPending,
                    Recovery = recovery
                };
                await PersistAsync(current, cancellationToken);
                loadedState = current;
            }

            return await CompletePendingAsync(current, recovery, candidate, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            guard.SetState(exception is InvalidDataException
                ? WriteAuthorityState.RecoveryRequired
                : loadedState?.WriteState ?? WriteAuthorityState.RecoveryRequired);
            return DisasterRecoveryResult.Failure("Disaster Recovery could not complete; this device remains read-only.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public Task<DisasterRecoveryResult> RetryAsync(
        bool quarantineConfirmed,
        CancellationToken cancellationToken = default) =>
        StartOrResumeAsync(null, normalPathUnavailableConfirmed: false, quarantineConfirmed, cancellationToken);

    public async Task<DisasterRecoveryResult> ReinitializeStaleDeviceAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var current = await LoadProtocolAsync(cancellationToken);
            if (current.Phase != AuthorityPhase.StaleGeneration || current.LineageId is not { } lineageId)
                return DisasterRecoveryResult.Failure("This device is not in stale-generation reinitialization state.");
            var lineage = await systemMetadata.ReadLineageAsync(cancellationToken);
            if (lineage.LineageId != lineageId || lineage.CurrentGeneration <= current.Generation)
                return DisasterRecoveryResult.Failure("A readable newer same-lineage System generation is required.");
            var seed = await systemMetadata.FindValidatedReadOnlySeedAsync(lineageId, lineage.CurrentGeneration, cancellationToken);
            if (seed is null)
                return DisasterRecoveryResult.Failure("No validated current-generation read-only seed is available.");

            var candidate = new RecoveryCandidate(
                RecoveryCandidateType.OneDriveCheckpoint,
                $"system-seed:{seed.Metadata.SeedId:N}:h{seed.Metadata.HandoffVersion}",
                seed.Metadata.LineageId,
                seed.Metadata.Generation,
                seed.Metadata.SourceDeviceId,
                seed.Metadata.BusinessRevision,
                seed.Metadata.HandoffVersion,
                seed.Metadata.CreatedAtUtc,
                seed.Metadata.PayloadSize,
                seed.Metadata.PayloadSha256,
                seed.PayloadPath);
            candidate.Validate();
            await PreserveLocalDatabaseAsync(current.BusinessRevision, cancellationToken);
            var staged = await installer.StageLocalAndValidateAsync(candidate, cancellationToken);
            await installer.InstallAndVerifyAsync(staged, candidate, cancellationToken);
            await systemMetadata.JoinCurrentGenerationAsync(current.DeviceId, current.DisplayName, cancellationToken);
            var reinitialized = current with
            {
                Revision = checked(current.Revision + 1),
                Generation = lineage.CurrentGeneration,
                BusinessRevision = candidate.BusinessRevision,
                HandoffVersion = Math.Max(current.HandoffVersion, candidate.HandoffVersion),
                Phase = AuthorityPhase.NonAuthoritativeReadOnly,
                Transfer = null,
                Recovery = null
            };
            await PersistAsync(reinitialized, cancellationToken);
            guard.SetState(WriteAuthorityState.NonAuthoritativeReadOnly);
            return new(true, true, "This device was reinitialized into the current generation and remains read-only.", candidate);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            guard.SetState(WriteAuthorityState.NonAuthoritativeReadOnly);
            return DisasterRecoveryResult.Failure("Stale-generation reinitialization could not complete; this device remains read-only.");
        }
        finally
        {
            operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        operationGate.Dispose();
        await ValueTask.CompletedTask;
    }

    private async Task<DisasterRecoveryResult> CompletePendingAsync(
        AuthorityProtocolState current,
        RecoveryActivationEvidence recovery,
        RecoveryCandidate candidate,
        CancellationToken cancellationToken)
    {
        var exact = await candidates.FindExactAsync(current, recovery.CandidateId, cancellationToken);
        if (exact is null || !Matches(recovery, exact))
            return DisasterRecoveryResult.Failure("The exact persisted recovery candidate could not be revalidated.", candidate);

        await PreserveLocalDatabaseAsync(current.BusinessRevision, cancellationToken);
        var staged = await StageCandidateAsync(exact, cancellationToken);
        await installer.InstallAndVerifyAsync(staged, exact, cancellationToken);
        await systemMetadata.AdvanceGenerationAsync(recovery.LineageId, recovery.PriorGeneration, recovery.NextGeneration, cancellationToken);
        await systemMetadata.JoinCurrentGenerationAsync(current.DeviceId, current.DisplayName, cancellationToken);

        var authoritative = current with
        {
            Revision = checked(current.Revision + 1),
            LineageId = recovery.LineageId,
            Generation = recovery.NextGeneration,
            HandoffVersion = Math.Max(current.HandoffVersion, exact.HandoffVersion),
            BusinessRevision = exact.BusinessRevision,
            Phase = AuthorityPhase.Authoritative,
            Transfer = null,
            Recovery = null,
            LastRecovery = recovery
        };
        await PersistAsync(authoritative, cancellationToken);
        await TryPublishCurrentGenerationSeedAsync(authoritative, cancellationToken);
        return DisasterRecoveryResult.Success(exact);
    }

    private async Task<string> StageCandidateAsync(RecoveryCandidate candidate, CancellationToken cancellationToken)
    {
        if (candidate.Type == RecoveryCandidateType.OneDriveCheckpoint)
            return await installer.StageLocalAndValidateAsync(candidate, cancellationToken);
        if (githubTransport is null || candidate.SnapshotAssetId is not { } snapshotAssetId)
            throw new InvalidOperationException("The GitHub recovery candidate transport is unavailable.");
        await using var content = await githubTransport.DownloadAssetAsync(snapshotAssetId, cancellationToken);
        return await installer.StageAndValidateAsync(candidate, content, cancellationToken);
    }

    private async Task<bool> TryCleanExactStarterAsync(
        RecoveryActivationRequest request,
        CancellationToken cancellationToken)
    {
        if (githubTransport is null) return false;
        try
        {
            var release = await githubTransport.EnsureContainerAsync(createIfMissing: false, cancellationToken);
            var matches = (await githubTransport.ListAssetsAsync(release, cancellationToken))
                .Where(asset => string.Equals(asset.Name, request.AssetName, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1 || matches[0].IsComplete) return false;
            var exact = await githubTransport.GetAssetAsync(matches[0].Id, cancellationToken);
            if (exact.Id != matches[0].Id || exact.IsComplete || !string.Equals(exact.Name, request.AssetName, StringComparison.Ordinal))
                return false;
            await githubTransport.DeleteAssetAsync(exact.Id, cancellationToken);
            var remaining = (await githubTransport.ListAssetsAsync(release, cancellationToken))
                .Where(asset => string.Equals(asset.Name, request.AssetName, StringComparison.Ordinal))
                .ToArray();
            return remaining.Length == 0;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    private async Task PreserveLocalDatabaseAsync(long businessRevision, CancellationToken cancellationToken)
    {
        paths.EnsureInitialized();
        if (!File.Exists(paths.LiveDatabasePath)) return;
        await localSnapshots.CreateAsync(new DurableChange(Math.Max(0, businessRevision), clock.UtcNow), cancellationToken);
    }

    private async Task FenceIfNewerGenerationAsync(
        AuthorityProtocolState current,
        RecoveryActivationEvidence recovery,
        CancellationToken cancellationToken)
    {
        try
        {
            var lineage = await systemMetadata.ReadLineageAsync(cancellationToken);
            if (lineage.LineageId == recovery.LineageId && lineage.CurrentGeneration > current.Generation)
            {
                var stale = current with
                {
                    Revision = checked(current.Revision + 1),
                    Phase = AuthorityPhase.StaleGeneration,
                    Transfer = null,
                    Recovery = null,
                    LastRecovery = recovery
                };
                await PersistAsync(stale, cancellationToken);
            }
        }
        catch (SystemMetadataUnavailableException) { }
    }

    /// <summary>
    /// Reconstructible non-authority retry for the current authoritative data state. A failed
    /// publication is deliberately forgotten locally; a later authoritative refresh calls this
    /// method again and recognizes an already-valid equivalent seed before publishing.
    /// </summary>
    public async Task<bool> TryPublishCurrentGenerationSeedAsync(
        AuthorityProtocolState? state = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            state ??= await LoadProtocolAsync(cancellationToken);
            if (state.Phase is not (AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority)
                || state.LineageId is not { } lineageId || state.Generation < 1)
                return false;
            paths.EnsureInitialized();
            var payload = await File.ReadAllBytesAsync(paths.LiveDatabasePath, cancellationToken);
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
            var existing = await systemMetadata.FindValidatedReadOnlySeedAsync(lineageId, state.Generation, cancellationToken);
            if (existing is not null
                && existing.Metadata.BusinessRevision == state.BusinessRevision
                && existing.Metadata.HandoffVersion == state.HandoffVersion
                && existing.Metadata.PayloadSize == payload.LongLength
                && string.Equals(existing.Metadata.PayloadSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
                return true;

            var seedId = Guid.NewGuid();
            var metadata = new ReadOnlySeedMetadata(
                1, "M07", SystemMetadataContract.ReadOnlySeedArtifactKind, seedId,
                lineageId, state.Generation, state.DeviceId, state.BusinessRevision,
                SystemMetadataContract.SeedPayloadFileName(seedId), payload.LongLength,
                payloadHash, clock.UtcNow, state.HandoffVersion);
            await systemMetadata.PublishReadOnlySeedAsync(metadata, payload, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }

    private async Task<AuthorityProtocolState> LoadProtocolAsync(CancellationToken cancellationToken)
    {
        var document = await authorityStore.LoadAsync(cancellationToken)
            ?? throw new InvalidDataException("No canonical authority state is available.");
        var state = document.Protocol ?? throw new InvalidDataException("Canonical authority metadata is unavailable.");
        state.Validate();
        return state;
    }

    private async Task PersistAsync(AuthorityProtocolState state, CancellationToken cancellationToken)
    {
        state.Validate();
        await authorityStore.SaveAsync(new AuthorityStateDocument(2, state.WriteState, clock.UtcNow) { Protocol = state }, cancellationToken);
        guard.SetState(state.WriteState);
    }

    private static DisasterRecoveryEntryContext CreateEntryContext(AuthorityProtocolState state)
    {
        var transfer = state.Transfer;
        var reason = state.Phase switch
        {
            AuthorityPhase.PairedUninitializedReadOnly => DisasterRecoveryEntryReason.PairedReplacement,
            AuthorityPhase.NonAuthoritativeReadOnly => DisasterRecoveryEntryReason.NormalAuthorityUnavailable,
            AuthorityPhase.ReleasedNonAuthoritative => DisasterRecoveryEntryReason.ReleasedTargetUnavailable,
            AuthorityPhase.RelinquishedPendingGrant => DisasterRecoveryEntryReason.RelinquishedTransferTargetUnavailable,
            _ => DisasterRecoveryEntryReason.NormalAuthorityUnavailable
        };
        return new DisasterRecoveryEntryContext(
            state.Phase,
            state.LineageId,
            state.Generation,
            state.DeviceId,
            transfer?.SourceDeviceId,
            transfer?.TargetDeviceId,
            transfer?.TransferId,
            transfer?.Version,
            reason,
            NormalPathUnavailableConfirmationRequired: true,
            state.Recovery?.RecoveryId,
            state.Recovery?.CandidateId,
            state.Recovery?.CandidateType,
            state.Recovery?.BusinessRevision,
            state.Recovery?.CandidateHandoffVersion);
    }

    private static RecoveryPriorTransferEvidence? CreatePriorTransferEvidence(AuthorityProtocolState state) =>
        state.Phase is AuthorityPhase.RelinquishedPendingGrant or AuthorityPhase.ReleasedNonAuthoritative
            && state.Transfer is { } transfer
            && transfer.SnapshotReceipt is not null
            ? new RecoveryPriorTransferEvidence(
                state.Phase,
                transfer.TransferId,
                transfer.LineageId,
                transfer.Generation,
                transfer.Version,
                transfer.SourceDeviceId,
                transfer.TargetDeviceId,
                transfer.BusinessRevision,
                transfer.SnapshotReceipt,
                transfer.GrantReceipt)
            : null;

    private static bool Matches(RecoveryActivationEvidence evidence, RecoveryCandidate candidate) =>
        string.Equals(evidence.CandidateId, candidate.CandidateId, StringComparison.Ordinal)
        && string.Equals(evidence.CandidateSha256, candidate.PayloadSha256, StringComparison.OrdinalIgnoreCase)
        && evidence.BusinessRevision == candidate.BusinessRevision
        && string.Equals(evidence.CandidateType, candidate.TypeName, StringComparison.Ordinal)
        && string.Equals(evidence.CandidateReference, candidate.StorageReference, StringComparison.Ordinal)
        && evidence.CandidateHandoffVersion == candidate.HandoffVersion;
}
