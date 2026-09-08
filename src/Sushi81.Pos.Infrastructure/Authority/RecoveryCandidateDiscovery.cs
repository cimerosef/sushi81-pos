using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Recovery;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>
/// Discovers only complete, independently validated DR sources. It has no authority side
/// effects and never chooses a source by a wall-clock timestamp.
/// </summary>
public sealed class RecoveryCandidateDiscovery(
    IAppPaths paths,
    IGitHubHandoffTransport? githubTransport,
    string? configuredOneDriveRoot)
    : IRecoveryCandidateDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly string? oneDriveRoot = string.IsNullOrWhiteSpace(configuredOneDriveRoot)
        ? null
        : Path.GetFullPath(configuredOneDriveRoot);

    public async Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(
        AuthorityProtocolState localState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(localState);
        localState.Validate();
        if (localState.LineageId is not { } lineageId || localState.Generation < 1)
            return new(Array.Empty<RecoveryCandidate>(), "The local state is not bound to a recoverable lineage.");

        var candidates = new List<RecoveryCandidate>();
        var diagnostics = new List<string>();
        var temporarilyUnavailable = false;

        if (githubTransport is not null)
        {
            try
            {
                var github = await DiscoverGitHubAsync(localState, lineageId, cancellationToken);
                candidates.AddRange(github.Candidates);
                if (!string.IsNullOrWhiteSpace(github.Diagnostic)) diagnostics.Add(github.Diagnostic);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                temporarilyUnavailable = true;
                diagnostics.Add("GitHub handoff transport is unavailable.");
            }
            catch (Exception)
            {
                diagnostics.Add("GitHub handoff evidence was rejected by strict validation.");
            }
        }

        if (oneDriveRoot is not null)
        {
            try
            {
                candidates.AddRange(await DiscoverCheckpointsAsync(localState, lineageId, cancellationToken));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (IsUnavailable(exception))
            {
                temporarilyUnavailable = true;
                diagnostics.Add("OneDrive disaster-recovery checkpoints are unavailable.");
            }
            catch (Exception)
            {
                diagnostics.Add("OneDrive disaster-recovery evidence was rejected by strict validation.");
            }
        }

        var duplicateIds = candidates.GroupBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            return new(Array.Empty<RecoveryCandidate>(), "Contradictory duplicate recovery candidate identity was observed.");
        }

        var ordered = candidates
            .OrderByDescending(candidate => candidate.BusinessRevision)
            .ThenByDescending(candidate => candidate.HandoffVersion)
            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
            .ToArray();
        var diagnostic = ordered.Length == 0
            ? string.Join(" ", diagnostics.Prepend("No validated recovery candidate is available."))
            : string.Join(" ", diagnostics.Prepend($"Validated {ordered.Length} recovery candidate(s)."));
        return new(ordered, diagnostic, temporarilyUnavailable);
    }

    public async Task<RecoveryCandidate?> FindExactAsync(
        AuthorityProtocolState localState,
        string candidateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(candidateId)) return null;
        var discovered = await DiscoverAsync(localState, cancellationToken);
        return discovered.Candidates.SingleOrDefault(candidate =>
            string.Equals(candidate.CandidateId, candidateId, StringComparison.Ordinal));
    }

    private async Task<RecoveryCandidateDiscoveryResult> DiscoverGitHubAsync(
        AuthorityProtocolState localState,
        Guid lineageId,
        CancellationToken cancellationToken)
    {
        var release = await githubTransport!.EnsureContainerAsync(createIfMissing: false, cancellationToken);
        var assets = await githubTransport.ListAssetsAsync(release, cancellationToken);
        var candidates = new List<RecoveryCandidate>();
        foreach (var grantGroup in assets.Where(asset => GitHubHandoffAssetNames.IsGrantName(asset.Name))
            .GroupBy(asset => asset.Name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1))
        {
            var grantAsset = grantGroup.Single();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!grantAsset.IsComplete) continue;
                var grantBytes = await ReadRemoteBytesAsync(grantAsset, cancellationToken);
                var grant = JsonSerializer.Deserialize<NormalHandoffGrant>(grantBytes, JsonOptions)
                    ?? throw new InvalidDataException("The grant artifact is empty.");
                grant.Validate();
                var grantDigest = Convert.ToHexString(SHA256.HashData(grantBytes));
                if (!GitHubSha256.TryNormalizeServerDigest(grantAsset.Digest, out var remoteGrantDigest)
                    || !string.Equals(grantDigest, remoteGrantDigest[7..], StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The grant digest is not exact.");
                if (grant.SnapshotReceipt.ReleaseId != release.Id
                    || grant.LineageId != lineageId || grant.Generation != localState.Generation
                    || grant.SourceDeviceId == grant.TargetDeviceId)
                    continue;

                if (assets.Count(asset => string.Equals(asset.Name, grant.SnapshotReceipt.Name, StringComparison.Ordinal)) != 1)
                    continue;
                var snapshot = assets.SingleOrDefault(asset => asset.Id == grant.SnapshotReceipt.AssetId);
                if (snapshot is null || !snapshot.IsComplete
                    || !string.Equals(snapshot.Name, grant.SnapshotReceipt.Name, StringComparison.Ordinal)
                    || snapshot.Size != grant.SnapshotReceipt.Size
                    || !string.Equals(snapshot.Digest, "sha256:" + grant.SnapshotReceipt.Sha256, StringComparison.OrdinalIgnoreCase))
                    continue;

                var candidateId = $"github-handoff:{grant.TransferId:N}:v{grant.HandoffVersion}:grant-{grantAsset.Id}:snapshot-{snapshot.Id}";
                var snapshotBytes = await ReadRemoteBytesAsync(snapshot, cancellationToken);
                var snapshotDigest = Convert.ToHexString(SHA256.HashData(snapshotBytes));
                if (snapshotBytes.LongLength != snapshot.Size
                    || !string.Equals(snapshotDigest, grant.SnapshotReceipt.Sha256, StringComparison.OrdinalIgnoreCase))
                    continue;
                var stagingPath = Path.Combine(paths.TempDirectory, $"m07-candidate-{Guid.NewGuid():N}.db");
                try
                {
                    paths.EnsureInitialized();
                    await File.WriteAllBytesAsync(stagingPath, snapshotBytes, cancellationToken);
                    await ValidateSqliteAsync(stagingPath, cancellationToken);
                    if (await SqliteBusinessRevisionStore.ReadFromDatabaseAsync(stagingPath, cancellationToken) != grant.BusinessRevision)
                        continue;
                }
                finally
                {
                    if (File.Exists(stagingPath)) File.Delete(stagingPath);
                }

                var candidate = new RecoveryCandidate(
                    RecoveryCandidateType.GitHubHandoff,
                    candidateId,
                    grant.LineageId,
                    grant.Generation,
                    grant.SourceDeviceId,
                    grant.BusinessRevision,
                    grant.HandoffVersion,
                    grant.CreatedAtUtc,
                    snapshot.Size,
                    grant.SnapshotReceipt.Sha256,
                    $"release:{release.Id}:snapshot:{snapshot.Id}:grant:{grantAsset.Id}",
                    snapshot.Id,
                    grantAsset.Id);
                candidate.Validate();
                candidates.Add(candidate);
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidDataException) { }
            catch (JsonException) { }
        }

        return new(candidates, candidates.Count == 0 ? "No complete GitHub handoff+grant unit was validated." : string.Empty);
    }

    private async Task<IReadOnlyList<RecoveryCandidate>> DiscoverCheckpointsAsync(
        AuthorityProtocolState localState,
        Guid lineageId,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(oneDriveRoot!, "DisasterRecovery", "Checkpoints", localState.Generation.ToString(CultureInfo.InvariantCulture));
        if (!Directory.Exists(root)) return Array.Empty<RecoveryCandidate>();
        var candidates = new List<RecoveryCandidate>();
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var metadataPath = Path.Combine(directory, "metadata.json");
                await using var stream = new FileStream(metadataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
                var metadata = await JsonSerializer.DeserializeAsync<RecoveryCheckpointMetadata>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidDataException("The checkpoint metadata is empty.");
                metadata.Validate();
                if (metadata.LineageId != lineageId || metadata.Generation != localState.Generation
                    || !string.Equals(Path.GetFileName(directory), metadata.CheckpointId.ToString("N"), StringComparison.OrdinalIgnoreCase))
                    continue;
                var databasePath = Path.Combine(directory, metadata.DatabaseFileName);
                if (!File.Exists(databasePath) || new FileInfo(databasePath).Length != metadata.DatabaseSize)
                    continue;
                var digest = await ComputeSha256Async(databasePath, cancellationToken);
                if (!string.Equals(digest, metadata.DatabaseSha256, StringComparison.OrdinalIgnoreCase)) continue;
                await ValidateSqliteAsync(databasePath, cancellationToken);
                if (await SqliteBusinessRevisionStore.ReadFromDatabaseAsync(databasePath, cancellationToken) != metadata.BusinessRevision)
                    continue;

                var candidate = new RecoveryCandidate(
                    RecoveryCandidateType.OneDriveCheckpoint,
                    $"onedrive-checkpoint:{metadata.CheckpointId:N}:h{metadata.HandoffVersion}",
                    metadata.LineageId,
                    metadata.Generation,
                    metadata.SourceDeviceId,
                    metadata.BusinessRevision,
                    metadata.HandoffVersion,
                    metadata.CreatedAtUtc,
                    metadata.DatabaseSize,
                    metadata.DatabaseSha256,
                    databasePath);
                candidate.Validate();
                candidates.Add(candidate);
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException) { }
            catch (InvalidDataException) { }
            catch (JsonException) { }
            catch (SqliteException) { }
        }

        return candidates;
    }

    private async Task<byte[]> ReadRemoteBytesAsync(GitHubRemoteAsset asset, CancellationToken cancellationToken)
    {
        await using var stream = await githubTransport!.DownloadAssetAsync(asset.Id, cancellationToken);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.LongLength != asset.Size) throw new InvalidDataException("The remote asset size is not exact.");
        return bytes;
    }

    private static async Task ValidateSqliteAsync(string path, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Private, Pooling = false
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(Convert.ToString(await integrity.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The recovery candidate failed SQLite integrity validation.");
        await using var schema = connection.CreateCommand();
        schema.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='schema_migrations';";
        if (Convert.ToInt32(await schema.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 1)
            throw new InvalidDataException("The recovery candidate lacks the application schema marker.");
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    private static bool IsUnavailable(Exception exception) => exception is
        SystemMetadataUnavailableException or DirectoryNotFoundException or FileNotFoundException
        or UnauthorizedAccessException or HttpRequestException or TimeoutException;
}
