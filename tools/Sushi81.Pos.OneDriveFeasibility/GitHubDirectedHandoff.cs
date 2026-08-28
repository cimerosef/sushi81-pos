using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

/// <summary>GitHub-backed directed source flow. OneDrive observers are intentionally absent.</summary>
public sealed class GitHubDirectedSourceCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General) { WriteIndented = true };
    private readonly DurableAuthorityStateStore stateStore;
    private readonly DirectedHandoffCoordinator coordinator;
    private readonly IGitHubHandoffTransport transport;
    private readonly string stateDirectory;
    private readonly Func<DateTimeOffset> clock;

    public GitHubDirectedSourceCoordinator(string stateDirectory, IGitHubHandoffTransport transport, Func<DateTimeOffset>? clock = null)
    {
        this.stateDirectory = Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(this.stateDirectory);
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        stateStore = new DurableAuthorityStateStore(Path.Combine(this.stateDirectory, "source-authority.json"));
        coordinator = new DirectedHandoffCoordinator(stateStore);
    }

    public async Task<DirectedTransferOperationResult> RunAsync(DirectedTransferIdentity transfer, CancellationToken cancellationToken = default)
    {
        if (!transfer.IsValid) return new(false, "invalid-transfer", "Transfer identity is invalid.");
        var state = TryLoad();
        if (state is null)
        {
            var initialized = coordinator.InitializeAuthoritative(transfer.SourceDeviceId, [transfer.SourceDeviceId, transfer.TargetDeviceId]);
            if (!initialized.Succeeded && initialized.Code != "already-initialized") return initialized;
            state = TryLoad();
        }
        if (state is null || state.DeviceId != transfer.SourceDeviceId || state.Transfer is { } existing && existing != transfer)
            return new(false, "transfer-state-mismatch", "Durable source state does not match the exact immutable transfer.", state);
        if (state.Mode == DirectedAuthorityMode.Authoritative)
        {
            var prepared = coordinator.PrepareTransfer(transfer);
            if (!prepared.Succeeded && prepared.Code != "already-prepared") return prepared;
            state = coordinator.Current;
        }
        if (state.Transfer != transfer) return new(false, "transfer-state-mismatch", "Only the same transfer identity can be resumed.", state);
        var release = await transport.EnsureContainerAsync(false, cancellationToken);
        var snapshotReceipt = state.SnapshotEvidence?.RemoteReceipt ?? await ReadReceiptAsync(Path.Combine(stateDirectory, "github-snapshot-receipt.json"), transfer.TransferId, cancellationToken);
        string? snapshotPath = null;
        string? snapshotName = null;
        if (state.Mode == DirectedAuthorityMode.TransferPrepared)
        {
            snapshotName = snapshotReceipt?.Name ?? await ReserveSnapshotNameAsync(release, cancellationToken);
            snapshotPath = Path.Combine(stateDirectory, snapshotName);
            if (!File.Exists(snapshotPath)) await DirectedSnapshotEvidence.CreateSyntheticAsync(snapshotPath, cancellationToken);
            if (snapshotReceipt is null)
            {
                var local = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshotPath, true, cancellationToken);
                if (!local.IsValid) return new(false, "sqlite-integrity-failure", "Snapshot failed SQLite integrity validation.", state);
                snapshotReceipt = await transport.UploadAssetAsync(release, snapshotName, snapshotPath, cancellationToken);
                await WriteReceiptAsync(Path.Combine(stateDirectory, "github-snapshot-receipt.json"), transfer.TransferId, snapshotReceipt, cancellationToken);
                local = local with { RemoteReceipt = snapshotReceipt };
                var relinquished = await coordinator.DurablyRelinquishAsync(local, cancellationToken);
                if (!relinquished.Succeeded && relinquished.Code != "already-relinquished") return relinquished;
                state = coordinator.Current;
            }
            else
            {
                snapshotPath = Path.Combine(stateDirectory, snapshotReceipt.Name);
                if (!File.Exists(snapshotPath)) return new(false, "snapshot-local-missing", "Durable snapshot receipt exists but local snapshot is missing.", state);
                if (state.Mode == DirectedAuthorityMode.TransferPrepared)
                {
                    var local = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshotPath, true, snapshotReceipt, cancellationToken);
                    var relinquished = await coordinator.DurablyRelinquishAsync(local, cancellationToken);
                    if (!relinquished.Succeeded && relinquished.Code != "already-relinquished") return relinquished;
                    state = coordinator.Current;
                }
            }
        }
        if (state.Mode is not (DirectedAuthorityMode.RelinquishedBlocked or DirectedAuthorityMode.Released))
            return new(false, "source-not-relinquished", "GitHub source flow did not cross durable relinquishment.", state);
        if (state.Mode == DirectedAuthorityMode.Released) return new(true, "already-released", "The same GitHub transfer is already durably complete.", state);
        var evidence = state.SnapshotEvidence!;
        snapshotReceipt ??= evidence.RemoteReceipt;
        if (snapshotReceipt is null) return new(false, "snapshot-receipt-missing", "Durable relinquishment has no GitHub snapshot receipt.", state);
        snapshotName ??= snapshotReceipt.Name;
        snapshotPath ??= Path.Combine(stateDirectory, snapshotName);
        var grantName = GitHubSnapshotName.GrantName(snapshotName);
        var grantPath = Path.Combine(stateDirectory, grantName);
        var grant = new GitHubHandoffGrant(1, transfer.TransferId, transfer.LineageId, transfer.Generation, transfer.HandoffVersion, transfer.SourceDeviceId, transfer.TargetDeviceId, snapshotReceipt.ReleaseId, snapshotReceipt.AssetId, snapshotReceipt.Name, snapshotReceipt.Size, snapshotReceipt.Digest[7..].ToLowerInvariant(), evidence.RemoteReceipt?.CreatedAtUtc ?? DateTimeOffset.UtcNow, state.UpdatedAtUtc, clock().ToUniversalTime());
        if (!File.Exists(grantPath))
        {
            await using var stream = new FileStream(grantPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, grant, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken); stream.Flush(true);
        }
        var grantReceipt = state.GrantReceipt;
        if (grantReceipt is null)
        {
            var grantBytes = await File.ReadAllBytesAsync(grantPath, cancellationToken);
            var grantDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(grantBytes)).ToLowerInvariant();
            var existingGrant = (await transport.ListAssetsAsync(release, cancellationToken)).FirstOrDefault(a => a.Name == grantName);
            if (existingGrant is { IsComplete: true } && existingGrant.Size == grantBytes.LongLength && existingGrant.Digest.Equals(grantDigest, StringComparison.OrdinalIgnoreCase))
                grantReceipt = new GitHubAssetReceipt(release.Id, existingGrant.Id, existingGrant.Name, existingGrant.Size, existingGrant.Digest, existingGrant.CreatedAtUtc);
            else
                grantReceipt = await transport.UploadAssetAsync(release, grantName, grantPath, cancellationToken);
        }
        var committed = coordinator.CommitGitHubReleased($"github://release/{release.Id}/asset/{snapshotReceipt.AssetId}", $"github://release/{release.Id}/asset/{grantReceipt.AssetId}", grantReceipt);
        if (!committed.Succeeded && committed.Code != "already-released") return committed;
        _ = await new GitHubHandoffRetention(transport).CleanupAsync(release, new HashSet<long> { snapshotReceipt.AssetId, grantReceipt.AssetId }, cancellationToken);
        return new(true, "github-source-released", "GitHub snapshot and grant publication completed after durable source relinquishment.", coordinator.Current);
    }

    private async Task<string> ReserveSnapshotNameAsync(GitHubReleaseContainer release, CancellationToken cancellationToken)
    {
        var assets = await transport.ListAssetsAsync(release, cancellationToken);
        var candidate = clock().ToLocalTime();
        for (var i = 0; i < 120; i++)
        {
            var name = GitHubSnapshotName.Create(candidate.AddSeconds(i));
            if (!assets.Any(a => a.Name.Equals(name, StringComparison.Ordinal))) return name;
        }
        throw new GitHubTransportException("No unused snapshot timestamp could be reserved safely.");
    }
    private DurableAuthorityState? TryLoad() { try { return stateStore.Load(); } catch (InvalidDataException) { return null; } catch (IOException) { return null; } }
    private sealed record SnapshotReceiptEnvelope(string TransferId, GitHubAssetReceipt Receipt);
    private static async Task WriteReceiptAsync(string path, string transferId, GitHubAssetReceipt receipt, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await JsonSerializer.SerializeAsync(stream, new SnapshotReceiptEnvelope(transferId, receipt), JsonOptions, cancellationToken); await stream.FlushAsync(cancellationToken); stream.Flush(true);
    }
    private static async Task<GitHubAssetReceipt?> ReadReceiptAsync(string path, string transferId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        try { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true); var envelope = await JsonSerializer.DeserializeAsync<SnapshotReceiptEnvelope>(stream, JsonOptions, cancellationToken); return envelope is { Receipt.IsValid: true } && envelope.TransferId == transferId ? envelope.Receipt : null; }
        catch (JsonException) { return null; }
        catch (IOException) { return null; }
    }
}

