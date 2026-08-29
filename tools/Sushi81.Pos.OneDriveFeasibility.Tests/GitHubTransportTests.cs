using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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
    public void MissingTokenFailsBeforeAnyRequest()
    {
        using var client = new HttpClient { BaseAddress = new Uri("https://api.example/") };
        var exception = ExpectTransportFailureAsync(() =>
        {
            _ = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "");
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();
        Assert.IsTrue(exception.Message.Contains("not set", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task UploadMetadataContradictionsFailClosed()
    {
        var variants = new[]
        {
            "{\"id\":44,\"name\":\"wrong.snapshot.db\",\"size\":1,\"state\":\"uploaded\",\"digest\":\"sha256:" + new string('a', 64) + "\"}",
            "{\"id\":44,\"name\":\"20260829235959.snapshot.db\",\"size\":2,\"state\":\"uploaded\",\"digest\":\"sha256:" + new string('a', 64) + "\"}",
            "{\"id\":44,\"name\":\"20260829235959.snapshot.db\",\"size\":1,\"state\":\"starter\",\"digest\":\"sha256:" + new string('a', 64) + "\"}",
            "{\"id\":44,\"name\":\"20260829235959.snapshot.db\",\"size\":1,\"state\":\"uploaded\",\"digest\":\"sha256:" + new string('b', 64) + "\"}"
        };
        foreach (var variant in variants)
        {
            var handler = new StubHandler((request, _) => request.Method == HttpMethod.Post
                ? Json(HttpStatusCode.Created, variant)
                : new HttpResponseMessage(HttpStatusCode.OK));
            var dir = NewDirectory(); var path = Path.Combine(dir, "local"); await File.WriteAllBytesAsync(path, [1]);
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
            using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
            var exception = await ExpectTransportFailureAsync(() => transport.UploadAssetAsync(new GitHubReleaseContainer(9, "tag", "https://uploads.example/assets", "", false, false), "20260829235959.snapshot.db", path));
            Assert.IsTrue(exception.Message.Contains("receipt", StringComparison.OrdinalIgnoreCase));
        }
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
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.HasCount(2, result.DeletedAssetIds);
        Assert.HasCount(6, await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false)));
    }

    [TestMethod]
    public async Task CreateReleaseSendsDocumentedStringMakeLatestFalse()
    {
        string? requestBody = null;
        var handler = new StubHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/repos/acme/handoff")
                return Json(HttpStatusCode.OK, "{\"private\":true,\"visibility\":\"private\"}");
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("/releases/tags/", StringComparison.Ordinal))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath.EndsWith("/releases", StringComparison.Ordinal))
            {
                requestBody = request.Content!.ReadAsStringAsync(CancellationToken.None).GetAwaiter().GetResult();
                return Json(HttpStatusCode.Created, "{\"id\":9,\"tag_name\":\"sushi81-handoff-v1\",\"upload_url\":\"https://uploads.example/releases/9/assets{?name,label}\",\"html_url\":\"https://example/release\",\"draft\":false,\"prerelease\":false}");
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var release = await transport.EnsureContainerAsync(true);
        using var document = JsonDocument.Parse(requestBody!);
        Assert.AreEqual(JsonValueKind.String, document.RootElement.GetProperty("make_latest").ValueKind);
        Assert.AreEqual("false", document.RootElement.GetProperty("make_latest").GetString());
        Assert.AreEqual(9, release.Id);
    }

    [TestMethod]
    public async Task CreateReleaseValidation422FailsClosed()
    {
        var handler = new StubHandler((request, _) => request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath == "/repos/acme/handoff"
            ? Json(HttpStatusCode.OK, "{\"private\":true}")
            : request.Method == HttpMethod.Get ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(HttpStatusCode.UnprocessableEntity, "{\"message\":\"validation failed\"}"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        await ExpectTransportFailureAsync(() => transport.EnsureContainerAsync(true));
    }

    [TestMethod]
    public async Task MissingRepositoryFailsClosed()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var exception = await ExpectTransportFailureAsync(() => transport.EnsureContainerAsync(false));
        Assert.AreEqual(HttpStatusCode.NotFound, exception.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticationPermissionAndRateLimitFailuresAreStructured()
    {
        foreach (var status in new[] { 401, 403, 429 })
        {
            var handler = new StubHandler((_, _) => new HttpResponseMessage((HttpStatusCode)status));
            using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
            using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
            var exception = await ExpectTransportFailureAsync(() => transport.EnsureContainerAsync(false));
            Assert.IsFalse(exception.Message.Contains("secret-token", StringComparison.Ordinal));
            Assert.AreEqual((HttpStatusCode)status, exception.StatusCode);
        }
    }

    [TestMethod]
    public async Task ListAssetsReadsPaginationBeyondTheFirstHundred()
    {
        var handler = new StubHandler((request, _) =>
        {
            var page = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            var count = page == 1 ? 100 : 1;
            var items = Enumerable.Range(0, count).Select(index => $"{{\"id\":{1000 + page * 100 + index},\"name\":\"asset-{page}-{index}\",\"size\":1,\"state\":\"uploaded\",\"digest\":\"sha256:{new string('a', 64)}\"}}");
            return Json(HttpStatusCode.OK, "[" + string.Join(',', items) + "]");
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var assets = await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "https://uploads.example", "", false, false));
        Assert.HasCount(101, assets);
        Assert.IsTrue(assets.Any(asset => asset.Name == "asset-2-0"));
    }

    [TestMethod]
    public void SourceRepositoryCannotBeConfiguredAsOperationalStore() =>
        Assert.IsFalse(new GitHubHandoffTransportOptions("cimerosef", "sushi81-pos").IsValid);

    [TestMethod]
    public async Task TargetWrongSourceGrantCannotMutateDurableState()
    {
        var transport = new FakeTransport();
        var actual = await transport.SeedUnitAsync(1, source: "wrong-source");
        var expected = actual with { SourceDeviceId = "device-a" };
        var targetDir = NewDirectory();
        var result = await new GitHubDirectedTargetCoordinator(targetDir, transport).AcquireAsync(expected);
        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories).Any(file => file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task RetentionPrioritizesNewGenerationWithinOneLineage()
    {
        var transport = new FakeTransport();
        await transport.SeedUnitAsync(98, generation: 1);
        await transport.SeedUnitAsync(99, generation: 1);
        await transport.SeedUnitAsync(100, generation: 1);
        await transport.SeedUnitAsync(1, generation: 2);
        var result = await new GitHubHandoffRetention(transport).CleanupAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false));
        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(2, result.DeletedAssetIds);
        Assert.IsFalse((await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false))).Any(x => x.Name.StartsWith("20260829000098", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task RetentionKeepsExactlyThreeUnitsAcrossLineages()
    {
        var transport = new FakeTransport();
        for (var version = 1; version <= 4; version++) await transport.SeedUnitAsync(version, lineage: "11111111-1111-1111-1111-111111111111");
        await transport.SeedUnitAsync(1, lineage: "22222222-2222-2222-2222-222222222222", timestamp: "20260901000001");
        var result = await new GitHubHandoffRetention(transport).CleanupAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false));
        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(4, result.DeletedAssetIds);
        Assert.HasCount(6, await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false)));
    }

    [TestMethod]
    public async Task GitHubWrappersSupportContinuousAbv1BAv2Abv3AndReplaceStaleReceipt()
    {
        var transport = new FakeTransport();
        var stateA = NewDirectory();
        var stateB = NewDirectory();
        var lineage = Guid.NewGuid().ToString();
        const long generation = 7;
        DateTimeOffset Clock() => new(2026, 8, 29, 12, 0, 0, TimeSpan.Zero);

        var v1 = new DirectedTransferIdentity(Guid.NewGuid().ToString(), lineage, generation, 1, "device-a", "device-b");
        var sourceV1 = await new GitHubDirectedSourceCoordinator(stateA, transport, Clock).RunAsync(v1);
        Assert.IsTrue(sourceV1.Succeeded, sourceV1.Code + ": " + sourceV1.Message);
        var targetV1 = await new GitHubDirectedTargetCoordinator(stateB, transport).AcquireAsync(v1);
        Assert.IsTrue(targetV1.Succeeded, targetV1.Code + ": " + targetV1.Message);
        var promoteB = new DirectedContinuousLifecycleCoordinator(null, "device-b")
            .PromoteAcquiredTargetToSource(new DurableTargetAcquisitionStore(Path.Combine(stateB, "target-device-b.json")).Load(), Path.Combine(stateB, "source-authority.json"), ["device-a", "device-b"]);
        Assert.IsTrue(promoteB.Succeeded, promoteB.Code + ": " + promoteB.Message);

        var v2 = new DirectedTransferIdentity(Guid.NewGuid().ToString(), lineage, generation, 2, "device-b", "device-a");
        var sourceV2 = await new GitHubDirectedSourceCoordinator(stateB, transport, Clock).RunAsync(v2);
        Assert.IsTrue(sourceV2.Succeeded, sourceV2.Code + ": " + sourceV2.Message);
        var targetV2 = await new GitHubDirectedTargetCoordinator(stateA, transport).AcquireAsync(v2);
        Assert.IsTrue(targetV2.Succeeded, targetV2.Code + ": " + targetV2.Message);
        var promoteA = new DirectedContinuousLifecycleCoordinator(null, "device-a")
            .PromoteAcquiredTargetToSource(new DurableTargetAcquisitionStore(Path.Combine(stateA, "target-device-a.json")).Load(), Path.Combine(stateA, "source-authority.json"), ["device-a", "device-b"]);
        Assert.IsTrue(promoteA.Succeeded, promoteA.Code + ": " + promoteA.Message);

        var v3 = new DirectedTransferIdentity(Guid.NewGuid().ToString(), lineage, generation, 3, "device-a", "device-b");
        var sourceV3 = await new GitHubDirectedSourceCoordinator(stateA, transport, Clock).RunAsync(v3);
        Assert.IsTrue(sourceV3.Succeeded, sourceV3.Code + ": " + sourceV3.Message);
        Assert.AreEqual(DirectedAuthorityMode.Released, new DurableAuthorityStateStore(Path.Combine(stateA, "source-authority.json")).Load().Mode);
        using (var receiptDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(stateA, "github-snapshot-receipt.json"))))
        {
            Assert.AreEqual(v3.TransferId, receiptDocument.RootElement.GetProperty("TransferId").GetString());
        }
        var targetV3 = await new GitHubDirectedTargetCoordinator(stateB, transport).AcquireAsync(v3);
        Assert.IsTrue(targetV3.Succeeded, targetV3.Code + ": " + targetV3.Message);
        var snapshotNames = transport.UploadedNames.Where(GitHubSnapshotName.IsValid).ToArray();
        Assert.HasCount(3, snapshotNames.Distinct(StringComparer.Ordinal));
        Assert.IsTrue(new DirectedTargetAcquisitionCoordinator(
            new DurableTargetAcquisitionStore(Path.Combine(stateB, "target-device-b.json")),
            "device-b",
            ["device-a", "device-b"],
            sourceStateStore: new DurableAuthorityStateStore(Path.Combine(stateB, "source-authority.json")))
            .MayBusinessWrite(v3));
    }

    [TestMethod]
    public async Task GrantUploadFailureAfterRelinquishmentIsRetryable()
    {
        var transport = new FakeTransport { FailGrantUploadOnce = true };
        var directory = NewDirectory();
        var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "device-a", "device-b");
        var first = await new GitHubDirectedSourceCoordinator(directory, transport, () => new DateTimeOffset(2026, 8, 29, 12, 0, 10, TimeSpan.Zero)).RunAsync(transfer);
        Assert.IsFalse(first.Succeeded);
        Assert.AreEqual("grant-upload-failed", first.Code);
        Assert.AreEqual(DirectedAuthorityMode.RelinquishedBlocked, new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json")).Load().Mode);
        var retry = await new GitHubDirectedSourceCoordinator(directory, transport).RunAsync(transfer);
        Assert.IsTrue(retry.Succeeded, retry.Code + ": " + retry.Message);
        Assert.HasCount(2, transport.UploadedNames);
    }

    [TestMethod]
    public async Task GrantAlreadyUploadedBeforeReleasedCommitIsRecoveredWithoutDuplicate()
    {
        var transport = new FakeTransport();
        var directory = NewDirectory();
        var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "device-a", "device-b");
        var crashing = new GitHubDirectedSourceCoordinator(directory, transport, stateFailureInjector: new ReleaseCommitFailureInjector());
        var first = await crashing.RunAsync(transfer);
        Assert.IsFalse(first.Succeeded);
        Assert.AreEqual("durable-write-failed", first.Code);
        Assert.AreEqual(DirectedAuthorityMode.RelinquishedBlocked, new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json")).Load().Mode);
        var retry = await new GitHubDirectedSourceCoordinator(directory, transport).RunAsync(transfer);
        Assert.IsTrue(retry.Succeeded, retry.Code + ": " + retry.Message);
        Assert.HasCount(2, transport.UploadedNames);
    }

    [TestMethod]
    public async Task TargetDownloadTruncationFailsBeforeDurableMutation()
    {
        var transport = new FakeTransport();
        var transfer = await transport.SeedUnitAsync(1);
        transport.TruncateBytesPreservingReceipt("20260829000001.snapshot.db");
        var directory = NewDirectory();
        var result = await new GitHubDirectedTargetCoordinator(directory, transport).AcquireAsync(transfer);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("snapshot-hash-mismatch", result.Code);
        Assert.IsFalse(File.Exists(Path.Combine(directory, "target-device-b.json")));
    }

    [TestMethod]
    public async Task TargetCorruptSqliteFailsAfterRemoteHashValidation()
    {
        var transport = new FakeTransport();
        var transfer = await transport.SeedUnitAsync(1);
        transport.ReplaceSnapshotAndGrantWithCorruptSqlite("20260829000001.snapshot.db");
        var directory = NewDirectory();
        var result = await new GitHubDirectedTargetCoordinator(directory, transport).AcquireAsync(transfer);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual("sqlite-integrity-failure", result.Code);
        Assert.IsFalse(File.Exists(Path.Combine(directory, "target-device-b.json")));
    }

    [TestMethod]
    public async Task AssetDuplicateName422FailsClosed()
    {
        var handler = new StubHandler((request, _) => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.UnprocessableEntity, "{\"message\":\"already_exists\"}")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var directory = NewDirectory();
        var path = Path.Combine(directory, "20260829000001.snapshot.db");
        await File.WriteAllBytesAsync(path, [1]);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var exception = await ExpectTransportFailureAsync(() => transport.UploadAssetAsync(new GitHubReleaseContainer(9, "tag", "https://uploads.example/assets", "", false, false), Path.GetFileName(path), path));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
    }

    [TestMethod]
    public async Task UpstreamBadGatewayFailsClosedWithoutTreatingStarterAsComplete()
    {
        var handler = new StubHandler((request, _) => request.Method == HttpMethod.Post
            ? Json(HttpStatusCode.BadGateway, "{\"message\":\"upstream\"}")
            : new HttpResponseMessage(HttpStatusCode.OK));
        var directory = NewDirectory();
        var path = Path.Combine(directory, "20260829000001.snapshot.db");
        await File.WriteAllBytesAsync(path, [1]);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var exception = await ExpectTransportFailureAsync(() => transport.UploadAssetAsync(new GitHubReleaseContainer(9, "tag", "https://uploads.example/assets", "", false, false), Path.GetFileName(path), path));
        Assert.AreEqual(HttpStatusCode.BadGateway, exception.StatusCode);
    }

    [TestMethod]
    public async Task CorruptGrantReceiptNeverBecomesDeletionCandidate()
    {
        var transport = new FakeTransport();
        for (var version = 1; version <= 4; version++) await transport.SeedUnitAsync(version);
        transport.CorruptDigest("20260829000001.grant.json");
        var result = await new GitHubHandoffRetention(transport).CleanupAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false));
        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue((await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false))).Any(x => x.Name == "20260829000001.snapshot.db"));
    }

    [TestMethod]
    public async Task PartialRetentionDeletionIsResumableAndIdempotent()
    {
        var transport = new FakeTransport { FailSnapshotDeletionOnce = true };
        for (var version = 1; version <= 4; version++) await transport.SeedUnitAsync(version);
        var planPath = Path.Combine(NewDirectory(), "retention-plan.json");
        var retention = new GitHubHandoffRetention(transport, planPath);
        var first = await retention.CleanupAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false));
        Assert.IsFalse(first.Succeeded);
        Assert.IsFalse((await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false))).Any(x => x.Name == "20260829000001.grant.json"));
        Assert.IsTrue((await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false))).Any(x => x.Name == "20260829000001.snapshot.db"));
        var second = await retention.CleanupAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false));
        Assert.IsTrue(second.Succeeded);
        var third = await retention.CleanupAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false));
        Assert.IsTrue(third.Succeeded);
        Assert.IsFalse((await transport.ListAssetsAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false))).Any(x => x.Name.Contains("000001", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SourceRestartRejectsChangedLocalSnapshotBeforeRelinquishment()
    {
        var transport = new FakeTransport();
        var directory = NewDirectory();
        var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "device-a", "device-b");
        var coordinator = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json")));
        Assert.IsTrue(coordinator.InitializeAuthoritative("device-a", ["device-a", "device-b"]).Succeeded);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = Path.Combine(directory, "20260829120000.snapshot.db");
        await DirectedSnapshotEvidence.CreateSyntheticAsync(snapshot);
        var receipt = await transport.UploadAssetAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false), Path.GetFileName(snapshot), snapshot);
        await File.WriteAllTextAsync(Path.Combine(directory, "github-snapshot-receipt.json"), JsonSerializer.Serialize(new { TransferId = transfer.TransferId, Receipt = receipt }));
        await File.AppendAllTextAsync(snapshot, "changed");
        var result = await new GitHubDirectedSourceCoordinator(directory, transport, () => new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero)).RunAsync(transfer);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.TransferPrepared, new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json")).Load().Mode);
        Assert.IsFalse(transport.UploadedNames.Any(name => GitHubSnapshotName.IsGrant(name)));
    }

    [TestMethod]
    public async Task SourceRestartRevalidatesPersistedReceiptAndResumesSameTransfer()
    {
        var transport = new FakeTransport();
        var directory = NewDirectory();
        var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "device-a", "device-b");
        var coordinator = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json")));
        Assert.IsTrue(coordinator.InitializeAuthoritative("device-a", ["device-a", "device-b"]).Succeeded);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = Path.Combine(directory, "20260829120001.snapshot.db");
        await DirectedSnapshotEvidence.CreateSyntheticAsync(snapshot);
        var receipt = await transport.UploadAssetAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false), Path.GetFileName(snapshot), snapshot);
        await File.WriteAllTextAsync(Path.Combine(directory, "github-snapshot-receipt.json"), JsonSerializer.Serialize(new { TransferId = transfer.TransferId, Receipt = receipt }));
        var result = await new GitHubDirectedSourceCoordinator(directory, transport, () => new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero)).RunAsync(transfer);
        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual(DirectedAuthorityMode.Released, result.State!.Mode);
        Assert.HasCount(2, transport.UploadedNames);
        Assert.IsFalse(new DirectedHandoffCoordinator(new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json"))).MayBusinessWrite("device-a"));
    }

    [TestMethod]
    public async Task SourceRestartMissingRemoteReceiptStaysPrepared()
    {
        var transport = new FakeTransport();
        var directory = NewDirectory();
        var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), 1, 1, "device-a", "device-b");
        var coordinator = new DirectedHandoffCoordinator(new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json")));
        Assert.IsTrue(coordinator.InitializeAuthoritative("device-a", ["device-a", "device-b"]).Succeeded);
        Assert.IsTrue(coordinator.PrepareTransfer(transfer).Succeeded);
        var snapshot = Path.Combine(directory, "20260829120002.snapshot.db");
        await DirectedSnapshotEvidence.CreateSyntheticAsync(snapshot);
        var receipt = await transport.UploadAssetAsync(new GitHubReleaseContainer(9, "tag", "", "", false, false), Path.GetFileName(snapshot), snapshot);
        await File.WriteAllTextAsync(Path.Combine(directory, "github-snapshot-receipt.json"), JsonSerializer.Serialize(new { TransferId = transfer.TransferId, Receipt = receipt }));
        transport.RemoveAsset(receipt.AssetId);
        var result = await new GitHubDirectedSourceCoordinator(directory, transport).RunAsync(transfer);
        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(DirectedAuthorityMode.TransferPrepared, new DurableAuthorityStateStore(Path.Combine(directory, "source-authority.json")).Load().Mode);
    }

    [TestMethod]
    public async Task NetworkFailureIsRedactedAndStructured()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("secret-token DNS failure"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var exception = await ExpectTransportFailureAsync(() => transport.EnsureContainerAsync(false));
        Assert.IsFalse(exception.Message.Contains("secret-token", StringComparison.Ordinal));
        Assert.IsTrue(exception.Message.Contains("network", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task NonCallerTimeoutIsStructured()
    {
        var handler = new StubHandler((_, _) => throw new TaskCanceledException("timeout"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        using var transport = new GitHubReleaseAssetTransport(new GitHubHandoffTransportOptions("acme", "handoff", ApiBaseUri: client.BaseAddress), client, "secret-token");
        var exception = await ExpectTransportFailureAsync(() => transport.EnsureContainerAsync(false));
        Assert.IsTrue(exception.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
    }

    private static string NewDirectory() { var path = Path.Combine(Path.GetTempPath(), "sushi81-github-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<GitHubTransportException> ExpectTransportFailureAsync(Func<Task> action)
    {
        try { await action(); }
        catch (GitHubTransportException exception) { return exception; }
        Assert.Fail("Expected a structured GitHub transport failure.");
        return null!;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(responder(request, cancellationToken));
    }

    private sealed class ReleaseCommitFailureInjector : IDurableAuthorityStateFailureInjector
    {
        public void BeforeCommit(DurableAuthorityState nextState)
        {
            if (nextState.Mode == DirectedAuthorityMode.Released && nextState.GrantReceipt is not null)
                throw new IOException("synthetic crash before durable Released commit");
        }
    }

    private sealed class FakeTransport : IGitHubHandoffTransport
    {
        private readonly GitHubReleaseContainer release = new(9, "sushi81-handoff-v1", "https://uploads.example/assets", "", false, false);
        private readonly Dictionary<long, (GitHubRemoteAsset Asset, byte[] Bytes)> assets = new();
        private readonly string lineageId = Guid.NewGuid().ToString();
        private long nextId = 100;
        public List<string> UploadedNames { get; } = [];
        public bool FailSnapshotDeletionOnce { get; set; }
        public bool FailGrantUploadOnce { get; set; }
        public void CorruptDigest(string name)
        {
            var item = assets.Values.FirstOrDefault(x => x.Asset.Name == name);
            if (item.Asset is not null)
                assets[item.Asset.Id] = (item.Asset with { Digest = "sha256:" + new string('f', 64) }, item.Bytes);
        }
        public void RemoveAsset(long assetId) => assets.Remove(assetId);
        public void TruncateBytesPreservingReceipt(string name)
        {
            var item = assets.Values.First(x => x.Asset.Name == name);
            assets[item.Asset.Id] = (item.Asset, item.Bytes[..Math.Min(1, item.Bytes.Length)]);
        }

        public void ReplaceSnapshotAndGrantWithCorruptSqlite(string snapshotName)
        {
            var snapshot = assets.Values.First(x => x.Asset.Name == snapshotName);
            var corruptBytes = Encoding.UTF8.GetBytes("not-a-sqlite-database");
            var corruptDigest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(corruptBytes)).ToLowerInvariant();
            assets[snapshot.Asset.Id] = (snapshot.Asset with { Size = corruptBytes.LongLength, Digest = corruptDigest }, corruptBytes);

            var grant = assets.Values.First(x => x.Asset.Name == GitHubSnapshotName.GrantName(snapshotName));
            var original = JsonSerializer.Deserialize<GitHubHandoffGrant>(grant.Bytes)!;
            var updated = original with { SnapshotByteLength = corruptBytes.LongLength, SnapshotSha256 = corruptDigest[7..] };
            var grantBytes = JsonSerializer.SerializeToUtf8Bytes(updated);
            var grantDigest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(grantBytes)).ToLowerInvariant();
            assets[grant.Asset.Id] = (grant.Asset with { Size = grantBytes.LongLength, Digest = grantDigest }, grantBytes);
        }

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromResult(release);
        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, string filePath, CancellationToken cancellationToken = default)
        {
            if (GitHubSnapshotName.IsGrant(name) && FailGrantUploadOnce)
            {
                FailGrantUploadOnce = false;
                throw new GitHubTransportException("synthetic grant upload failure", HttpStatusCode.BadGateway);
            }
            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken); var id = ++nextId; var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            var asset = new GitHubRemoteAsset(id, name, bytes.Length, "uploaded", digest, "", DateTimeOffset.UtcNow); assets[id] = (asset, bytes); UploadedNames.Add(name);
            return new GitHubAssetReceipt(release.Id, id, name, bytes.Length, digest, DateTimeOffset.UtcNow);
        }
        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.Values.Select(x => x.Asset).ToArray());
        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => assets.TryGetValue(assetId, out var item)
            ? Task.FromResult(item.Asset)
            : Task.FromException<GitHubRemoteAsset>(new GitHubTransportException("asset not found", HttpStatusCode.NotFound));
        public Task<byte[]> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => assets.TryGetValue(assetId, out var item)
            ? Task.FromResult(item.Bytes)
            : Task.FromException<byte[]>(new GitHubTransportException("asset not found", HttpStatusCode.NotFound));
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            if (FailSnapshotDeletionOnce && assets.TryGetValue(assetId, out var item) && GitHubSnapshotName.IsValid(item.Asset.Name))
            {
                FailSnapshotDeletionOnce = false;
                throw new GitHubTransportException("synthetic deletion failure", HttpStatusCode.BadGateway);
            }
            assets.Remove(assetId); return Task.CompletedTask;
        }

        public async Task<DirectedTransferIdentity> SeedUnitAsync(long version, long generation = 1, string? lineage = null, string source = "a", string target = "b", string? timestamp = null)
        {
            var directory = NewDirectory(); var snapshotName = (timestamp ?? $"20260829{version:000000}") + ".snapshot.db"; var snapshotPath = Path.Combine(directory, snapshotName); await File.WriteAllBytesAsync(snapshotPath, Encoding.UTF8.GetBytes("snapshot-" + version));
            var snapshotReceipt = await UploadAssetAsync(release, snapshotName, snapshotPath);
            var transfer = new DirectedTransferIdentity(Guid.NewGuid().ToString(), lineage ?? lineageId, generation, version, source, target);
            var grant = new GitHubHandoffGrant(1, transfer.TransferId, transfer.LineageId, generation, version, source, target, release.Id, snapshotReceipt.AssetId, snapshotReceipt.Name, snapshotReceipt.Size, snapshotReceipt.Digest[7..], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var grantPath = Path.Combine(directory, GitHubSnapshotName.GrantName(snapshotName)); await File.WriteAllTextAsync(grantPath, System.Text.Json.JsonSerializer.Serialize(grant)); await UploadAssetAsync(release, GitHubSnapshotName.GrantName(snapshotName), grantPath);
            return transfer;
        }
    }
}
