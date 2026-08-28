using System.Text.Json;

namespace Sushi81.Pos.OneDriveFeasibility;

public sealed record GitHubHandoffRetentionResult(bool Succeeded, IReadOnlyList<long> DeletedAssetIds, string Message);

/// <summary>Post-completion cleanup for complete GitHub snapshot+grant units only.</summary>
public sealed class GitHubHandoffRetention(IGitHubHandoffTransport transport)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.General);

    public async Task<GitHubHandoffRetentionResult> CleanupAsync(
        GitHubReleaseContainer release,
        IReadOnlySet<long>? protectedAssetIds = null,
        CancellationToken cancellationToken = default)
    {
        var assets = await transport.ListAssetsAsync(release, cancellationToken);
        var byName = assets.Where(a => a.IsComplete).ToDictionary(a => a.Name, StringComparer.Ordinal);
        var units = new List<(GitHubHandoffGrant Grant, GitHubRemoteAsset GrantAsset, GitHubRemoteAsset SnapshotAsset)>();
        foreach (var grantAsset in assets.Where(a => a.IsComplete && GitHubSnapshotName.IsGrant(a.Name)))
        {
            try
            {
                var grant = JsonSerializer.Deserialize<GitHubHandoffGrant>(await transport.DownloadAssetAsync(grantAsset.Id, cancellationToken), Options);
                if (grant is null || !grant.IsValid || !byName.TryGetValue(grant.SnapshotAssetName, out var snapshot) || snapshot.Id != grant.SnapshotAssetId || snapshot.Size != grant.SnapshotByteLength || !snapshot.Digest.Equals("sha256:" + grant.SnapshotSha256, StringComparison.OrdinalIgnoreCase)) continue;
                units.Add((grant, grantAsset, snapshot));
            }
            catch (JsonException) { }
        }
        var keep = units.OrderByDescending(x => x.Grant.HandoffVersion).ThenByDescending(x => x.Grant.Generation).Take(3).ToHashSet();
        var deleted = new List<long>();
        try
        {
            foreach (var unit in units.Where(x => !keep.Contains(x)))
            {
                if (protectedAssetIds?.Contains(unit.GrantAsset.Id) == true || protectedAssetIds?.Contains(unit.SnapshotAsset.Id) == true) continue;
                await transport.DeleteAssetAsync(unit.GrantAsset.Id, cancellationToken); deleted.Add(unit.GrantAsset.Id);
                await transport.DeleteAssetAsync(unit.SnapshotAsset.Id, cancellationToken); deleted.Add(unit.SnapshotAsset.Id);
            }
            return new(true, deleted, "Retention cleanup retained the newest three complete validated handoff units.");
        }
        catch (Exception exception) when (exception is IOException or GitHubTransportException or HttpRequestException or TaskCanceledException)
        {
            return new(false, deleted, "Retention cleanup is incomplete and will be retried; completed authority is unchanged.");
        }
    }
}
