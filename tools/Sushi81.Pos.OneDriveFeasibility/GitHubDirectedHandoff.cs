using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

/// <summary>GitHub-backed directed source flow. OneDrive observers are intentionally absent.</summary>
public sealed class GitHubDirectedSourceCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General) { WriteIndented = true };
    private static readonly JsonSerializerOptions GrantJsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        PropertyNameCaseInsensitive = false
    };
    private readonly DurableAuthorityStateStore stateStore;
    private readonly DirectedHandoffCoordinator coordinator;
    private readonly IGitHubHandoffTransport transport;
    private readonly string stateDirectory;
    private readonly Func<DateTimeOffset> clock;

    public GitHubDirectedSourceCoordinator(
        string stateDirectory,
        IGitHubHandoffTransport transport,
        Func<DateTimeOffset>? clock = null,
        IDurableAuthorityStateFailureInjector? stateFailureInjector = null)
    {
        this.stateDirectory = Path.GetFullPath(stateDirectory);
        Directory.CreateDirectory(this.stateDirectory);
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        this.clock = clock ?? (() => DateTimeOffset.Now);
        stateStore = new DurableAuthorityStateStore(Path.Combine(this.stateDirectory, "source-authority.json"), stateFailureInjector);
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
        var receiptPath = Path.Combine(stateDirectory, "github-snapshot-receipt.json");
        var persistedReceipt = await ReadReceiptAsync(receiptPath, transfer.TransferId, cancellationToken);
        if (persistedReceipt.Exists && persistedReceipt.Receipt is null && persistedReceipt.Error is not null)
            return new(false, "snapshot-receipt-invalid", persistedReceipt.Error ?? "Persisted GitHub snapshot receipt is invalid; source remains prepared.", state);
        var snapshotReceipt = state.SnapshotEvidence?.RemoteReceipt ?? persistedReceipt.Receipt;
        string? snapshotPath = null;
        string? snapshotName = null;
        if (state.Mode == DirectedAuthorityMode.TransferPrepared)
        {
            snapshotName = snapshotReceipt?.Name ?? await ReserveSnapshotNameAsync(release, cancellationToken);
            snapshotPath = Path.Combine(stateDirectory, snapshotName);
            if (snapshotReceipt is null)
            {
                if (!File.Exists(snapshotPath)) await DirectedSnapshotEvidence.CreateSyntheticAsync(snapshotPath, cancellationToken);
                var local = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshotPath, true, cancellationToken);
                if (!local.IsValid) return new(false, "sqlite-integrity-failure", "Snapshot failed SQLite integrity validation.", state);
                try
                {
                    snapshotReceipt = await UploadAssetWithRetryAsync(release, snapshotName, snapshotPath, cancellationToken);
                }
                catch (GitHubTransportException exception)
                {
                    return new(false, "snapshot-upload-failed", exception.Message, state);
                }
                await WriteReceiptAsync(receiptPath, transfer.TransferId, snapshotReceipt, cancellationToken);
                var revalidated = await RevalidateSnapshotReceiptAsync(release, transfer, snapshotPath, snapshotReceipt, cancellationToken);
                if (!revalidated.Succeeded) return new(false, revalidated.Code, revalidated.Message, state);
                var relinquished = await coordinator.DurablyRelinquishAsync(revalidated.Evidence!, cancellationToken);
                if (!relinquished.Succeeded && relinquished.Code != "already-relinquished") return relinquished;
                state = coordinator.Current;
            }
            else
            {
                snapshotPath = Path.Combine(stateDirectory, snapshotReceipt.Name);
                if (!File.Exists(snapshotPath)) return new(false, "snapshot-local-missing", "Durable snapshot receipt exists but local snapshot is missing.", state);
                if (state.Mode == DirectedAuthorityMode.TransferPrepared)
                {
                    var revalidated = await RevalidateSnapshotReceiptAsync(release, transfer, snapshotPath, snapshotReceipt, cancellationToken);
                    if (!revalidated.Succeeded) return new(false, revalidated.Code, revalidated.Message, state);
                    var relinquished = await coordinator.DurablyRelinquishAsync(revalidated.Evidence!, cancellationToken);
                    if (!relinquished.Succeeded && relinquished.Code != "already-relinquished") return relinquished;
                    state = coordinator.Current;
                }
            }
        }
        if (state.Mode is not (DirectedAuthorityMode.RelinquishedBlocked or DirectedAuthorityMode.Released))
            return new(false, "source-not-relinquished", "GitHub source flow did not cross durable relinquishment.", state);
        if (state.Mode == DirectedAuthorityMode.Released) return new(true, "already-released", "The same GitHub transfer is already durably complete.", state);
        // Reconstruct from durable state at the relinquishment boundary.  The
        // grant is not even created until the fresh write gate is false.
        var reconstructed = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(Path.Combine(stateDirectory, "source-authority.json")));
        if (reconstructed.MayBusinessWrite(transfer.SourceDeviceId))
            return new(false, "source-write-gate-failed", "Reconstructed source authority remained writable after durable relinquishment.", reconstructed.Current);
        state = reconstructed.Current ?? state;
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
            try { await WriteAtomicJsonAsync(grantPath, grant, GrantJsonOptions, cancellationToken); }
            catch (IOException) when (File.Exists(grantPath)) { }
        }
        var persistedGrant = await ReadGrantAsync(grantPath, cancellationToken);
        if (persistedGrant is null || !MatchesGrant(persistedGrant, grant))
        {
            return new(false, "grant-file-invalid", "The local grant artifact is malformed or does not match the immutable transfer; source remains blocked.", state);
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
            {
                try
                {
                    grantReceipt = await UploadAssetWithRetryAsync(release, grantName, grantPath, cancellationToken);
                }
                catch (GitHubTransportException exception)
                {
                    return new(false, "grant-upload-failed", exception.Message, state);
                }
            }
        }
        var committed = coordinator.CommitGitHubReleased($"github://release/{release.Id}/asset/{snapshotReceipt.AssetId}", $"github://release/{release.Id}/asset/{grantReceipt.AssetId}", grantReceipt);
        if (!committed.Succeeded && committed.Code != "already-released") return committed;
        _ = await new GitHubHandoffRetention(transport, Path.Combine(stateDirectory, "github-retention-plan.json"))
            .CleanupAsync(release, new HashSet<long> { snapshotReceipt.AssetId, grantReceipt.AssetId }, cancellationToken);
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

    private async Task<GitHubAssetReceipt> UploadAssetWithRetryAsync(
        GitHubReleaseContainer release,
        string name,
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var digest = GitHubDigest.FromHex(Convert.ToHexString(SHA256.HashData(bytes)));
        GitHubTransportException? lastFailure = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try { return await transport.UploadAssetAsync(release, name, path, cancellationToken); }
            catch (GitHubTransportException uploadFailure)
            {
                lastFailure = uploadFailure;
                IReadOnlyList<GitHubRemoteAsset> assets;
                try { assets = await transport.ListAssetsAsync(release, cancellationToken); }
                catch (GitHubTransportException) { throw uploadFailure; }
                var matches = assets.Where(asset => asset.Name.Equals(name, StringComparison.Ordinal)).ToArray();
                var exact = matches.FirstOrDefault(asset => asset.IsComplete
                    && asset.Size == bytes.LongLength
                    && asset.Digest.Equals(digest, StringComparison.OrdinalIgnoreCase));
                if (exact is { })
                    return new GitHubAssetReceipt(release.Id, exact.Id, exact.Name, exact.Size, exact.Digest, exact.CreatedAtUtc);

                var starters = matches.Where(asset => asset.State.Equals("starter", StringComparison.OrdinalIgnoreCase)).ToArray();
                foreach (var starter in starters)
                {
                    try { await transport.DeleteAssetAsync(starter.Id, cancellationToken); }
                    catch (GitHubTransportException deleteFailure) when (deleteFailure.StatusCode == HttpStatusCode.NotFound) { }
                }

                // A 502 may leave a starter asset behind; delete only those
                // exact IDs and retry once. Other failures remain fail-closed
                // unless an exact complete asset was already acknowledged.
                if (starters.Length == 0 && uploadFailure.StatusCode is not HttpStatusCode.BadGateway and not HttpStatusCode.GatewayTimeout)
                    throw;
                if (attempt == 1) throw;
            }
        }

        throw lastFailure ?? new GitHubTransportException("GitHub asset upload did not complete.");
    }

    private static async Task WriteAtomicJsonAsync<T>(string path, T value, JsonSerializerOptions options, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null, ignoreMetadataErrors: true);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static async Task<GitHubHandoffGrant?> ReadGrantAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            return await JsonSerializer.DeserializeAsync<GitHubHandoffGrant>(stream, GrantJsonOptions, cancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    private static bool MatchesGrant(GitHubHandoffGrant actual, GitHubHandoffGrant expected) =>
        actual.IsValid
        && actual.TransferId == expected.TransferId
        && actual.LineageId == expected.LineageId
        && actual.Generation == expected.Generation
        && actual.HandoffVersion == expected.HandoffVersion
        && actual.SourceDeviceId == expected.SourceDeviceId
        && actual.TargetDeviceId == expected.TargetDeviceId
        && actual.SnapshotReleaseId == expected.SnapshotReleaseId
        && actual.SnapshotAssetId == expected.SnapshotAssetId
        && actual.SnapshotAssetName == expected.SnapshotAssetName
        && actual.SnapshotByteLength == expected.SnapshotByteLength
        && actual.SnapshotSha256.Equals(expected.SnapshotSha256, StringComparison.OrdinalIgnoreCase);

    private DurableAuthorityState? TryLoad() { try { return stateStore.Load(); } catch (InvalidDataException) { return null; } catch (IOException) { return null; } }
    private sealed record SnapshotReceiptEnvelope(string TransferId, GitHubAssetReceipt Receipt);
    private static async Task WriteReceiptAsync(string path, string transferId, GitHubAssetReceipt receipt, CancellationToken cancellationToken)
    {
        await WriteAtomicJsonAsync(path, new SnapshotReceiptEnvelope(transferId, receipt), JsonOptions, cancellationToken);
    }
    private sealed record ReceiptReadResult(GitHubAssetReceipt? Receipt, bool Exists, string? Error);
    private static async Task<ReceiptReadResult> ReadReceiptAsync(string path, string transferId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new(null, false, null);
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var envelope = await JsonSerializer.DeserializeAsync<SnapshotReceiptEnvelope>(stream, JsonOptions, cancellationToken);
            if (envelope is null || string.IsNullOrWhiteSpace(envelope.TransferId))
                return new(null, true, "Persisted GitHub snapshot receipt does not match the strict receipt schema.");
            // A valid receipt for an earlier completed transfer is retained
            // as local history.  It must not block a later transfer on the
            // same device; the new transfer reserves its own immutable name
            // and atomically replaces this envelope after upload.
            if (!string.Equals(envelope.TransferId, transferId, StringComparison.Ordinal))
                return new(null, true, null);
            if (envelope.Receipt is not { IsValid: true })
                return new(null, true, "Persisted GitHub snapshot receipt does not match the strict receipt schema.");
            return new(envelope.Receipt, true, null);
        }
        catch (JsonException exception) { return new(null, true, "Persisted GitHub snapshot receipt is malformed: " + exception.Message); }
        catch (IOException exception) { return new(null, true, "Persisted GitHub snapshot receipt could not be read: " + exception.Message); }
    }

    private async Task<(bool Succeeded, string Code, string Message, DirectedSnapshotEvidence? Evidence)> RevalidateSnapshotReceiptAsync(
        GitHubReleaseContainer release,
        DirectedTransferIdentity transfer,
        string snapshotPath,
        GitHubAssetReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (!receipt.IsValid || receipt.ReleaseId != release.Id || !GitHubSnapshotName.IsValid(receipt.Name) || !File.Exists(snapshotPath))
            return (false, "snapshot-receipt-invalid", "Persisted GitHub snapshot receipt is not bound to the current release and local snapshot.", null);
        try
        {
            var remote = await transport.GetAssetAsync(receipt.AssetId, cancellationToken);
            if (!remote.IsComplete || remote.Id != receipt.AssetId || remote.Name != receipt.Name || remote.Size != receipt.Size
                || !remote.Digest.Equals(receipt.Digest, StringComparison.OrdinalIgnoreCase))
                return (false, "snapshot-receipt-mismatch", "GitHub snapshot receipt no longer matches the complete remote asset.", null);
            var evidence = await DirectedSnapshotEvidence.CaptureAsync(transfer, snapshotPath, true, receipt, cancellationToken);
            if (!evidence.IsValid || evidence.SnapshotByteLength != receipt.Size
                || !evidence.SnapshotChecksum.Equals(receipt.Digest[7..], StringComparison.OrdinalIgnoreCase))
                return (false, "snapshot-local-mismatch", "The current local SQLite snapshot no longer matches the persisted GitHub receipt.", null);
            return (true, "snapshot-receipt-validated", "GitHub snapshot receipt and local SQLite snapshot are unchanged.", evidence);
        }
        catch (GitHubTransportException exception)
        {
            return (false, "snapshot-remote-revalidation-failed", exception.Message, null);
        }
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
        var candidates = new List<(GitHubHandoffGrant Grant, GitHubRemoteAsset GrantAsset)>();
        foreach (var grantAsset in assets.Where(a => GitHubSnapshotName.IsGrant(a.Name)))
        {
            if (!grantAsset.IsComplete) continue;
            var grantBytes = await transport.DownloadAssetAsync(grantAsset.Id, cancellationToken);
            var grantDigest = "sha256:" + Convert.ToHexString(SHA256.HashData(grantBytes)).ToLowerInvariant();
            if (grantBytes.LongLength != grantAsset.Size || !grantDigest.Equals(grantAsset.Digest, StringComparison.OrdinalIgnoreCase))
                return new(false, "grant-hash-mismatch", "Downloaded grant does not match its GitHub server receipt.");
            GitHubHandoffGrant? grant;
            try { grant = JsonSerializer.Deserialize<GitHubHandoffGrant>(grantBytes, JsonOptions); } catch (JsonException) { continue; }
            if (grant is null || !grant.IsValid || grant.SourceDeviceId != expectedTransfer.SourceDeviceId || grant.TargetDeviceId != expectedTransfer.TargetDeviceId || grant.TransferId != expectedTransfer.TransferId || grant.LineageId != expectedTransfer.LineageId || grant.Generation != expectedTransfer.Generation || grant.HandoffVersion != expectedTransfer.HandoffVersion) continue;
            candidates.Add((grant, grantAsset));
        }
        foreach (var (grant, grantAsset) in candidates.OrderByDescending(x => x.Grant.Generation).ThenByDescending(x => x.Grant.HandoffVersion))
        {
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
            // The target wrapper runs on the same device-local state directory
            // that may already contain this device's prior Released source
            // cursor (for example A -> B v1, then B -> A v2).  Supplying that
            // source store lets the centralized gate prove the exact successor
            // instead of treating the returning device as virgin.
            var localSourceStore = new DurableAuthorityStateStore(Path.Combine(stateDirectory, "source-authority.json"));
            var coordinator = new DirectedTargetAcquisitionCoordinator(
                new DurableTargetAcquisitionStore(Path.Combine(stateDirectory, "target-" + expectedTransfer.TargetDeviceId + ".json")),
                expectedTransfer.TargetDeviceId,
                [expectedTransfer.SourceDeviceId, expectedTransfer.TargetDeviceId],
                sourceStateStore: localSourceStore);
            return await coordinator.AcquireAsync(handoffDirectory, snapshotPath, expectedTransfer, cancellationToken);
        }
        return new(false, "missing-grant", "No exact target-bound GitHub grant was found.");
    }
}
