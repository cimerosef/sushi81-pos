using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M07ReviewRemediationTests
{
    [TestMethod]
    public async Task PostRelinquishmentRetryReusesByteIdenticalGrantAfterUnknownCreateOutcome()
    {
        using var fixture = await ReviewFixture.CreateAsync(0);
        fixture.Transport.LoseFirstGrantResponse = true;
        await using (var first = fixture.CreateHandoff())
        {
            var result = await first.TransferAndCloseAsync(fixture.TargetDeviceId);
            Assert.IsFalse(result.Succeeded);
        }

        var firstGrantBytes = fixture.Transport.GetGrantBytes();
        Assert.AreEqual(AuthorityPhase.RelinquishedPendingGrant, (await fixture.Store.LoadAsync())!.Protocol!.Phase);

        await using (var retry = fixture.CreateHandoff())
        {
            var result = await retry.TransferAndCloseAsync(fixture.TargetDeviceId);
            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        }

        Assert.AreEqual(1, fixture.Transport.GrantUploadCount, "The exact remote grant is re-observed, not uploaded with new bytes.");
        CollectionAssert.AreEqual(firstGrantBytes, fixture.Transport.GetGrantBytes());
        Assert.AreEqual(AuthorityPhase.ReleasedNonAuthoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task ContradictorySameNameGrantAfterRelinquishmentRemainsReadOnly()
    {
        using var fixture = await ReviewFixture.CreateAsync(0);
        fixture.Transport.LoseFirstGrantResponse = true;
        await using (var first = fixture.CreateHandoff())
            Assert.IsFalse((await first.TransferAndCloseAsync(fixture.TargetDeviceId)).Succeeded);

        fixture.Transport.ReplaceGrantWithContradictoryBytes();
        await using var retry = fixture.CreateHandoff();
        var result = await retry.TransferAndCloseAsync(fixture.TargetDeviceId);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.RelinquishedPendingGrant, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task RetentionUsesGenerationAndHandoffVersionWhenFilenameClockIsSkewed()
    {
        using var fixture = await ReviewFixture.CreateAsync(4);
        fixture.Transport.AddCompleteUnit(fixture.LineageId, 1, "20991231115959");
        fixture.Transport.AddCompleteUnit(fixture.LineageId, 2, "20000101000000");
        fixture.Transport.AddCompleteUnit(fixture.LineageId, 3, "20000101000001");
        fixture.Transport.AddCompleteUnit(fixture.LineageId, 4, "20000101000002");

        await using var service = fixture.CreateHandoff();
        var result = await service.TransferAndCloseAsync(fixture.TargetDeviceId);

        Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        CollectionAssert.AreEquivalent(new long[] { 3, 4, 5 }, fixture.Transport.ValidGrantVersions().ToArray());
    }

    [TestMethod]
    public async Task SnapshotAndGrantNamesAdvanceWithoutReusingOccupiedTimestampPair()
    {
        using var fixture = await ReviewFixture.CreateAsync(0);
        fixture.Transport.AddOccupiedSnapshot("20260907120000.snapshot.db");

        await using var service = fixture.CreateHandoff();
        var result = await service.TransferAndCloseAsync(fixture.TargetDeviceId);

        Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        var uploadedSnapshot = fixture.Transport.UploadedSnapshotNames.Single(name => name != "20260907120000.snapshot.db");
        Assert.AreEqual("20260907120001.snapshot.db", uploadedSnapshot);
        CollectionAssert.Contains(fixture.Transport.UploadedGrantNames, GitHubHandoffAssetNames.CreateGrantName(uploadedSnapshot));
    }

    private sealed class ReviewFixture : IDisposable
    {
        private ReviewFixture(long handoffVersion)
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Review", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Clock = new FixedClock();
            Guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            SystemMetadata = new JsonSystemMetadataStore(Root, new FixedTimeProvider(Clock.UtcNow));
            Store = new JsonAuthorityStateStore(new TestPaths(Root));
            Transport = new ReviewTransport(Clock);
        }

        public string Root { get; }
        public Guid LineageId { get; } = Guid.NewGuid();
        public Guid SourceDeviceId { get; } = Guid.NewGuid();
        public Guid TargetDeviceId { get; } = Guid.NewGuid();
        public FixedClock Clock { get; }
        public WriteAuthorityGuard Guard { get; }
        public JsonSystemMetadataStore SystemMetadata { get; }
        public JsonAuthorityStateStore Store { get; }
        public ReviewTransport Transport { get; }

        public static async Task<ReviewFixture> CreateAsync(long handoffVersion)
        {
            var fixture = new ReviewFixture(handoffVersion);
            await fixture.SystemMetadata.EnsureCurrentLineageAsync(fixture.LineageId, 1);
            await fixture.SystemMetadata.JoinCurrentGenerationAsync(fixture.SourceDeviceId, "Source");
            await fixture.SystemMetadata.JoinCurrentGenerationAsync(fixture.TargetDeviceId, "Target");
            await fixture.Store.SaveAsync(new AuthorityStateDocument(2, WriteAuthorityState.Authoritative, fixture.Clock.UtcNow)
            {
                Protocol = new AuthorityProtocolState(1, fixture.SourceDeviceId, "Source", fixture.LineageId, 1, handoffVersion, handoffVersion, AuthorityPhase.Authoritative)
            });
            return fixture;
        }

        public NormalHandoffService CreateHandoff() => new(
            Store,
            Guard,
            SystemMetadata,
            new FixedSnapshotFactory(Root),
            Transport,
            Clock);

        public void Dispose()
        {
            Guard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FixedSnapshotFactory(string root) : ITransferSnapshotFactory
    {
        public async Task<TransferSnapshot> CreateAsync(AuthorityProtocolState source, Guid transferId, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, $"snapshot-{transferId:N}.db");
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return new TransferSnapshot("20260907120000.snapshot.db", path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), source.BusinessRevision);
        }
    }

    private sealed class ReviewTransport(FixedClock clock) : IGitHubHandoffTransport
    {
        private readonly GitHubReleaseContainer release = new(1, "sushi81-handoff-v1", "https://uploads.example/releases/1/assets{?name}", false, false);
        private readonly List<GitHubRemoteAsset> assets = [];
        private readonly Dictionary<long, byte[]> contents = [];
        private long nextId = 100;
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

        public bool LoseFirstGrantResponse { get; set; }
        public int GrantUploadCount { get; private set; }
        public List<string> UploadedSnapshotNames { get; } = [];
        public List<string> UploadedGrantNames { get; } = [];

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromResult(release);

        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var id = nextId++;
            var digest = Convert.ToHexString(SHA256.HashData(bytes));
            assets.Add(new GitHubRemoteAsset(id, name, bytes.Length, "uploaded", "sha256:" + digest.ToLowerInvariant(), clock.UtcNow));
            contents[id] = bytes;
            if (GitHubHandoffAssetNames.IsGrantName(name))
            {
                GrantUploadCount++;
                UploadedGrantNames.Add(name);
                if (LoseFirstGrantResponse)
                {
                    LoseFirstGrantResponse = false;
                    throw new IOException("synthetic unknown response after remote grant creation");
                }
            }
            else UploadedSnapshotNames.Add(name);
            return new GitHubAssetReceipt(release.Id, id, name, bytes.Length, "sha256:" + digest.ToLowerInvariant(), clock.UtcNow, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult(assets.Single(asset => asset.Id == assetId));

        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(contents[assetId], writable: false));

        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            assets.RemoveAll(asset => asset.Id == assetId);
            contents.Remove(assetId);
            return Task.CompletedTask;
        }

        public byte[] GetGrantBytes() => contents[assets.Single(asset => GitHubHandoffAssetNames.IsGrantName(asset.Name)).Id];

        public void ReplaceGrantWithContradictoryBytes()
        {
            var existing = assets.Single(asset => GitHubHandoffAssetNames.IsGrantName(asset.Name));
            contents[existing.Id] = [9, 9, 9];
            var digest = Convert.ToHexString(SHA256.HashData(contents[existing.Id])).ToLowerInvariant();
            assets[assets.IndexOf(existing)] = existing with { Size = 3, Digest = "sha256:" + digest };
        }

        public void AddOccupiedSnapshot(string name)
        {
            var bytes = new byte[] { 7, 7, 7 };
            var id = nextId++;
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.Length, "uploaded", "sha256:" + digest, clock.UtcNow));
            contents[id] = bytes;
        }

        public void AddCompleteUnit(Guid lineageId, long version, string timestamp)
        {
            var snapshotName = timestamp + ".snapshot.db";
            var bytes = new byte[] { (byte)version, 4, 5 };
            var snapshotId = nextId++;
            var snapshotHash = Convert.ToHexString(SHA256.HashData(bytes));
            assets.Add(new GitHubRemoteAsset(snapshotId, snapshotName, bytes.Length, "uploaded", "sha256:" + snapshotHash.ToLowerInvariant(), clock.UtcNow));
            contents[snapshotId] = bytes;
            var grant = new NormalHandoffGrant(
                "M07", Guid.NewGuid(), lineageId, 1, version, Guid.NewGuid(), Guid.NewGuid(), version,
                new RemoteAssetEvidence(release.Id, snapshotId, snapshotName, bytes.Length, snapshotHash),
                clock.UtcNow.AddMinutes(-1), clock.UtcNow);
            var grantBytes = JsonSerializer.SerializeToUtf8Bytes(grant, JsonOptions);
            var grantId = nextId++;
            var grantHash = Convert.ToHexString(SHA256.HashData(grantBytes));
            assets.Add(new GitHubRemoteAsset(grantId, GitHubHandoffAssetNames.CreateGrantName(snapshotName), grantBytes.Length, "uploaded", "sha256:" + grantHash.ToLowerInvariant(), clock.UtcNow));
            contents[grantId] = grantBytes;
        }

        public long[] ValidGrantVersions() => assets
            .Where(asset => GitHubHandoffAssetNames.IsGrantName(asset.Name))
            .Select(asset => JsonSerializer.Deserialize<NormalHandoffGrant>(contents[asset.Id], JsonOptions)!.HandoffVersion)
            .OrderBy(version => version)
            .ToArray();
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 7);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string RootDirectory { get; } = root;
        public string DataDirectory { get; } = Path.Combine(root, "Data");
        public string RecoveryDirectory { get; } = Path.Combine(root, "Recovery");
        public string CacheDirectory { get; } = Path.Combine(root, "Cache");
        public string LogsDirectory { get; } = Path.Combine(root, "Logs");
        public string ConfigDirectory { get; } = Path.Combine(root, "Config");
        public string TempDirectory { get; } = Path.Combine(root, "Temp");
        public string LiveDatabasePath { get; } = Path.Combine(root, "Data", "live.db");
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
    }
}
