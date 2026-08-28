using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Sushi81.Pos.OneDriveFeasibility.Tests;

[TestClass]
public sealed class GitHubTransportTests
{
    [TestMethod]
    public void SnapshotNamesAreStrictAndGrantBasenamesMatch()
    {
        Assert.AreEqual("20260829235959.snapshot.db", GitHubSnapshotName.Create(new DateTimeOffset(2026, 8, 29, 23, 59, 59, TimeSpan.FromHours(2))));
        Assert.IsTrue(GitHubSnapshotName.IsValid("20260829235959.snapshot.db"));
        Assert.IsFalse(GitHubSnapshotName.IsValid("2026082923595.snapshot.db"));
        Assert.IsFalse(GitHubSnapshotName.IsValid("20260829235959.SNAPSHOT.DB"));
        Assert.AreEqual("20260829235959.grant.json", GitHubSnapshotName.GrantName("20260829235959.snapshot.db"));
    }

    [TestMethod]
    public async Task HttpUploadRequiresCreatedCompleteExactDigestAndSize()
    {
        var bytes = Encoding.UTF8.GetBytes("synthetic");
        var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/repos/acme/handoff")
                return Json(HttpStatusCode.OK, "{\"private\":true,\"visibility\":\"private\"}");
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/releases/tags/sushi81-handoff-v1", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, "{\"id\":9,\"tag_name\":\"sushi81-handoff-v1\",\"upload_url\":\"https://uploads.example/releases/9/assets{?name,label}\",\"html_url\":\"https://example/release\",\"draft\":false,\"prerelease\":false}");
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/releases/9/assets")
                return Json(HttpStatusCode.Created, $"{{\"id\":44,\"name\":\"20260829235959.snapshot.db\",\"size\":{bytes.Length},\"state\":\"uploaded\",\"digest\":\"{digest}\",\"created_at\":\"2026-08-29T22:00:00Z\"}}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var dir = NewDirectory(); var path = Path.Combine(dir, "local.db"); await File.WriteAllBytesAsync(path, bytes);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "test-secret");
        var receipt = await transport.UploadAssetAsync(new GitHubReleaseContainer(9, "sushi81-handoff-v1", "https://uploads.example/releases/9/assets{?name,label}", "https://example/release", false, false), "20260829235959.snapshot.db", path);
        Assert.AreEqual(44, receipt.AssetId); Assert.AreEqual(digest, receipt.Digest);
    }

    [TestMethod]
    public async Task HttpUploadMissingDigestFailsClosed()
    {
        var handler = new StubHandler((request, _) => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Created, "{\"id\":44,\"name\":\"20260829235959.snapshot.db\",\"size\":1,\"state\":\"uploaded\"}")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var dir = NewDirectory(); var path = Path.Combine(dir, "x"); await File.WriteAllBytesAsync(path, [1]);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var failed = false;
        try { await transport.UploadAssetAsync(new GitHubReleaseContainer(9, "tag", "https://uploads.example/assets", "", false, false), "20260829235959.snapshot.db", path); }
        catch (GitHubTransportException) { failed = true; }
        Assert.IsTrue(failed);
    }

    [TestMethod]
    public async Task PublicRepositoryIsRejectedWithoutLeakingToken()
    {
        var handler = new StubHandler((_, _) => Json(HttpStatusCode.OK, "{\"private\":false,\"visibility\":\"public\"}"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var failed = false;
        try { await transport.EnsureContainerAsync(false); }
        catch (GitHubTransportException ex) { failed = true; Assert.IsFalse(ex.Message.Contains("secret-token", StringComparison.Ordinal)); }
        Assert.IsTrue(failed);
    }

    [TestMethod]
    public async Task SourceAndExactTargetUseGitHubReceiptsAndDurableGate()
    {
        var transport = new FakeTransport();
        var sourceDir = NewDirectory();
        var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 7, 1, "device-a", "device-b");
        var source = await new GitHubDirectedSourceCoordinator(sourceDir, transport, () => new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero)).RunAsync(transfer);
        Assert.IsTrue(source.Succeeded, source.Message); Assert.AreEqual(DirectedAuthorityMode.Released, source.State!.Mode);
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(Path.Combine(sourceDir, "source-authority.json"))).MayBusinessWrite("device-a"));
        Assert.IsTrue(transport.UploadedNames[0].EndsWith(".snapshot.db", StringComparison.Ordinal));
        Assert.IsTrue(transport.UploadedNames[1].EndsWith(".grant.json", StringComparison.Ordinal));
        var target = await new GitHubDirectedTargetCoordinator(NewDirectory(), transport).AcquireAsync(transfer);
        Assert.IsTrue(target.Succeeded, target.Message);
    }

    [TestMethod]
    public async Task RetentionDeletesOnlyTheOldestCompletePairAfterFourthSuccess()
    {
        var transport = new FakeTransport();
        for (var version = 1; version <= 4; version++) await transport.SeedUnitAsync(version);
        var result = await new GitHubHandoffRetention(transport).CleanupAsync(new GitHubReleaseContainer(9, "sushi81-handoff-v1", "", "", false, false));
        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(2, result.DeletedAssetIds);
        Assert.HasCount(6, await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false)));
    }

    private static string NewDirectory() { var path = Path.Combine(Path.GetTempPath(), "sushi81-github-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request, cancellationToken));
    }

    private sealed class FakeTransport : IGitHubHandoffTransport
    {
        private readonly GitHubReleaseContainer release = new(9, "sushi81-handoff-v1", "https://uploads.example/assets", "", false, false);
        private readonly Dictionary<long, (GitHubRemoteAsset Asset, byte[] Bytes)> assets = new();
        private long nextId = 100;
        public List<string> UploadedNames { get; } = [];
        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromResult(release);
        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, string filePath, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken); var id = ++nextId; var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var asset = new GitHubRemoteAsset(id, name, bytes.Length, "uploaded", digest, "", DateTimeOffset.UtcNow); assets[id] = (asset, bytes); UploadedNames.Add(name);
            return new GitHubAssetReceipt(release.Id, id, name, bytes.Length, digest, DateTimeOffset.UtcNow);
        }
        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.Values.Select(x => x.Asset).ToArray());
        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult(assets[assetId].Asset);
        public Task<byte[]> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult(assets[assetId].Bytes);
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) { assets.Remove(assetId); return Task.CompletedTask; }

        public async Task SeedUnitAsync(long version)
        {
            var directory = NewDirectory(); var snapshotName = $"20260829{version:000000}.snapshot.db"; var snapshotPath = Path.Combine(directory, snapshotName); await File.WriteAllBytesAsync(snapshotPath, Encoding.UTF8.GetBytes("snapshot-" + version));
            var snapshotReceipt = await UploadAssetAsync(release, snapshotName, snapshotPath);
            var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, version, "a", "b");
            var grant = new GitHubHandoffGrant(1, transfer.TransferId, transfer.LineageId, 1, version, "a", "b", release.Id, snapshotReceipt.AssetId, snapshotReceipt.Name, snapshotReceipt.Size, snapshotReceipt.Digest[7..], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var grantPath = Path.Combine(directory, GitHubSnapshotName.GrantName(snapshotName)); await File.WriteAllTextAsync(grantPath, System.Text.Json.JsonSerializer.Serialize(grant)); await UploadAssetAsync(release, GitHubSnapshotName.GrantName(snapshotName), grantPath);
        }
    }
}
