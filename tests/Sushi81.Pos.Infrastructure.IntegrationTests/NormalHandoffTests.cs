using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class NormalHandoffTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [TestMethod]
    public async Task ExactTargetHandoffPersistsRelinquishmentBeforeGrantAndReleasesSource()
    {
        using var fixture = new HandoffFixture();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        await fixture.WriteMembershipAsync(source, target);
        var store = fixture.CreateStateStore();
        await store.SaveAsync(fixture.AuthoritativeDocument(source));
        var transport = new RecordingTransport(fixture, store);
        var snapshot = new RecordingSnapshotFactory(fixture.Root);
        await using var service = new NormalHandoffService(
            store, fixture.Guard, fixture.SystemStore, snapshot, transport, fixture.Clock);

        var result = await service.TransferAndCloseAsync(target);

        Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        Assert.AreNotEqual(Guid.Empty, result.TransferId);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        var persisted = await store.LoadAsync();
        Assert.IsNotNull(persisted?.Protocol);
        Assert.AreEqual(AuthorityPhase.ReleasedNonAuthoritative, persisted!.Protocol!.Phase);
        Assert.HasCount(2, transport.Uploads);
        Assert.AreEqual("snapshot", transport.Uploads[0].Kind);
        Assert.AreEqual("grant", transport.Uploads[1].Kind);
        Assert.AreEqual(AuthorityPhase.RelinquishedPendingGrant, transport.PhaseObservedBeforeGrantUpload);
    }

    [TestMethod]
    public async Task SnapshotFailureBeforeRelinquishmentReturnsSourceToAuthoritativeWithConsumedVersion()
    {
        using var fixture = new HandoffFixture();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        await fixture.WriteMembershipAsync(source, target);
        var store = fixture.CreateStateStore();
        await store.SaveAsync(fixture.AuthoritativeDocument(source));
        var snapshot = new RecordingSnapshotFactory(fixture.Root) { ThrowOnCreate = true };
        await using var service = new NormalHandoffService(
            store, fixture.Guard, fixture.SystemStore, snapshot, new RecordingTransport(fixture, store), fixture.Clock);

        var result = await service.TransferAndCloseAsync(target);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        var persisted = await store.LoadAsync();
        Assert.AreEqual(AuthorityPhase.Authoritative, persisted!.Protocol!.Phase);
        Assert.AreEqual(1, persisted.Protocol.HandoffVersion, "The failed transfer version is never reused.");
    }

    [TestMethod]
    public async Task GrantFailureAfterRelinquishmentLeavesExactPendingTransferReadOnly()
    {
        using var fixture = new HandoffFixture();
        var source = Guid.NewGuid();
        var target = Guid.NewGuid();
        await fixture.WriteMembershipAsync(source, target);
        var store = fixture.CreateStateStore();
        await store.SaveAsync(fixture.AuthoritativeDocument(source));
        var transport = new RecordingTransport(fixture, store) { ThrowOnGrantUpload = true };
        await using var service = new NormalHandoffService(
            store, fixture.Guard, fixture.SystemStore, new RecordingSnapshotFactory(fixture.Root), transport, fixture.Clock);

        var result = await service.TransferAndCloseAsync(target);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        var persisted = await store.LoadAsync();
        Assert.AreEqual(AuthorityPhase.RelinquishedPendingGrant, persisted!.Protocol!.Phase);
        Assert.IsNull(persisted.Protocol.Transfer!.GrantReceipt);
        Assert.AreEqual(result.TransferId, persisted.Protocol.Transfer.TransferId);
    }

    private sealed class HandoffFixture : IDisposable
    {
        public HandoffFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Handoff", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Clock = new FixedBusinessClock();
            Guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            SystemStore = new JsonSystemMetadataStore(Root, new FixedTimeProvider(Clock.UtcNow));
        }

        public string Root { get; }
        public FixedBusinessClock Clock { get; }
        public WriteAuthorityGuard Guard { get; }
        public JsonSystemMetadataStore SystemStore { get; }

        public JsonAuthorityStateStore CreateStateStore() => new(new TestAppPaths(Root));

        public AuthorityStateDocument AuthoritativeDocument(Guid deviceId)
        {
            var protocol = new AuthorityProtocolState(1, deviceId, "Source", LineageId, 1, 0, 12, AuthorityPhase.Authoritative);
            return new AuthorityStateDocument(2, WriteAuthorityState.Authoritative, Clock.UtcNow) { Protocol = protocol };
        }

        public Guid LineageId { get; } = Guid.NewGuid();

        public async Task WriteMembershipAsync(Guid source, Guid target)
        {
            var lineage = new SystemLineageMetadata(1, "M07", LineageId, 1, Clock.UtcNow);
            await WriteJsonAsync(
                Path.Combine(Root, "System", "Lineage", "lineage.json"), lineage);
            foreach (var (device, name) in new[] { (source, "Source"), (target, "Target") })
            {
                var artifact = new DeviceRegistrationArtifact(1, "M07", "device-membership", device, name, LineageId, 1, Clock.UtcNow);
                await WriteJsonAsync(
                    Path.Combine(Root, "System", "Devices", "1", $"{device:N}.device.json"), artifact);
            }
        }

        private static async Task WriteJsonAsync<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions));
        }

        public void Dispose()
        {
            Guard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class RecordingSnapshotFactory(string root) : ITransferSnapshotFactory
    {
        public bool ThrowOnCreate { get; init; }

        public async Task<TransferSnapshot> CreateAsync(AuthorityProtocolState source, Guid transferId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnCreate) throw new IOException("synthetic snapshot failure");
            var path = Path.Combine(root, $"snapshot-{transferId:N}.db");
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return new TransferSnapshot("20260907120000.snapshot.db", path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), source.BusinessRevision);
        }
    }

    private sealed class RecordingTransport(HandoffFixture fixture, JsonAuthorityStateStore store) : IGitHubHandoffTransport
    {
        public List<(string Kind, string Name)> Uploads { get; } = [];
        public AuthorityPhase? PhaseObservedBeforeGrantUpload { get; private set; }
        public bool ThrowOnGrantUpload { get; init; }
        private readonly List<GitHubRemoteAsset> assets = [];
        private long nextId = 10;

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitHubReleaseContainer(1, "sushi81-handoff-v1", "https://uploads.example/releases/1/assets{?name}", false, false));

        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var isGrant = GitHubHandoffAssetNames.IsGrantName(name);
            if (isGrant)
            {
                PhaseObservedBeforeGrantUpload = (await store.LoadAsync(cancellationToken))!.Protocol!.Phase;
                if (ThrowOnGrantUpload) throw new IOException("synthetic grant failure");
            }
            var id = nextId++;
            var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.Length, "uploaded", digest, fixture.Clock.UtcNow));
            Uploads.Add((isGrant ? "grant" : "snapshot", name));
            return new GitHubAssetReceipt(release.Id, id, name, bytes.Length, digest, fixture.Clock.UtcNow, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult(assets.Single(asset => asset.Id == assetId));

        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream());

        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            assets.RemoveAll(asset => asset.Id == assetId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedBusinessClock : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 7);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestAppPaths(string root) : IAppPaths
    {
        public string RootDirectory { get; } = root;
        public string DataDirectory { get; } = Path.Combine(root, "Data");
        public string RecoveryDirectory { get; } = Path.Combine(root, "Recovery");
        public string CacheDirectory { get; } = Path.Combine(root, "Cache");
        public string LogsDirectory { get; } = Path.Combine(root, "Logs");
        public string ConfigDirectory { get; } = Path.Combine(root, "Config");
        public string TempDirectory { get; } = Path.Combine(root, "Temp");
        public string LiveDatabasePath { get; } = Path.Combine(root, "Data", "live.db");

        public void EnsureInitialized()
        {
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(path);
        }
    }
}
