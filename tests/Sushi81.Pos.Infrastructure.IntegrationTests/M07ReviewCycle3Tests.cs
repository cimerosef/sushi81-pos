using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
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
public sealed class M07ReviewCycle3Tests
{
    [TestMethod]
    public async Task ReleasedNonAuthoritativeSourceCanCompleteDeterministicRoundTrip()
    {
        using var fixture = new RoundTripFixture();
        await fixture.InitializeAsync();

        await using (var sourceHandoff = fixture.CreateHandoff(fixture.SourceStore, fixture.SourceGuard, fixture.SourceRoot))
        {
            var result = await sourceHandoff.TransferAndCloseAsync(fixture.TargetDeviceId);
            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        }

        await using (var targetAcquisition = fixture.CreateAcquisition(fixture.TargetStore, fixture.TargetGuard, fixture.TargetRoot))
        {
            var result = await targetAcquisition.AcquireAsync();
            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        }

        await using (var targetHandoff = fixture.CreateHandoff(fixture.TargetStore, fixture.TargetGuard, fixture.TargetRoot))
        {
            var result = await targetHandoff.TransferAndCloseAsync(fixture.SourceDeviceId);
            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        }

        await using (var sourceAcquisition = fixture.CreateAcquisition(fixture.SourceStore, fixture.SourceGuard, fixture.SourceRoot))
        {
            var result = await sourceAcquisition.AcquireAsync();
            Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        }

        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.SourceStore.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(AuthorityPhase.ReleasedNonAuthoritative, (await fixture.TargetStore.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(2L, (await fixture.SourceStore.LoadAsync())!.Protocol!.HandoffVersion);
        Assert.AreEqual(2L, (await fixture.TargetStore.LoadAsync())!.Protocol!.HandoffVersion);
    }

    [TestMethod]
    public async Task LocalAuthorityRemainsWritableWhenSystemMetadataIsUnavailableButContradictionFailsClosed()
    {
        using var fixture = new CoordinatorFixture();
        await fixture.Store.SaveAsync(fixture.AuthoritativeDocument);
        await fixture.Store.WriteBootstrapMarkerAsync();
        await fixture.Store.WriteBootstrapAnchorAsync();

        using var unavailableGuard = new WriteAuthorityGuard();
        var unavailable = await new AuthorityStateCoordinator(
            fixture.Store,
            unavailableGuard,
            fixture.Clock,
            NullLogger<AuthorityStateCoordinator>.Instance,
            new ThrowingSystemMetadataStore(new IOException("synthetic OneDrive outage")))
            .InitializeAsync(legacyBootstrapEvidence: true, preMigrationLiveDatabaseEvidence: true);
        Assert.AreEqual(WriteAuthorityState.Authoritative, unavailable.State);
        unavailableGuard.RequireWriteAuthority();

        using var contradictoryGuard = new WriteAuthorityGuard();
        var contradictory = await new AuthorityStateCoordinator(
            fixture.Store,
            contradictoryGuard,
            fixture.Clock,
            NullLogger<AuthorityStateCoordinator>.Instance,
            new ThrowingSystemMetadataStore(new InvalidDataException("synthetic contradictory lineage")))
            .InitializeAsync(legacyBootstrapEvidence: true, preMigrationLiveDatabaseEvidence: true);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, contradictory.State);
        Assert.Throws<WriteAuthorityException>(() => contradictoryGuard.RequireWriteAuthority());
    }

    private sealed class RoundTripFixture : IDisposable
    {
        public RoundTripFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.RoundTrip", Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Root, "source");
            TargetRoot = Path.Combine(Root, "target");
            SharedRoot = Path.Combine(Root, "onedrive");
            Directory.CreateDirectory(Root);
            Clock = new FixedClock();
            LineageId = Guid.NewGuid();
            SourceDeviceId = Guid.NewGuid();
            TargetDeviceId = Guid.NewGuid();
            Transport = new SharedTransport(Clock);
            SourceGuard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            TargetGuard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            SourceStore = new JsonAuthorityStateStore(new TestPaths(SourceRoot));
            TargetStore = new JsonAuthorityStateStore(new TestPaths(TargetRoot));
            SourceMetadata = new JsonSystemMetadataStore(SharedRoot, new FixedTimeProvider(Clock.UtcNow));
            TargetMetadata = new JsonSystemMetadataStore(SharedRoot, new FixedTimeProvider(Clock.UtcNow));
        }

        public string Root { get; }
        public string SourceRoot { get; }
        public string TargetRoot { get; }
        public string SharedRoot { get; }
        public FixedClock Clock { get; }
        public Guid LineageId { get; }
        public Guid SourceDeviceId { get; }
        public Guid TargetDeviceId { get; }
        public SharedTransport Transport { get; }
        public WriteAuthorityGuard SourceGuard { get; }
        public WriteAuthorityGuard TargetGuard { get; }
        public JsonAuthorityStateStore SourceStore { get; }
        public JsonAuthorityStateStore TargetStore { get; }
        public JsonSystemMetadataStore SourceMetadata { get; }
        public JsonSystemMetadataStore TargetMetadata { get; }

        public async Task InitializeAsync()
        {
            await SourceMetadata.EnsureCurrentLineageAsync(LineageId, 1);
            await SourceMetadata.JoinCurrentGenerationAsync(SourceDeviceId, "Source");
            await TargetMetadata.JoinCurrentGenerationAsync(TargetDeviceId, "Target");
            await SaveEvidenceAsync(SourceStore);
            await SaveEvidenceAsync(TargetStore);
            await SourceStore.SaveAsync(new AuthorityStateDocument(2, WriteAuthorityState.Authoritative, Clock.UtcNow)
            {
                Protocol = new AuthorityProtocolState(1, SourceDeviceId, "Source", LineageId, 1, 0, 0, AuthorityPhase.Authoritative)
            });
            await TargetStore.SaveAsync(new AuthorityStateDocument(2, WriteAuthorityState.NonAuthoritativeReadOnly, Clock.UtcNow)
            {
                Protocol = new AuthorityProtocolState(1, TargetDeviceId, "Target", LineageId, 1, 0, 0, AuthorityPhase.PairedUninitializedReadOnly)
            });
        }

        public NormalHandoffService CreateHandoff(JsonAuthorityStateStore store, WriteAuthorityGuard guard, string root) => new(
            store, guard, new JsonSystemMetadataStore(SharedRoot, new FixedTimeProvider(Clock.UtcNow)),
            new SnapshotFactory(root), Transport, Clock);

        public TargetAcquisitionService CreateAcquisition(JsonAuthorityStateStore store, WriteAuthorityGuard guard, string root) => new(
            store, guard, new JsonSystemMetadataStore(SharedRoot, new FixedTimeProvider(Clock.UtcNow)),
            Transport, new SnapshotInstaller(root), Clock);

        private static async Task SaveEvidenceAsync(JsonAuthorityStateStore store)
        {
            await store.WriteBootstrapMarkerAsync();
            await store.WriteBootstrapAnchorAsync();
        }

        public void Dispose()
        {
            SourceGuard.Dispose();
            TargetGuard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class CoordinatorFixture : IDisposable
    {
        public CoordinatorFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Coordinator", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Clock = new FixedClock();
            Store = new JsonAuthorityStateStore(new TestPaths(Root));
            DeviceId = Guid.NewGuid();
            LineageId = Guid.NewGuid();
            AuthoritativeDocument = new AuthorityStateDocument(2, WriteAuthorityState.Authoritative, Clock.UtcNow)
            {
                Protocol = new AuthorityProtocolState(1, DeviceId, "Source", LineageId, 1, 0, 0, AuthorityPhase.Authoritative)
            };
        }

        public string Root { get; }
        public FixedClock Clock { get; }
        public JsonAuthorityStateStore Store { get; }
        public Guid DeviceId { get; }
        public Guid LineageId { get; }
        public AuthorityStateDocument AuthoritativeDocument { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class ThrowingSystemMetadataStore(Exception exception) : ISystemMetadataStore
    {
        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Task.FromException<SystemLineageMetadata>(exception);
        public Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default) => Task.FromException<SystemLineageMetadata>(exception);
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) => Task.FromException<DeviceSelfJoinResult>(exception);
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<DeviceRegistrationArtifact>>(exception);
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Task.FromException<ValidatedReadOnlySeed?>(exception);
        public Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) => Task.FromException<ReadOnlySeedPublicationResult>(exception);
    }

