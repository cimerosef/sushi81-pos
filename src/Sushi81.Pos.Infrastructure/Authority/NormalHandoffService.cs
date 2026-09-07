using System.Security.Cryptography;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.Authority;

public sealed record NormalHandoffResult(Guid TransferId, bool Succeeded, Exception? Error)
{
    public static NormalHandoffResult Success(Guid transferId) => new(transferId, true, null);

    public static NormalHandoffResult Failure(Guid transferId, Exception error) => new(transferId, false, error);
}

/// <summary>
/// Main-owned source-side normal handoff state machine. Remote transport is deliberately a
/// receipt-only dependency: it never changes the local write guard or canonical authority state.
/// </summary>
public sealed class NormalHandoffService(
    IAuthorityStateStore authorityStore,
    WriteAuthorityGuard guard,
    ISystemMetadataStore systemMetadata,
    ITransferSnapshotFactory snapshots,
    IGitHubHandoffTransport transport,
    IBusinessClock clock,
    IBusinessRevisionReader? revisionReader = null) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions GrantJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim operationGate = new(1, 1);

    public async Task<NormalHandoffResult> TransferAndCloseAsync(
        Guid targetDeviceId,
        CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var document = await authorityStore.LoadAsync(cancellationToken)
                ?? throw new InvalidDataException("No canonical authority state is available.");
            if (document.Protocol is null)
                throw new InvalidDataException("Normal handoff requires canonical M07 authority metadata.");
            document.Protocol.Validate();

            return document.Protocol.Phase switch
            {
                AuthorityPhase.Authoritative => await StartTransferAsync(document.Protocol, targetDeviceId, cancellationToken),
                AuthorityPhase.TransferPreparing or AuthorityPhase.RelinquishedPendingGrant
                    => await ResumeTransferAsync(document.Protocol, cancellationToken),
                AuthorityPhase.ReleasedNonAuthoritative
                    => NormalHandoffResult.Success(document.Protocol.Transfer!.TransferId),
                _ => throw new WriteAuthorityException(document.EffectiveState)
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return NormalHandoffResult.Failure(Guid.Empty, exception);
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

    private async Task<NormalHandoffResult> StartTransferAsync(
        AuthorityProtocolState source,
        Guid targetDeviceId,
        CancellationToken cancellationToken)
    {
        if (source.LineageId is not { } lineageId || source.Generation < 1)
            throw new InvalidDataException("An authoritative source is missing its lineage/generation binding.");
        if (targetDeviceId == Guid.Empty || targetDeviceId == source.DeviceId)
            throw new InvalidDataException("A normal handoff requires one other exact target device.");

        var currentBusinessRevision = revisionReader is null
            ? source.BusinessRevision
            : await revisionReader.ReadAsync(cancellationToken);
        if (currentBusinessRevision < source.BusinessRevision)
            throw new InvalidDataException("The local business-data revision moved backwards relative to authority state.");
        if (currentBusinessRevision != source.BusinessRevision)
        {
            source = source with
            {
                Revision = checked(source.Revision + 1),
                BusinessRevision = currentBusinessRevision
            };
            await PersistAsync(source, cancellationToken);
        }

        var devices = await systemMetadata.ListCurrentGenerationDevicesAsync(lineageId, source.Generation, cancellationToken);
        if (devices.All(device => device.DeviceId != targetDeviceId))
            throw new InvalidDataException("The selected target is not a current-generation joined device.");

        var transfer = TransferEvidence.Pending(
            Guid.NewGuid(), lineageId, source.Generation, checked(source.HandoffVersion + 1),
            source.DeviceId, targetDeviceId, source.BusinessRevision);
        var preparing = source with
        {
            Revision = checked(source.Revision + 1),
            HandoffVersion = transfer.Version,
            Phase = AuthorityPhase.TransferPreparing,
            Transfer = transfer,
            Recovery = null
        };

        // The in-memory barrier is engaged before any remote/file operation. If the durable
        // transition fails, the only safe result is RecoveryRequired.
        guard.SetState(WriteAuthorityState.Transitioning);
        try
        {
            await PersistAsync(preparing, cancellationToken);
        }
        catch
        {
            guard.SetState(WriteAuthorityState.RecoveryRequired);
            throw;
        }

        return await ContinueTransferAsync(preparing, cancellationToken);
    }

    private async Task<NormalHandoffResult> ResumeTransferAsync(
        AuthorityProtocolState current,
        CancellationToken cancellationToken)
    {
        if (current.Transfer is null)
            throw new InvalidDataException("A transfer phase is missing its immutable transfer identity.");
        guard.SetState(current.WriteState);
        return await ContinueTransferAsync(current, cancellationToken);
    }

    private async Task<NormalHandoffResult> ContinueTransferAsync(
        AuthorityProtocolState current,
        CancellationToken cancellationToken)
    {
        var transfer = current.Transfer!;
        var relinquished = current.Phase is AuthorityPhase.RelinquishedPendingGrant or AuthorityPhase.ReleasedNonAuthoritative;

        try
        {
            if (!relinquished)
            {
                var snapshot = transfer.SnapshotReady
                    ? new TransferSnapshot(transfer.SnapshotName, transfer.SnapshotPath, transfer.SnapshotSize, transfer.SnapshotSha256, transfer.BusinessRevision)
                    : await snapshots.CreateAsync(current, transfer.TransferId, cancellationToken);
                snapshot.Validate();
                if (snapshot.BusinessRevision != transfer.BusinessRevision)
                    throw new InvalidDataException("The transfer snapshot business revision does not match the durable transfer.");

                if (!transfer.SnapshotReady
                    || !string.Equals(transfer.SnapshotName, snapshot.Name, StringComparison.Ordinal)
                    || !string.Equals(transfer.SnapshotPath, snapshot.Path, StringComparison.Ordinal)
                    || transfer.SnapshotSize != snapshot.Size
                    || !string.Equals(transfer.SnapshotSha256, snapshot.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    transfer = transfer with
                    {
                        SnapshotName = snapshot.Name,
                        SnapshotPath = snapshot.Path,
                        SnapshotSize = snapshot.Size,
                        SnapshotSha256 = snapshot.Sha256,
                        SnapshotReady = true
                    };
                    current = current with { Revision = checked(current.Revision + 1), Transfer = transfer };
                    await PersistAsync(current, cancellationToken);
                }

                var release = await transport.EnsureContainerAsync(createIfMissing: true, cancellationToken);
                var snapshotReceipt = await UploadOrReuseAsync(
                    release, transfer.SnapshotName, transfer.SnapshotPath, transfer.SnapshotSize,
                    transfer.SnapshotSha256, cancellationToken);
                transfer = transfer with { SnapshotReceipt = snapshotReceipt, RelinquishedAtUtc = clock.UtcNow };
                var relinquishedState = current with
                {
                    Revision = checked(current.Revision + 1),
                    Phase = AuthorityPhase.RelinquishedPendingGrant,
                    Transfer = transfer
                };
                await PersistAsync(relinquishedState, cancellationToken);
                current = relinquishedState;
                relinquished = true;
            }

            if (current.Phase == AuthorityPhase.RelinquishedPendingGrant)
            {
                var release = await transport.EnsureContainerAsync(createIfMissing: true, cancellationToken);
                var grant = new NormalHandoffGrant(
                    "M07", transfer.TransferId, transfer.LineageId, transfer.Generation, transfer.Version,
                    transfer.SourceDeviceId, transfer.TargetDeviceId, transfer.BusinessRevision,
                    transfer.SnapshotReceipt!, transfer.RelinquishedAtUtc!.Value, clock.UtcNow);
                grant.Validate();
                var grantBytes = JsonSerializer.SerializeToUtf8Bytes(grant, GrantJsonOptions);
                var grantHash = Convert.ToHexString(SHA256.HashData(grantBytes));
                var grantName = GitHubHandoffAssetNames.CreateGrantName(transfer.SnapshotName);
                await using var grantStream = new MemoryStream(grantBytes, writable: false);
                var grantReceipt = await UploadOrReuseAsync(
                    release, grantName, grantStream, grantBytes.LongLength, grantHash, cancellationToken);
                transfer = transfer with { GrantReceipt = grantReceipt };
                current = current with
                {
                    Revision = checked(current.Revision + 1),
                    Phase = AuthorityPhase.ReleasedNonAuthoritative,
                    Transfer = transfer
                };
                await PersistAsync(current, cancellationToken);
            }

            await CleanupNewestThreeAsync(cancellationToken);
            return NormalHandoffResult.Success(transfer.TransferId);
        }
        catch (OperationCanceledException)
        {
            if (!relinquished) await AbortPreparingAsync(current);
            throw;
        }
        catch (Exception exception)
        {
            if (!relinquished)
            {
                try { await AbortPreparingAsync(current); }
                catch { guard.SetState(WriteAuthorityState.RecoveryRequired); }
            }
            return NormalHandoffResult.Failure(transfer.TransferId, exception);
        }
    }

    private async Task<RemoteAssetEvidence> UploadOrReuseAsync(
        GitHubReleaseContainer release,
        string name,
        string path,
        long size,
        string sha256,
        CancellationToken cancellationToken)
    {
        var assets = await transport.ListAssetsAsync(release, cancellationToken);
        var existing = assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!existing.IsComplete)
                throw new InvalidDataException($"An incomplete or starter handoff asset already occupies '{name}'.");
            return ConvertReceipt(release.Id, new GitHubAssetReceipt(
                release.Id, existing.Id, existing.Name, existing.Size, existing.Digest,
                existing.CreatedAtUtc, existing.State), name, size, sha256);
        }

        await using var content = path is not null
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : throw new InvalidDataException("A handoff asset path is missing.");
        var receipt = await transport.UploadAssetAsync(release, name, content, size, sha256, cancellationToken);
        return ConvertReceipt(release.Id, receipt, name, size, sha256);
    }

    private async Task<RemoteAssetEvidence> UploadOrReuseAsync(
        GitHubReleaseContainer release,
        string name,
        Stream content,
        long size,
        string sha256,
        CancellationToken cancellationToken)
    {
        var assets = await transport.ListAssetsAsync(release, cancellationToken);
        var existing = assets.FirstOrDefault(asset => string.Equals(asset.Name, name, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (!existing.IsComplete)
                throw new InvalidDataException($"An incomplete or starter handoff asset already occupies '{name}'.");
            return ConvertReceipt(release.Id, new GitHubAssetReceipt(
                release.Id, existing.Id, existing.Name, existing.Size, existing.Digest,
                existing.CreatedAtUtc, existing.State), name, size, sha256);
        }

        if (content.CanSeek) content.Position = 0;
        var receipt = await transport.UploadAssetAsync(release, name, content, size, sha256, cancellationToken);
        return ConvertReceipt(release.Id, receipt, name, size, sha256);
    }

    private static RemoteAssetEvidence ConvertReceipt(
        long releaseId,
        GitHubAssetReceipt receipt,
        string expectedName,
        long expectedSize,
        string expectedSha256)
    {
        receipt.EnsureStrictlyValidFor(releaseId, expectedName, expectedSize, expectedSha256);
        if (!GitHubSha256.TryNormalizeServerDigest(receipt.Digest, out var digest))
            throw new InvalidDataException("The GitHub receipt digest is not a strict SHA-256 value.");
        return new RemoteAssetEvidence(
            releaseId,
            receipt.AssetId,
            receipt.Name,
            receipt.Size,
            digest[7..],
            receipt.State);
    }

    private async Task PersistAsync(AuthorityProtocolState state, CancellationToken cancellationToken)
    {
        state.Validate();
        var document = new AuthorityStateDocument(2, state.WriteState, clock.UtcNow) { Protocol = state };
        await authorityStore.SaveAsync(document, cancellationToken);
        guard.SetState(state.WriteState);
    }

    private async Task AbortPreparingAsync(AuthorityProtocolState current)
    {
        var transfer = current.Transfer;
        if (transfer is null || current.Phase != AuthorityPhase.TransferPreparing)
        {
            guard.SetState(WriteAuthorityState.RecoveryRequired);
            return;
        }

        var aborted = current with
        {
            Revision = checked(current.Revision + 1),
            HandoffVersion = Math.Max(current.HandoffVersion, transfer.Version),
            Phase = AuthorityPhase.Authoritative,
            Transfer = null,
            Recovery = null
        };
        await PersistAsync(aborted, CancellationToken.None);
    }

    private async Task CleanupNewestThreeAsync(CancellationToken cancellationToken)
    {
        // Retention is deliberately best-effort and exact-ID based. It never changes local
        // authority, and incomplete/starter assets are left untouched for diagnostics/retry.
        try
        {
            var release = await transport.EnsureContainerAsync(createIfMissing: false, cancellationToken);
            var assets = await transport.ListAssetsAsync(release, cancellationToken);
            var completeUnits = assets
                .Where(asset => asset.IsComplete && (GitHubHandoffAssetNames.IsSnapshotName(asset.Name) || GitHubHandoffAssetNames.IsGrantName(asset.Name)))
                .GroupBy(asset => asset.Name[..14], StringComparer.Ordinal)
                .Where(group => group.Any(asset => GitHubHandoffAssetNames.IsSnapshotName(asset.Name))
                    && group.Any(asset => GitHubHandoffAssetNames.IsGrantName(asset.Name)))
                .OrderByDescending(group => group.Key, StringComparer.Ordinal)
                .Skip(3)
                .SelectMany(group => group)
                .ToArray();
            foreach (var asset in completeUnits)
                await transport.DeleteAssetAsync(asset.Id, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // Cleanup failure is explicitly retryable and must not roll back a released source.
        }
    }
}
