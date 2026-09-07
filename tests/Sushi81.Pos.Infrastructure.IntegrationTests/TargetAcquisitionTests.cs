using System.Security.Cryptography;
using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class TargetAcquisitionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ExpectedInstallOrder = ["stage", "install"];

    [TestMethod]
    public async Task ExactTargetAcquisitionInstallsDataBeforeDurableAuthority()
    {
        using var fixture = new AcquisitionFixture();
        var grant = await fixture.CreateGrantAsync(fixture.DeviceId);
        var store = fixture.CreateStateStore();
        await store.SaveAsync(fixture.PairedDocument());
        var transport = new AcquisitionTransport(fixture, grant);
        var installer = new RecordingInstaller(fixture, store);
        await using var service = new TargetAcquisitionService(
            store, fixture.Guard, fixture.SystemStore, transport, installer, fixture.Clock);

        var result = await service.AcquireAsync();

        Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.TargetAcquisitionPending, installer.PhaseObservedBeforeInstall);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await store.LoadAsync())!.Protocol!.Phase);
        CollectionAssert.AreEqual(ExpectedInstallOrder, installer.Events);
    }

    [TestMethod]
    public async Task NonTargetGrantDoesNotCreateAuthority()
    {
        using var fixture = new AcquisitionFixture();
        var grant = await fixture.CreateGrantAsync(Guid.NewGuid());
        var store = fixture.CreateStateStore();
        await store.SaveAsync(fixture.PairedDocument());
        await using var service = new TargetAcquisitionService(
            store, fixture.Guard, fixture.SystemStore, new AcquisitionTransport(fixture, grant),
            new RecordingInstaller(fixture, store), fixture.Clock);

        var result = await service.AcquireAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.PairedUninitializedReadOnly, (await store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task InstallFailureLeavesPendingStateReadOnly()
    {
        using var fixture = new AcquisitionFixture();
        var grant = await fixture.CreateGrantAsync(fixture.DeviceId);
        var store = fixture.CreateStateStore();
        await store.SaveAsync(fixture.PairedDocument());
        var installer = new RecordingInstaller(fixture, store) { ThrowOnInstall = true };
        await using var service = new TargetAcquisitionService(
            store, fixture.Guard, fixture.SystemStore, new AcquisitionTransport(fixture, grant), installer, fixture.Clock);

        var result = await service.AcquireAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.TargetAcquisitionPending, (await store.LoadAsync())!.Protocol!.Phase);
    }

    private sealed class AcquisitionFixture : IDisposable
    {
        public AcquisitionFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Acquisition", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Clock = new FixedClock();
            Guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            SystemStore = new JsonSystemMetadataStore(Root, new FixedTimeProvider(Clock.UtcNow));
        }

        public string Root { get; }
        public Guid LineageId { get; } = Guid.NewGuid();
        public Guid DeviceId { get; } = Guid.NewGuid();
        public Guid SourceDeviceId { get; } = Guid.NewGuid();
        public FixedClock Clock { get; }
        public WriteAuthorityGuard Guard { get; }
        public JsonSystemMetadataStore SystemStore { get; }

        public JsonAuthorityStateStore CreateStateStore() => new(new TestPaths(Root));

        public AuthorityStateDocument PairedDocument() => new(
            2,
            WriteAuthorityState.NonAuthoritativeReadOnly,
            Clock.UtcNow)
        {
            Protocol = new AuthorityProtocolState(1, DeviceId, "Target", LineageId, 1, 0, 0, AuthorityPhase.PairedUninitializedReadOnly)
        };

        public async Task<GrantFixture> CreateGrantAsync(Guid targetDeviceId)
        {
            await WriteJsonAsync(Path.Combine(Root, "System", "Lineage", "lineage.json"), new SystemLineageMetadata(1, "M07", LineageId, 1, Clock.UtcNow));
            await WriteJsonAsync(Path.Combine(Root, "System", "Devices", "1", $"{DeviceId:N}.device.json"), new DeviceRegistrationArtifact(1, "M07", "device-membership", DeviceId, "Target", LineageId, 1, Clock.UtcNow));
            var snapshotBytes = new byte[] { 9, 8, 7, 6, 5 };
            var snapshotHash = Convert.ToHexString(SHA256.HashData(snapshotBytes));
            var snapshot = new RemoteAssetEvidence(1, 20, "20260907120000.snapshot.db", snapshotBytes.Length, snapshotHash);
            var grant = new NormalHandoffGrant("M07", Guid.NewGuid(), LineageId, 1, 1, SourceDeviceId, targetDeviceId, 4, snapshot, Clock.UtcNow.AddMinutes(-1), Clock.UtcNow);
            var grantBytes = JsonSerializer.SerializeToUtf8Bytes(grant, JsonOptions);
            var grantHash = Convert.ToHexString(SHA256.HashData(grantBytes));
            return new GrantFixture(grant, grantBytes, snapshotBytes, grantHash, snapshotHash);
        }

        private static async Task WriteJsonAsync<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions));
        }

        public void Dispose()
        {
            Guard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed record GrantFixture(NormalHandoffGrant Grant, byte[] GrantBytes, byte[] SnapshotBytes, string GrantHash, string SnapshotHash);

    private sealed class AcquisitionTransport(AcquisitionFixture fixture, GrantFixture grant) : IGitHubHandoffTransport
    {
        private readonly GitHubReleaseContainer release = new(1, "sushi81-handoff-v1", "https://uploads.example/releases/1/assets{?name}", false, false);

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default) => Task.FromResult(release);

        public Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default)
        {
            var grantAsset = new GitHubRemoteAsset(10, "20260907120000.grant.json", grant.GrantBytes.Length, "uploaded", "sha256:" + grant.GrantHash.ToLowerInvariant(), fixture.Clock.UtcNow);
            return Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>([grantAsset]);
        }

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            if (assetId == 20)
                return Task.FromResult(new GitHubRemoteAsset(20, grant.Grant.SnapshotReceipt.Name, grant.SnapshotBytes.Length, "uploaded", "sha256:" + grant.SnapshotHash.ToLowerInvariant(), fixture.Clock.UtcNow));
            return Task.FromResult(new GitHubRemoteAsset(10, "20260907120000.grant.json", grant.GrantBytes.Length, "uploaded", "sha256:" + grant.GrantHash.ToLowerInvariant(), fixture.Clock.UtcNow));
        }

        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(assetId == 20 ? grant.SnapshotBytes : grant.GrantBytes, writable: false));

        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingInstaller(AcquisitionFixture fixture, JsonAuthorityStateStore store) : ITransferSnapshotInstaller
    {
        public List<string> Events { get; } = [];
        public AuthorityPhase? PhaseObservedBeforeInstall { get; private set; }
        public bool ThrowOnInstall { get; init; }

        public async Task<TargetSnapshotStaging> StageAndValidateAsync(Guid transferId, Stream content, string expectedName, long expectedSize, string expectedSha256, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(fixture.Root, $"staged-{transferId:N}.db");
            await using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            Events.Add("stage");
            var actualHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));
            return new TargetSnapshotStaging(path, new FileInfo(path).Length, actualHash);
        }

        public async Task InstallAndVerifyAsync(TargetSnapshotStaging staging, CancellationToken cancellationToken = default)
        {
            PhaseObservedBeforeInstall = (await store.LoadAsync(cancellationToken))!.Protocol!.Phase;
            Events.Add("install");
            if (ThrowOnInstall) throw new IOException("synthetic install failure");
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