/// <summary>GitHub-backed target flow. Downloaded assets are validated before existing durable target acquisition.</summary>
public sealed class GitHubDirectedTargetCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General) { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly string stateDirectory;
    private readonly IGitHubHandoffTransport transport;
    public GitHubDirectedTargetCoordinator(string stateDirectory, IGitHubHandoffTransport transport) { this.stateDirectory = Path.GetFullPath(stateDirectory); this.transport = transport ?? throw new ArgumentNullException(nameof(transport)); Directory.CreateDirectory(this.stateDirectory); }

    public async Task<DirectedTransferOperationResult> AcquireAsync(DirectedTransferIdentity expectedTransfer, CancellationToken cancellationToken = default)
    {
        if (!expectedTransfer.IsValid) return new(false, "invalid-transfer", "Transfer identity is invalid.");
        var release = await transport.EnsureContainerAsync(false, cancellationToken);
        var assets = await transport.ListAssetsAsync(release, cancellationToken);
        foreach (var grantAsset in assets.Where(a => GitHubSnapshotName.IsGrant(a.Name)).OrderByDescending(a => a.Name, StringComparer.Ordinal))
        {
            if (!grantAsset.IsComplete) continue;
            var grantBytes = await transport.DownloadAssetAsync(grantAsset.Id, cancellationToken);
            var grantDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(grantBytes)).ToLowerInvariant();
            if (grantBytes.LongLength != grantAsset.Size || !grantDigest.Equals(grantAsset.Digest, StringComparison.OrdinalIgnoreCase))
                return new(false, "grant-hash-mismatch", "Downloaded grant does not match its GitHub server receipt.");
            GitHubHandoffGrant? grant;
            try { grant = JsonSerializer.Deserialize<GitHubHandoffGrant>(grantBytes, JsonOptions); } catch (JsonException) { continue; }
            if (grant is null || !grant.IsValid || grant.TargetDeviceId != expectedTransfer.TargetDeviceId || grant.TransferId != expectedTransfer.TransferId || grant.LineageId != expectedTransfer.LineageId || grant.Generation != expectedTransfer.Generation || grant.HandoffVersion != expectedTransfer.HandoffVersion) continue;
            if (grant.SnapshotReleaseId != release.Id) return new(false, "snapshot-release-mismatch", "Grant references a different release.");
            var snapshotAsset = await transport.GetAssetAsync(grant.SnapshotAssetId, cancellationToken);
            if (!snapshotAsset.IsComplete || snapshotAsset.Name != grant.SnapshotAssetName || snapshotAsset.Size != grant.SnapshotByteLength || !snapshotAsset.Digest.Equals("sha256:" + grant.SnapshotSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "remote-snapshot-mismatch", "GitHub snapshot metadata contradicts the immutable grant.");
            var bytes = await transport.DownloadAssetAsync(snapshotAsset.Id, cancellationToken);
            if (bytes.LongLength != grant.SnapshotByteLength || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(grant.SnapshotSha256, StringComparison.OrdinalIgnoreCase)) return new(false, "snapshot-hash-mismatch", "Downloaded snapshot does not match the grant and GitHub receipt.");
            var handoffDirectory = Path.Combine(stateDirectory, "github-handoff"); Directory.CreateDirectory(handoffDirectory);
            var snapshotPath = Path.Combine(handoffDirectory, grant.SnapshotAssetName); await File.WriteAllBytesAsync(snapshotPath, bytes, cancellationToken);
            var markerBase = "directed-" + expectedTransfer.TransferId;
            var readyPath = Path.Combine(handoffDirectory, markerBase + ".ready.json"); var localGrantPath = Path.Combine(handoffDirectory, markerBase + ".grant.json");
            var marker = new DirectedTransferMarker(1, expectedTransfer.TransferId, expectedTransfer.LineageId, expectedTransfer.Generation, expectedTransfer.HandoffVersion, expectedTransfer.SourceDeviceId, expectedTransfer.TargetDeviceId, grant.SnapshotSha256, grant.SnapshotByteLength, grant.GrantPublishedAtUtc, "ready", "released");
            await File.WriteAllTextAsync(readyPath, JsonSerializer.Serialize(marker), cancellationToken); await File.WriteAllTextAsync(localGrantPath, JsonSerializer.Serialize(marker with { MarkerType = "grant" }), cancellationToken);
            var coordinator = new DirectedTargetAcquisitionCoordinator(new DurableTargetAcquisitionStore(Path.Combine(stateDirectory, "target-" + expectedTransfer.TargetDeviceId + ".json")), expectedTransfer.TargetDeviceId, [expectedTransfer.SourceDeviceId, expectedTransfer.TargetDeviceId]);
            return await coordinator.AcquireAsync(handoffDirectory, snapshotPath, expectedTransfer, cancellationToken);
        }
        return new(false, "missing-grant", "No exact target-bound GitHub grant was found.");
    }
}