    private sealed class SharedTransport(FixedClock clock) : IGitHubHandoffTransport
    {
        private readonly GitHubReleaseContainer release = new(1, "sushi81-handoff-v1", "https://uploads.example/releases/1/assets{?name}", false, false);
        private readonly List<GitHubRemoteAsset> assets = [];
        private readonly Dictionary<long, byte[]> contents = [];
        private long nextId = 100;

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromResult(release);

        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var id = nextId++;
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            assets.Add(new GitHubRemoteAsset(id, name, bytes.Length, "uploaded", "sha256:" + hash.ToLowerInvariant(), clock.UtcNow));
            contents[id] = bytes;
            return new GitHubAssetReceipt(release.Id, id, name, bytes.Length, "sha256:" + hash.ToLowerInvariant(), clock.UtcNow, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());
        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult(assets.Single(asset => asset.Id == assetId));
        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(contents[assetId], writable: false));
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) { assets.RemoveAll(asset => asset.Id == assetId); contents.Remove(assetId); return Task.CompletedTask; }
    }

    private sealed class SnapshotFactory(string root) : ITransferSnapshotFactory
    {
        public async Task<TransferSnapshot> CreateAsync(AuthorityProtocolState source, Guid transferId, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"snapshot-{transferId:N}.db");
            var bytes = new byte[] { 1, 2, 3, (byte)source.HandoffVersion };
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return new TransferSnapshot("20260907120000.snapshot.db", path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), source.BusinessRevision);
        }
    }

    private sealed class SnapshotInstaller(string root) : ITransferSnapshotInstaller
    {
        public async Task<TargetSnapshotStaging> StageAndValidateAsync(Guid transferId, Stream content, string expectedName, long expectedSize, string expectedSha256, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"staged-{transferId:N}.db");
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.AreEqual(expectedSize, bytes.LongLength);
            Assert.AreEqual(expectedSha256, hash, true);
            return new TargetSnapshotStaging(path, bytes.LongLength, hash);
        }

        public Task InstallAndVerifyAsync(TargetSnapshotStaging staging, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(root);
            File.Copy(staging.Path, Path.Combine(root, "live.db"), overwrite: true);
            File.Delete(staging.Path);
            return Task.CompletedTask;
        }
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
