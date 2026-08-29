using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record GitHubHandoffRetentionResult(bool Succeeded, IReadOnlyList<long> DeletedAssetIds, string Message);

/// <summary>
/// Post-completion cleanup for complete GitHub snapshot+grant units only.
/// A deletion plan is persisted before the first DELETE so a grant-success /
/// snapshot-failure interruption is resumable and idempotent.
/// </summary>
public sealed class GitHubHandoffRetention(IGitHubHandoffTransport transport, string? cleanupPlanPath = null)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string? planPath = cleanupPlanPath is null ? null : Path.GetFullPath(cleanupPlanPath);
    private List<PlannedDeletion>? inMemoryPlan;

    private sealed record PlannedDeletion(long GrantAssetId, long SnapshotAssetId);
    private sealed record RetentionPlan(long ReleaseId, IReadOnlyList<PlannedDeletion> Deletions);
    private sealed record Unit(GitHubHandoffGrant Grant, GitHubRemoteAsset GrantAsset, GitHubRemoteAsset SnapshotAsset);

    public async Task<GitHubHandoffRetentionResult> CleanupAsync(
        GitHubReleaseContainer release,
        IReadOnlySet<long>? protectedAssetIds = null,
        CancellationToken cancellationToken = default)
    {
        var deleted = new List<long>();
        try
        {
            var plan = await LoadPlanAsync(release.Id, cancellationToken);
            if (plan is null)
            {
                var units = await EnumerateCompleteUnitsAsync(release, cancellationToken);
                // Authority ordering is protocol metadata: active generation first,
                // then monotonic handoff version. Filename timestamps never enter.
                var keep = units
                    .GroupBy(x => x.Grant.LineageId, StringComparer.Ordinal)
                    .SelectMany(group => group
                        .OrderByDescending(x => x.Grant.Generation)
                        .ThenByDescending(x => x.Grant.HandoffVersion)
                        .Skip(3))
                    .Where(x => protectedAssetIds is null
                        || !protectedAssetIds.Contains(x.GrantAsset.Id)
                        && !protectedAssetIds.Contains(x.SnapshotAsset.Id))
                    .Select(x => new PlannedDeletion(x.GrantAsset.Id, x.SnapshotAsset.Id))
                    .ToList();
                plan = new RetentionPlan(release.Id, keep);
                await SavePlanAsync(plan, cancellationToken);
            }

            foreach (var item in plan.Deletions.ToArray())
            {
                if (protectedAssetIds?.Contains(item.GrantAssetId) == true || protectedAssetIds?.Contains(item.SnapshotAssetId) == true)
                    continue;
                await DeleteIdempotentAsync(item.GrantAssetId, cancellationToken);
                deleted.Add(item.GrantAssetId);
                // Keep both IDs in the plan when this call fails: on retry the
                // already-deleted grant is a safe 404 and snapshot is retried.
                await DeleteIdempotentAsync(item.SnapshotAssetId, cancellationToken);
                deleted.Add(item.SnapshotAssetId);
                plan = plan with { Deletions = plan.Deletions.Where(x => x != item).ToArray() };
                await SavePlanAsync(plan, cancellationToken);
            }

            await ClearPlanAsync(cancellationToken);
            return new(true, deleted, "Retention cleanup retained the newest three complete units per lineage and is idempotently complete.");
        }
        catch (Exception exception) when (exception is IOException or GitHubTransportException or HttpRequestException or TaskCanceledException)
        {
            return new(false, deleted, "Retention cleanup is incomplete and will be retried; completed authority is unchanged.");
        }
    }

    private async Task<IReadOnlyList<Unit>> EnumerateCompleteUnitsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken)
    {
        var assets = await transport.ListAssetsAsync(release, cancellationToken);
        var byName = assets.Where(a => a.IsComplete)
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var units = new List<Unit>();
        foreach (var grantAsset in assets.Where(a => a.IsComplete && GitHubSnapshotName.IsGrant(a.Name)))
        {
            try
            {
                var bytes = await transport.DownloadAssetAsync(grantAsset.Id, cancellationToken);
                var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
                // A damaged/missing grant digest never becomes a destructive candidate.
                if (bytes.LongLength != grantAsset.Size || !GitHubDigest.IsValid(grantAsset.Digest)
                    || !digest.Equals(grantAsset.Digest, StringComparison.OrdinalIgnoreCase)) continue;
                var grant = JsonSerializer.Deserialize<GitHubHandoffGrant>(bytes, Options);
                if (grant is null || !grant.IsValid || !byName.TryGetValue(grant.SnapshotAssetName, out var snapshot) || snapshot is null
                    || snapshot.Id != grant.SnapshotAssetId || snapshot.Size != grant.SnapshotByteLength
                    || !snapshot.IsComplete || !snapshot.Digest.Equals("sha256:" + grant.SnapshotSha256, StringComparison.OrdinalIgnoreCase)) continue;
                units.Add(new Unit(grant, grantAsset, snapshot));
            }
            catch (JsonException) { }
        }
        return units;
    }

    private async Task<RetentionPlan?> LoadPlanAsync(long releaseId, CancellationToken cancellationToken)
    {
        if (planPath is null)
            return inMemoryPlan is null ? null : new RetentionPlan(releaseId, inMemoryPlan);
        if (!File.Exists(planPath)) return null;
        try
        {
            await using var stream = new FileStream(planPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            var plan = await JsonSerializer.DeserializeAsync<RetentionPlan>(stream, Options, cancellationToken);
            return plan is { ReleaseId: var id } && id == releaseId ? plan : null;
        }
        catch (JsonException exception) { throw new InvalidDataException("Retention cleanup plan is malformed; cleanup is blocked.", exception); }
    }

    private async Task SavePlanAsync(RetentionPlan plan, CancellationToken cancellationToken)
    {
        inMemoryPlan = plan.Deletions.ToList();
        if (planPath is null) return;
        var directory = Path.GetDirectoryName(planPath)!;
        Directory.CreateDirectory(directory);
        var temp = planPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, plan, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(true);
            }
            if (File.Exists(planPath)) File.Replace(temp, planPath, null, true); else File.Move(temp, planPath);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private async Task ClearPlanAsync(CancellationToken cancellationToken)
    {
        inMemoryPlan = null;
        await Task.CompletedTask.WaitAsync(cancellationToken);
        if (planPath is not null && File.Exists(planPath)) File.Delete(planPath);
    }

    private async Task DeleteIdempotentAsync(long assetId, CancellationToken cancellationToken)
    {
        try { await transport.DeleteAssetAsync(assetId, cancellationToken); }
        catch (GitHubTransportException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
    }
}
