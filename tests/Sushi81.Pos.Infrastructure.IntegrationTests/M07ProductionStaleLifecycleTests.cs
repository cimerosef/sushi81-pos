using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M07ProductionStaleLifecycleTests
{
    [TestMethod]
    public async Task ReinitializedDeviceCanLaterReceiveExactCurrentGenerationNormalHandoff()
    {
        using var fixture = new LifecycleFixture();
        await fixture.InitializeAsync();

        await using (var recovery = fixture.CreateRecoveryService())
        {
            var reinitialized = await recovery.ReinitializeStaleDeviceAsync();
            Assert.IsTrue(reinitialized.Succeeded, reinitialized.Diagnostic);
        }

        await fixture.TargetStore.WriteBootstrapMarkerAsync();
        await fixture.TargetStore.WriteBootstrapAnchorAsync();
        await fixture.SourceSystem.JoinCurrentGenerationAsync(fixture.SourceDeviceId, "Current source");
        await fixture.SourceStore.SaveAsync(fixture.SourceDocument());

        await using (var handoff = new NormalHandoffService(
            fixture.SourceStore,
            fixture.SourceGuard,
            fixture.SourceSystem,
            new ValidSnapshotFactory(fixture.SourceRoot, fixture.Clock),
            fixture.Transport,
            fixture.Clock))
        {
            var transferred = await handoff.TransferAndCloseAsync(fixture.TargetDeviceId);
            Assert.IsTrue(transferred.Succeeded, transferred.Error?.ToString());
        }

        var sourceAfterTransfer = (await fixture.SourceStore.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.ReleasedNonAuthoritative, sourceAfterTransfer.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.SourceGuard.State);

        await using (var acquisition = new TargetAcquisitionService(
            fixture.TargetStore,
            fixture.TargetGuard,
            fixture.TargetSystem,
            fixture.Transport,
            new SqliteTransferSnapshotInstaller(fixture.TargetPaths),
            fixture.Clock))
        {
            var acquired = await acquisition.AcquireAsync();
            Assert.IsTrue(acquired.Succeeded, acquired.Error?.ToString());
        }

        var targetAfterAcquisition = (await fixture.TargetStore.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, targetAfterAcquisition.Phase);
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.TargetGuard.State);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.SourceGuard.State);
        Assert.IsTrue(fixture.Transport.UploadedNames.Any(name => GitHubHandoffAssetNames.IsGrantName(name)));
        Assert.IsTrue(fixture.Transport.UploadedNames.Any(name => GitHubHandoffAssetNames.IsSnapshotName(name)));
    }

    private sealed class LifecycleFixture : IDisposable
    {
        public LifecycleFixture()
        {
            TargetRoot = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.StaleLifecycle", Guid.NewGuid().ToString("N"));
            SourceRoot = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.StaleLifecycle", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TargetRoot);
            Directory.CreateDirectory(SourceRoot);
            Clock = new FixedClock();
            LineageId = Guid.NewGuid();
            TargetDeviceId = Guid.NewGuid();
            SourceDeviceId = Guid.NewGuid();
            TargetPaths = new LifecyclePaths(TargetRoot);
            SourcePaths = new LifecyclePaths(SourceRoot);
            TargetPaths.EnsureInitialized();
            SourcePaths.EnsureInitialized();
            TargetGuard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            SourceGuard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            TargetStore = new JsonAuthorityStateStore(TargetPaths);
            SourceStore = new JsonAuthorityStateStore(SourcePaths);
            TargetSystem = new JsonSystemMetadataStore(Path.Combine(TargetRoot, "OneDrive"), new FixedTimeProvider(Clock.UtcNow));
            SourceSystem = new JsonSystemMetadataStore(Path.Combine(TargetRoot, "OneDrive"), new FixedTimeProvider(Clock.UtcNow));
            Transport = new SharedTransport(Clock);
        }

        public string TargetRoot { get; }
        public string SourceRoot { get; }
        public Guid LineageId { get; }
        public Guid TargetDeviceId { get; }
        public Guid SourceDeviceId { get; }
        public FixedClock Clock { get; }
        public LifecyclePaths TargetPaths { get; }
        public LifecyclePaths SourcePaths { get; }
        public WriteAuthorityGuard TargetGuard { get; }
        public WriteAuthorityGuard SourceGuard { get; }
        public JsonAuthorityStateStore TargetStore { get; }
        public JsonAuthorityStateStore SourceStore { get; }
        public JsonSystemMetadataStore TargetSystem { get; }
        public JsonSystemMetadataStore SourceSystem { get; }
        public SharedTransport Transport { get; }

        public async Task InitializeAsync()
        {
            await TargetSystem.EnsureCurrentLineageAsync(LineageId, 2);
            var stale = new AuthorityProtocolState(
                1, TargetDeviceId, "Replacement", LineageId, 1, 3, 0, AuthorityPhase.StaleGeneration);
            await TargetStore.SaveAsync(new AuthorityStateDocument(2, stale.WriteState, Clock.UtcNow) { Protocol = stale });

            var seedBytes = CreateDatabase(22, "current-generation-seed");
            var seedId = Guid.NewGuid();
            var seedMetadata = new ReadOnlySeedMetadata(
                1, "M07", SystemMetadataContract.ReadOnlySeedArtifactKind, seedId,
                LineageId, 2, SourceDeviceId, 22,
                SystemMetadataContract.SeedPayloadFileName(seedId), seedBytes.LongLength,
                Convert.ToHexString(SHA256.HashData(seedBytes)), Clock.UtcNow, 4);
            await TargetSystem.PublishReadOnlySeedAsync(seedMetadata, seedBytes);
        }

        public AuthorityStateDocument SourceDocument() => new(
            2,
            WriteAuthorityState.Authoritative,
            Clock.UtcNow)
        {
            Protocol = new AuthorityProtocolState(
                1, SourceDeviceId, "Current source", LineageId, 2, 4, 22, AuthorityPhase.Authoritative)
        };

        public DisasterRecoveryService CreateRecoveryService() => new(
            TargetStore,
            TargetGuard,
            TargetSystem,
            new EmptyRecoveryDiscovery(),
            activation: null,
            githubTransport: null,
            localSnapshots: new NoOpSnapshots(),
            paths: TargetPaths,
            clock: Clock);

        public void Dispose()
        {
            TargetGuard.Dispose();
            SourceGuard.Dispose();
            if (Directory.Exists(TargetRoot)) Directory.Delete(TargetRoot, recursive: true);
            if (Directory.Exists(SourceRoot)) Directory.Delete(SourceRoot, recursive: true);
        }

        private static byte[] CreateDatabase(long revision, string marker)
        {
            var path = Path.Combine(Path.GetTempPath(), $"m07-stale-lifecycle-{Guid.NewGuid():N}.db");
            try
            {
                using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision',$revision); CREATE TABLE synthetic_markers(marker TEXT NOT NULL PRIMARY KEY); INSERT INTO synthetic_markers(marker) VALUES ($marker);";
                    command.Parameters.AddWithValue("$revision", revision);
                    command.Parameters.AddWithValue("$marker", marker);
                    command.ExecuteNonQuery();
                }
                return File.ReadAllBytes(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private sealed class ValidSnapshotFactory(string root, FixedClock clock) : ITransferSnapshotFactory
    {
        public async Task<TransferSnapshot> CreateAsync(AuthorityProtocolState source, Guid transferId, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, $"handoff-{transferId:N}.db");
            var bytes = CreateDatabase(source.BusinessRevision, "normal-handoff-current-data");
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return new TransferSnapshot(
                GitHubHandoffAssetNames.CreateSnapshotName(clock.UtcNow),
                path,
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)),
                source.BusinessRevision);
        }

        private static byte[] CreateDatabase(long revision, string marker)
        {
            var path = Path.Combine(Path.GetTempPath(), $"m07-normal-handoff-{Guid.NewGuid():N}.db");
            try
            {
                using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision',$revision); CREATE TABLE synthetic_markers(marker TEXT NOT NULL PRIMARY KEY); INSERT INTO synthetic_markers(marker) VALUES ($marker);";
                    command.Parameters.AddWithValue("$revision", revision);
                    command.Parameters.AddWithValue("$marker", marker);
                    command.ExecuteNonQuery();
                }
                return File.ReadAllBytes(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private sealed class SharedTransport(FixedClock clock) : IGitHubHandoffTransport
    {
        private readonly GitHubReleaseContainer release = new(1, "sushi81-handoff-v1", "https://uploads.example/releases/1/assets{?name}", false, false);
        private readonly List<GitHubRemoteAsset> assets = [];
        private readonly Dictionary<long, byte[]> contents = [];
        private long nextAssetId = 100;

        public IReadOnlyList<string> UploadedNames => assets.Select(asset => asset.Name).ToArray();

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromResult(release);

        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var id = nextAssetId++;
            var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.LongLength, "uploaded", "sha256:" + digest, clock.UtcNow));
            contents[id] = bytes;
            return new GitHubAssetReceipt(release.Id, id, name, bytes.LongLength, "sha256:" + digest, clock.UtcNow, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(assets.Single(asset => asset.Id == assetId));

        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(contents[assetId], writable: false));

        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            assets.RemoveAll(asset => asset.Id == assetId);
            contents.Remove(assetId);
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyRecoveryDiscovery : IRecoveryCandidateDiscovery
    {
        public Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(AuthorityProtocolState localState, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoveryCandidateDiscoveryResult([], "not used by stale reinitialization"));

        public Task<RecoveryCandidate?> FindExactAsync(AuthorityProtocolState localState, string candidateId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RecoveryCandidate?>(null);
    }

    private sealed class NoOpSnapshots : ILocalRecoverySnapshotService
    {
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoverySnapshotResult(string.Empty, string.Empty, new string('A', 64), change.CommittedAtUtc, change.Sequence, 1));
    }

    private sealed class LifecyclePaths(string root) : IAppPaths
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
            {
                Directory.CreateDirectory(path);
            }
        }
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 8);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
