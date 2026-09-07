using System.IO;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Time;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M07SetupDesktopTests
{
    [TestMethod]
    public async Task PersistedSetupRecompositionExposesReadOnlySelfJoinWithoutAuthorityStateMutation()
    {
        using var paths = new TestAppPaths();
        var configurationService = new JsonLocalConfigurationService(paths);
        var initial = await configurationService.LoadAsync();
        var oneDriveRoot = Path.Combine(paths.RootDirectory, "Shared OneDrive");
        Directory.CreateDirectory(oneDriveRoot);
        await new JsonSystemMetadataStore(oneDriveRoot).EnsureCurrentLineageAsync(Guid.NewGuid(), 1);
        var saved = await new M07ConfigurationSetupService(configurationService).ValidateAndPersistAsync(
            initial,
            new(oneDriveRoot, null, null, null, null, null));
        Assert.IsTrue(saved.Succeeded);

        var persisted = await configurationService.LoadAsync();
        var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var authorityStore = new JsonAuthorityStateStore(paths);
        var systemMetadata = new JsonSystemMetadataStore(persisted.OneDriveRoot!);
        await using var runtime = new M07RuntimeServices(
            guard,
            authorityStore,
            systemMetadata,
            new SelfJoinService(authorityStore, guard, systemMetadata, new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Local)),
            null,
            null,
            null,
            GitHubConnectionSetupState.RepositoryNotConfigured);

        await runtime.RefreshAuthorityStateAsync();
        Assert.IsTrue(runtime.CanSelfJoin);
        Assert.AreNotEqual(WriteAuthorityState.Authoritative, runtime.AuthorityGuard.State);
        Assert.IsFalse(File.Exists(Path.Combine(paths.ConfigDirectory, "authority-state.json")));

        var joined = await runtime.SelfJoin.JoinAsync("Fresh B");
        Assert.AreEqual(PairingReadiness.PairedUninitializedReadOnly, joined.Readiness);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, runtime.AuthorityGuard.State);
        Assert.IsNotNull(await authorityStore.LoadAsync());
    }

    private sealed class TestAppPaths : Sushi81.Pos.Application.Foundation.Paths.IAppPaths, IDisposable
    {
        public TestAppPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath { get; }

        public void EnsureInitialized()
        {
            foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true);
        }
    }
}
