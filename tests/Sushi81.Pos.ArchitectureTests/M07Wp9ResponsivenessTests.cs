using System.Runtime.ExceptionServices;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Threading;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M07Wp9ResponsivenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void RealStaShellAndClosePathsRemainDispatcherResponsiveWhileM07IoAwaits()
    {
        RunOnSta(() =>
        {
            ConnectionTestRemainsResponsive();
            SelfJoinRemainsResponsive();
            DisasterRecoveryDiscoveryRemainsResponsive();
            StaleReinitializationRemainsResponsive();
            TargetDirectedCloseRemainsResponsive();
        });
    }

    private static void ConnectionTestRemainsResponsive()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var store = new TestAuthorityStateStore(UnboundReadOnlyDocument());
        var metadata = new TestSystemMetadataStore(Guid.NewGuid(), 1);
        var transport = new DelayedTransport();
        var runtime = new M07RuntimeServices(
            guard, store, metadata, new SelfJoinService(store, guard, metadata, new FixedClock()),
            null, null, new GitHubHandoffConnectionTester(transport), GitHubConnectionSetupState.Ready);
        using var shell = new ShellViewModel(
            new InMemorySelectedCultureStore(), true, authorityGuard: guard, authorityState: guard.State,
            m07Runtime: runtime, authorityPhase: AuthorityPhase.NonAuthoritativeReadOnly);

        var operation = shell.TestGitHubConnectionAsync();
        Assert.AreEqual(shell.Localized["M07ConnectionChecking"], shell.M07OperationStatus);
        PumpUntil(transport.Entered.Task);
        AssertDispatcherPulse(operation);
        Assert.IsFalse(shell.CanTestGitHubConnection);
        Assert.IsFalse(shell.CanWrite);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
        Assert.AreEqual(shell.Localized["M07ConnectionChecking"], shell.M07OperationStatus);
        transport.Release.TrySetResult(true);
        operation.GetAwaiter().GetResult();
        Assert.IsFalse(shell.CanWrite);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void SelfJoinRemainsResponsive()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.RecoveryRequired);
        var store = new TestAuthorityStateStore(null);
        var metadata = new TestSystemMetadataStore(Guid.NewGuid(), 1) { DelayReadLineage = true };
        var runtime = new M07RuntimeServices(
            guard, store, metadata, new SelfJoinService(store, guard, metadata, new FixedClock()),
            null, null, null, GitHubConnectionSetupState.RepositoryNotConfigured);
        using var shell = new ShellViewModel(
            new InMemorySelectedCultureStore(), true, authorityGuard: guard, authorityState: guard.State,
            m07Runtime: runtime, authorityPhase: AuthorityPhase.Uninitialized);

        var operation = shell.JoinExistingLineageAsync("Replacement PC");
        PumpUntil(metadata.LineageReadEntered.Task);
        AssertDispatcherPulse(operation);
        Assert.IsFalse(shell.CanJoinExistingLineage);
        Assert.IsFalse(shell.CanWrite);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, guard.State);
        Assert.IsNotNull(store.Document);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, store.Document!.State);
        Assert.AreEqual(AuthorityPhase.Uninitialized, store.Document.Protocol!.Phase);
        Assert.IsNull(store.Document.Protocol.LineageId);
        Assert.AreEqual(0, store.Document.Protocol.Generation);
        metadata.ReleaseLineage.TrySetResult(true);
        operation.GetAwaiter().GetResult();
        Assert.IsFalse(shell.CanWrite);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void DisasterRecoveryDiscoveryRemainsResponsive()
    {
        using var paths = new TestPaths();
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var store = new TestAuthorityStateStore(UnboundReadOnlyDocument());
        var metadata = new TestSystemMetadataStore(Guid.NewGuid(), 1);
        var discovery = new DelayedDiscovery();
        var recovery = new DisasterRecoveryService(
            store, guard, metadata, discovery, null, null, new EmptySnapshots(), paths, new FixedClock());
        var runtime = new M07RuntimeServices(
            guard, store, metadata, new SelfJoinService(store, guard, metadata, new FixedClock()),
            null, null, null, GitHubConnectionSetupState.RepositoryNotConfigured, recovery, discovery);
        using var shell = new ShellViewModel(
            new InMemorySelectedCultureStore(), true, authorityGuard: guard, authorityState: guard.State,
            m07Runtime: runtime, authorityPhase: AuthorityPhase.NonAuthoritativeReadOnly);

        var operation = shell.DiscoverRecoveryCandidatesAsync();
        PumpUntil(discovery.DiscoverEntered.Task);
        AssertDispatcherPulse(operation);
        Assert.IsFalse(shell.CanStartDisasterRecovery);
        Assert.IsFalse(shell.CanWrite);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
        Assert.AreEqual(AuthorityPhase.NonAuthoritativeReadOnly, store.Document!.Protocol!.Phase);
        discovery.Release.TrySetResult(true);
        operation.GetAwaiter().GetResult();
        Assert.IsEmpty(shell.RecoveryCandidates);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void StaleReinitializationRemainsResponsive()
    {
        using var paths = new TestPaths();
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
        var lineageId = Guid.NewGuid();
        var store = new TestAuthorityStateStore(StaleDocument(lineageId, 1));
        var metadata = new TestSystemMetadataStore(lineageId, 2) { DelayReadLineage = true };
        var discovery = new DelayedDiscovery();
        var recovery = new DisasterRecoveryService(
            store, guard, metadata, discovery, null, null, new EmptySnapshots(), paths, new FixedClock());
        var runtime = new M07RuntimeServices(
            guard, store, metadata, new SelfJoinService(store, guard, metadata, new FixedClock()),
            null, null, null, GitHubConnectionSetupState.RepositoryNotConfigured, recovery, discovery);
        using var shell = new ShellViewModel(
            new InMemorySelectedCultureStore(), true, authorityGuard: guard, authorityState: guard.State,
            m07Runtime: runtime, authorityPhase: AuthorityPhase.StaleGeneration);

        var operation = shell.ReinitializeStaleDeviceAsync();
        PumpUntil(metadata.LineageReadEntered.Task);
        AssertDispatcherPulse(operation);
        Assert.IsFalse(shell.CanReinitializeStaleDevice);
        Assert.IsFalse(shell.CanWrite);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, store.Document!.Protocol!.Phase);
        metadata.ReleaseLineage.TrySetResult(true);
        operation.GetAwaiter().GetResult();
        Assert.IsFalse(shell.CanWrite);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void TargetDirectedCloseRemainsResponsive()
    {
        using var paths = new TestPaths();
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var sourceDeviceId = Guid.NewGuid();
        var targetDeviceId = Guid.NewGuid();
        var lineageId = Guid.NewGuid();
        var store = new TestAuthorityStateStore(new AuthorityStateDocument(2, WriteAuthorityState.Authoritative, Now)
        {
            Protocol = new AuthorityProtocolState(1, sourceDeviceId, "Synthetic source", lineageId, 1, 0, 12, AuthorityPhase.Authoritative)
        });
        var metadata = new TestSystemMetadataStore(lineageId, 1)
        {
            CurrentDevices =
            [
                new DeviceRegistrationArtifact(1, "M07", SystemMetadataContract.DeviceArtifactKind, sourceDeviceId, "Source", lineageId, 1, Now),
                new DeviceRegistrationArtifact(1, "M07", SystemMetadataContract.DeviceArtifactKind, targetDeviceId, "Target", lineageId, 1, Now)
            ]
        };
        var transport = new DelayedHandoffTransport();
        var snapshots = new HandoffSnapshotFactory(paths.RootDirectory);
        var service = new NormalHandoffService(store, guard, metadata, snapshots, transport, new FixedClock());
        var runtime = new M07RuntimeServices(
            guard, store, metadata, new SelfJoinService(store, guard, metadata, new FixedClock()),
            service, null, null, GitHubConnectionSetupState.Ready);
        using var shell = new ShellViewModel(
            new InMemorySelectedCultureStore(), true, authorityGuard: guard, authorityState: guard.State,
            m07Runtime: runtime, authorityPhase: AuthorityPhase.Authoritative);
        var finalCloseCount = 0;
        var coordinator = new MainWindowCloseCoordinator(
            () => shell.CanWrite,
            () => Task.FromResult(new MainWindowCloseRequest(MainWindowCloseIntent.Transfer, targetDeviceId)),
            async target => (await service.TransferAndCloseAsync(target)).Succeeded,
            null,
            () => finalCloseCount++,
            exception => Assert.Fail(exception.Message));
        var closing = new System.ComponentModel.CancelEventArgs();

        var operation = coordinator.HandleClosingAsync(closing);
        PumpUntil(transport.Entered.Task);
        AssertDispatcherPulse(operation);
        Assert.IsTrue(coordinator.IsCloseInProgress);
        Assert.IsTrue(closing.Cancel);
        Assert.AreEqual(WriteAuthorityState.Transitioning, guard.State);
        Assert.Throws<WriteAuthorityException>(() => guard.RequireWriteAuthority());
        shell.RefreshAuthorityStateAsync().GetAwaiter().GetResult();
        Assert.IsFalse(shell.CanWrite);
        Assert.IsFalse(coordinator.IsFinalCloseAllowed);
        Assert.AreEqual(0, finalCloseCount);

        transport.Release.TrySetResult(true);
        operation.GetAwaiter().GetResult();

        Assert.IsTrue(coordinator.IsFinalCloseAllowed);
        Assert.AreEqual(1, finalCloseCount);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
        Assert.AreEqual(AuthorityPhase.ReleasedNonAuthoritative, store.Document!.Protocol!.Phase);
        Assert.AreEqual(targetDeviceId, store.Document.Protocol.Transfer!.TargetDeviceId);
        runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static void AssertDispatcherPulse(Task operation)
    {
        var pulse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => pulse.TrySetResult(true)));
        PumpUntil(pulse.Task);
        Assert.IsFalse(operation.IsCompleted, "The M07 operation completed before the delayed I/O gate was released.");
    }

    private static void PumpUntil(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        Assert.IsTrue(task.IsCompleted, "The delayed fake did not reach its expected await point.");
        task.GetAwaiter().GetResult();
    }

    private static AuthorityStateDocument UnboundReadOnlyDocument() => new(
        2, WriteAuthorityState.NonAuthoritativeReadOnly, Now)
    {
        Protocol = new AuthorityProtocolState(1, Guid.NewGuid(), "Synthetic", null, 0, 0, 0, AuthorityPhase.NonAuthoritativeReadOnly)
    };

    private static AuthorityStateDocument StaleDocument(Guid lineageId, long generation) => new(
        2, WriteAuthorityState.NonAuthoritativeReadOnly, Now)
    {
        Protocol = new AuthorityProtocolState(1, Guid.NewGuid(), "Synthetic stale", lineageId, generation, 0, 0, AuthorityPhase.StaleGeneration)
    };

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class DelayedTransport : IGitHubHandoffTransport
    {
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new GitHubReleaseContainer(1, "synthetic", "https://uploads.example.test/release", false, false);
        }
        public Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class DelayedHandoffTransport : IGitHubHandoffTransport
    {
        private readonly List<GitHubRemoteAsset> assets = [];
        private long nextAssetId = 1;
        private int delayedEnsure;

        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref delayedEnsure, 1) == 0)
            {
                Entered.TrySetResult(true);
                await Release.Task.WaitAsync(cancellationToken);
            }

            return new GitHubReleaseContainer(1, "synthetic", "https://uploads.example.test/release", false, false);
        }

        public async Task<GitHubAssetReceipt> UploadAssetAsync(
            GitHubReleaseContainer release,
            string name,
            Stream content,
            long contentLength,
            string localSha256,
            CancellationToken cancellationToken = default)
        {
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            var digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var asset = new GitHubRemoteAsset(nextAssetId++, name, bytes.LongLength, "uploaded", digest, Now);
            assets.Add(asset);
            return new GitHubAssetReceipt(release.Id, asset.Id, name, bytes.LongLength, digest, Now, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());

        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(assets.Single(asset => asset.Id == assetId));

        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            assets.RemoveAll(asset => asset.Id == assetId);
            return Task.CompletedTask;
        }
    }

    private sealed class HandoffSnapshotFactory(string root) : ITransferSnapshotFactory
    {
        public async Task<TransferSnapshot> CreateAsync(AuthorityProtocolState source, Guid transferId, CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, $"snapshot-{transferId:N}.db");
            var bytes = new byte[] { 7, 8, 9, 10 };
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            return new TransferSnapshot(
                "20260908120000.snapshot.db",
                path,
                bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)),
                source.BusinessRevision);
        }
    }

    private sealed class DelayedDiscovery : IRecoveryCandidateDiscovery
    {
        public TaskCompletionSource<bool> DiscoverEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(AuthorityProtocolState localState, CancellationToken cancellationToken = default)
        {
            DiscoverEntered.TrySetResult(true);
            await Release.Task.WaitAsync(cancellationToken);
            return new([], "synthetic delayed discovery");
        }
        public Task<RecoveryCandidate?> FindExactAsync(AuthorityProtocolState localState, string candidateId, CancellationToken cancellationToken = default) => Task.FromResult<RecoveryCandidate?>(null);
    }

    private sealed class TestAuthorityStateStore(AuthorityStateDocument? initial) : IAuthorityStateStore
    {
        public AuthorityStateDocument? Document { get; private set; } = initial;
        public bool Marker { get; private set; }
        public bool Anchor { get; private set; }
        public Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Document);
        public Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default) { Document = document; return Task.CompletedTask; }
        public Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default) => Task.FromResult(Marker);
        public Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default) { Marker = true; return Task.CompletedTask; }
        public Task<bool> HasBootstrapAnchorAsync(CancellationToken cancellationToken = default) => Task.FromResult(Anchor);
        public Task WriteBootstrapAnchorAsync(CancellationToken cancellationToken = default) { Anchor = true; return Task.CompletedTask; }
    }

    private sealed class TestSystemMetadataStore(Guid lineageId, long generation) : ISystemMetadataStore
    {
        public bool DelayReadLineage { get; init; }
        public IReadOnlyList<DeviceRegistrationArtifact> CurrentDevices { get; init; } = [];
        public TaskCompletionSource<bool> LineageReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseLineage { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private SystemLineageMetadata Lineage => new(1, "M07", lineageId, generation, Now);

        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid requestedLineageId, long requestedGeneration, CancellationToken cancellationToken = default) => Task.FromResult(Lineage);
        public async Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default)
        {
            if (DelayReadLineage)
            {
                LineageReadEntered.TrySetResult(true);
                await ReleaseLineage.Task.WaitAsync(cancellationToken);
            }
            return Lineage;
        }
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) => Task.FromResult(
            new DeviceSelfJoinResult(
                new DeviceRegistrationArtifact(1, "M07", SystemMetadataContract.DeviceArtifactKind, deviceId, displayName, lineageId, generation, Now),
                true, null));
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid requestedLineageId, long requestedGeneration, CancellationToken cancellationToken = default) => Task.FromResult(CurrentDevices);
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid requestedLineageId, long requestedGeneration, CancellationToken cancellationToken = default) => Task.FromResult<ValidatedReadOnlySeed?>(null);
        public Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptySnapshots : ILocalRecoverySnapshotService
    {
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => Now;
        public DateOnly BusinessDate => DateOnly.FromDateTime(Now.DateTime);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TestPaths : IAppPaths, IDisposable
    {
        public TestPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M07.WP9", Guid.NewGuid().ToString("N"));
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
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, recursive: true); }
    }
}
