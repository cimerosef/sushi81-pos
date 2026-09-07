using System.Security.Cryptography;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.Authority;

public sealed record TargetAcquisitionResult(Guid TransferId, bool Succeeded, Exception? Error)
{
    public static TargetAcquisitionResult Success(Guid transferId) => new(transferId, true, null);

    public static TargetAcquisitionResult Failure(Guid transferId, Exception error) => new(transferId, false, error);
}

/// <summary>
/// Data-first target acquisition. A valid target grant never enables writes until the
/// staged database is installed and the exact authoritative state is durable.
/// </summary>
public sealed class TargetAcquisitionService(
    IAuthorityStateStore authorityStore,
    WriteAuthorityGuard guard,
    ISystemMetadataStore systemMetadata,
    IGitHubHandoffTransport transport,
    ITransferSnapshotInstaller installer,
    IBusinessClock clock) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim operationGate = new(1, 1);

    public async Task<TargetAcquisitionResult> AcquireAsync(CancellationToken cancellationToken = default)
    {
        await operationGate.WaitAsync(cancellationToken);
        try
        {
            var document = await authorityStore.LoadAsync(cancellationToken)
                ?? throw new InvalidDataException("No canonical authority state is available.");
            if (document.Protocol is null)
                throw new InvalidDataException("Target acquisition requires canonical M07 authority metadata.");
            document.Protocol.Validate();
            return await AcquireFromStateAsync(document.Protocol, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return TargetAcquisitionResult.Failure(Guid.Empty, exception);
        }
        finally
        {
            operationGate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        operationGate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<TargetAcquisitionResult> AcquireFromStateAsync(
        AuthorityProtocolState local,
        CancellationToken cancellationToken)
    {
        if (local.LineageId is not { } lineageId || local.Generation < 1)
            throw new InvalidDataException("A target must be paired to a current lineage before normal acquisition.");
        if (local.Phase is not (AuthorityPhase.PairedUninitializedReadOnly or AuthorityPhase.NonAuthoritativeReadOnly
            or AuthorityPhase.TargetAcquisitionPending))
            throw new WriteAuthorityException(local.WriteState);

        var devices = await systemMetadata.ListCurrentGenerationDevicesAsync(lineageId, local.Generation, cancellationToken);
        if (devices.All(device => device.DeviceId != local.DeviceId))
            throw new InvalidDataException("The local device is not a current-generation joined target.");

        var release = await transport.EnsureContainerAsync(createIfMissing: false, cancellationToken);
        var grant = local.Phase == AuthorityPhase.TargetAcquisitionPending
            ? await LoadExactPendingGrantAsync(release, local.Transfer!, cancellationToken)
            : await DiscoverExactTargetGrantAsync(release, local, cancellationToken);

        var snapshotRemote = await transport.GetAssetAsync(grant.Grant.SnapshotReceipt.AssetId, cancellationToken);
        ValidateRemoteSnapshot(snapshotRemote, release.Id, grant.Grant.SnapshotReceipt);
        await using var snapshotStream = await transport.DownloadAssetAsync(snapshotRemote.Id, cancellationToken);
        var staging = await installer.StageAndValidateAsync(
            grant.Grant.TransferId,
            snapshotStream,
            grant.Grant.SnapshotReceipt.Name,
            grant.Grant.SnapshotReceipt.Size,
            grant.Grant.SnapshotReceipt.Sha256,
            cancellationToken);
        staging.Validate();

        var pendingTransfer = new TransferEvidence(
            grant.Grant.TransferId,
            grant.Grant.LineageId,
            grant.Grant.Generation,
            grant.Grant.HandoffVersion,
            grant.Grant.SourceDeviceId,
            grant.Grant.TargetDeviceId,
            grant.Grant.BusinessRevision,
            grant.Grant.SnapshotReceipt.Name,
            staging.Path,
            staging.Size,
            staging.Sha256,
            grant.Grant.SnapshotReceipt,
            grant.Receipt,
            grant.Grant.RelinquishedAtUtc,
            SnapshotReady: true);
        var pending = local with
        {
            Revision = checked(local.Revision + 1),
            HandoffVersion = grant.Grant.HandoffVersion,
            BusinessRevision = grant.Grant.BusinessRevision,
            Phase = AuthorityPhase.TargetAcquisitionPending,
            Transfer = pendingTransfer,
            Recovery = null
        };
        if (local.Phase != AuthorityPhase.TargetAcquisitionPending)
            await PersistAsync(pending, cancellationToken);
        else
            guard.SetState(WriteAuthorityState.Transitioning);

        try
        {
            await installer.InstallAndVerifyAsync(staging, cancellationToken);
            var authoritative = pending with
            {
                Revision = checked(pending.Revision + 1),
                Phase = AuthorityPhase.Authoritative,
                Transfer = null,
                Recovery = null
            };
            await PersistAsync(authoritative, cancellationToken);
            return TargetAcquisitionResult.Success(grant.Grant.TransferId);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            guard.SetState(WriteAuthorityState.RecoveryRequired);
            return TargetAcquisitionResult.Failure(grant.Grant.TransferId, exception);
        }
    }

    private async Task<DiscoveredGrant> DiscoverExactTargetGrantAsync(
        GitHubReleaseContainer release,
        AuthorityProtocolState local,
        CancellationToken cancellationToken)
    {
        var candidates = new List<DiscoveredGrant>();
        foreach (var asset in await transport.ListAssetsAsync(release, cancellationToken))
        {
            if (!asset.IsComplete || !GitHubHandoffAssetNames.IsGrantName(asset.Name)) continue;
            var discovered = await ReadGrantAsync(release, asset, cancellationToken);
            if (discovered.Grant.TargetDeviceId != local.DeviceId) continue;
            if (discovered.Grant.LineageId != local.LineageId || discovered.Grant.Generation != local.Generation)
                throw new InvalidDataException("A target-bound grant belongs to a different lineage or generation.");
            if (discovered.Grant.HandoffVersion <= local.HandoffVersion) continue;
            candidates.Add(discovered);
        }

        if (candidates.Count == 0)
            throw new InvalidDataException("No current-generation target-bound handoff grant is available.");
        var highest = candidates.Max(candidate => candidate.Grant.HandoffVersion);
        var selected = candidates.Where(candidate => candidate.Grant.HandoffVersion == highest).ToArray();
        if (selected.Length != 1)
            throw new InvalidDataException("Multiple target-bound grants have the same handoff version.");
        return selected[0];
    }

    private async Task<DiscoveredGrant> LoadExactPendingGrantAsync(
        GitHubReleaseContainer release,
        TransferEvidence pending,
        CancellationToken cancellationToken)
    {
        if (pending.GrantReceipt is null)
            throw new InvalidDataException("Pending target acquisition is missing its grant receipt.");
        var remote = await transport.GetAssetAsync(pending.GrantReceipt.AssetId, cancellationToken);
        var discovered = await ReadGrantAsync(release, remote, cancellationToken);
        var grant = discovered.Grant;
        if (grant.TransferId != pending.TransferId || grant.LineageId != pending.LineageId
            || grant.Generation != pending.Generation || grant.HandoffVersion != pending.Version
            || grant.SourceDeviceId != pending.SourceDeviceId || grant.TargetDeviceId != pending.TargetDeviceId
            || grant.BusinessRevision != pending.BusinessRevision
            || !ReceiptsEqual(discovered.Receipt, pending.GrantReceipt))
            throw new InvalidDataException("The pending target acquisition grant does not match its durable identity or receipt.");
        return discovered;
    }

    private static bool ReceiptsEqual(RemoteAssetEvidence actual, RemoteAssetEvidence expected) =>
        actual.ReleaseId == expected.ReleaseId && actual.AssetId == expected.AssetId
        && actual.Size == expected.Size && actual.Name == expected.Name
        && string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(actual.State, expected.State, StringComparison.OrdinalIgnoreCase);

    private async Task<DiscoveredGrant> ReadGrantAsync(
        GitHubReleaseContainer release,
        GitHubRemoteAsset remote,
        CancellationToken cancellationToken)
    {
        var remoteReceipt = ToRemoteEvidence(release.Id, remote);
        await using var stream = await transport.DownloadAssetAsync(remote.Id, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.LongLength != remote.Size)
            throw new InvalidDataException("The downloaded grant size does not match the server receipt.");
        var digest = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(digest, remoteReceipt.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded grant digest does not match the server receipt.");
        var grant = JsonSerializer.Deserialize<NormalHandoffGrant>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The target-bound grant is empty.");
        grant.Validate();
        if (grant.SnapshotReceipt.ReleaseId != release.Id)
            throw new InvalidDataException("The target-bound grant references a different release.");
        return new DiscoveredGrant(grant, remoteReceipt);
    }

    private static void ValidateRemoteSnapshot(
        GitHubRemoteAsset remote,
        long releaseId,
        RemoteAssetEvidence expected)
    {
        if (!remote.IsComplete || remote.Id != expected.AssetId || remote.Name != expected.Name
            || remote.Size != expected.Size || remote.State != expected.State
            || !string.Equals(remote.Digest, "sha256:" + expected.Sha256, StringComparison.OrdinalIgnoreCase)
            || expected.ReleaseId != releaseId)
            throw new InvalidDataException("The snapshot asset does not match the target-bound grant.");
    }

    private static RemoteAssetEvidence ToRemoteEvidence(long releaseId, GitHubRemoteAsset remote)
    {
        if (!remote.IsComplete || !GitHubSha256.TryNormalizeServerDigest(remote.Digest, out var digest))
            throw new InvalidDataException("The remote grant asset is not a strict uploaded receipt.");
        return new RemoteAssetEvidence(releaseId, remote.Id, remote.Name, remote.Size, digest[7..], remote.State);
    }

    private async Task PersistAsync(AuthorityProtocolState state, CancellationToken cancellationToken)
    {
        state.Validate();
        var document = new AuthorityStateDocument(2, state.WriteState, clock.UtcNow) { Protocol = state };
        await authorityStore.SaveAsync(document, cancellationToken);
        guard.SetState(state.WriteState);
    }

    private sealed record DiscoveredGrant(NormalHandoffGrant Grant, RemoteAssetEvidence Receipt);
}
