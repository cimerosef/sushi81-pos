using System.Net;
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
using Sushi81.Pos.Infrastructure.GitHubTransport;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M07ProductionDisasterRecoveryServiceTests
{
    [TestMethod]
    public async Task ServiceWonCommitsPendingThenAuthoritativeAfterExactDataFirstInstall()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, state.Phase);
        Assert.AreEqual(2, state.Generation);
        CollectionAssert.Contains(fixture.SavedPhases.ToArray(), AuthorityPhase.DisasterRecoveryPending);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
        Assert.IsNotNull(state.LastRecovery);
    }

    [TestMethod]
    public async Task ServiceUnknownCreateOutcomeReobservesSameWinnerAndCompletes()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.UnknownAfterUpload = true;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.IsTrue(fixture.Transport.UnknownOutcomeWasReobserved);
        Assert.AreEqual(1, fixture.Transport.UploadCount);
        Assert.HasCount(1, fixture.Transport.ActivationAssets);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task ServiceLostToExistingWinnerFencesNewGenerationAndNeverWrites()
    {
        using var fixture = await ServiceFixture.CreateAsync(systemGeneration: 2);
        fixture.Transport.OtherWinnerOnUpload = true;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath), "A losing activation must not install business data.");
    }

    [TestMethod]
    public async Task ServiceBlockedNoWinnerLeavesExactPreparingStateReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.AddIncompleteStarter(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        fixture.Transport.AddIncompleteStarter(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(0, fixture.Transport.DeleteCount, "Contradictory occupants forbid broad cleanup.");
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task ServiceDeletesOnlyOneExactIncompleteStarterAndRetries()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.AddIncompleteStarter(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.AreEqual(1, fixture.Transport.DeleteCount);
        Assert.AreEqual(1, fixture.Transport.UploadCount);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task ServiceNeverDeletesACompleteContradictoryActivationOccupant()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.AddCompleteOccupant(fixture.Candidate.LineageId, fixture.Candidate.Generation + 1);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, fixture.Transport.DeleteCount);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    [DataRow("401")]
    [DataRow("403")]
    [DataRow("404")]
    [DataRow("5xx")]
    [DataRow("timeout")]
    public async Task ServiceTransportUnavailableOrCredentialFailureFailsClosed(string failure)
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.Transport.EnsureFailure = failure;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashBeforePreparingPersistenceDoesNotCreateRemoteActivationOrWriter()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailOnSaveNumber = 1;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(0, fixture.Transport.UploadCount);
        Assert.AreEqual(AuthorityPhase.NonAuthoritativeReadOnly, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task CrashAfterActivationBeforePendingPersistenceRestartsExactRecovery()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailOnSaveNumber = 2;
        await using (var first = fixture.CreateService())
        {
            var result = await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);
            Assert.IsFalse(result.Succeeded);
        }

        var pending = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPreparing, pending.Phase);
        Assert.IsNotNull(pending.Recovery);
        Assert.AreEqual(fixture.Candidate.CandidateId, pending.Recovery!.CandidateId);
        fixture.FailOnSaveNumber = null;

        await using var restart = fixture.CreateService();
        var resumed = await restart.RetryAsync(true);

        Assert.IsTrue(resumed.Succeeded, resumed.Diagnostic);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashAtExactCandidateStagingLeavesPendingAndReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailCandidateValidation = true;
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, fixture.Guard.State);
        Assert.IsFalse(File.Exists(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashAtSystemGenerationAdvanceLeavesInstalledDataPendingAndReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailSystemOperation = "advance";
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashAtMembershipPublicationLeavesNoAuthoritativeState()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailSystemOperation = "join";
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.Transitioning, fixture.Guard.State);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
    }

    [TestMethod]
    public async Task CrashImmediatelyBeforeAuthoritativePersistenceRestartsWithoutCandidateSubstitution()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailOnSaveNumber = 3;
        await using (var first = fixture.CreateService())
        {
            Assert.IsFalse((await first.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true)).Succeeded);
        }

        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.DisasterRecoveryPending, state.Phase);
        Assert.AreEqual(fixture.Candidate.CandidateId, state.Recovery!.CandidateId);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
        fixture.FailOnSaveNumber = null;

        await using var restart = fixture.CreateService();
        Assert.IsTrue((await restart.RetryAsync(true)).Succeeded);
        var final = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.Authoritative, final.Phase);
        Assert.AreEqual(fixture.Candidate.PayloadSha256, final.LastRecovery!.CandidateSha256);
    }

    [TestMethod]
    public async Task OptionalSeedPublicationFailureDoesNotRevokeDurableAuthority()
    {
        using var fixture = await ServiceFixture.CreateAsync();
        fixture.FailSystemOperation = "publish-seed";
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.AreEqual(AuthorityPhase.Authoritative, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
    }

    [TestMethod]
    public async Task StaleGenerationWithReadableNewerLineageRemainsReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await using var service = fixture.CreateService();

        var result = await service.StartOrResumeAsync(fixture.Candidate.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task StaleReinitializationWithoutSeedIsBlockedReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await using var service = fixture.CreateService();

        var result = await service.ReinitializeStaleDeviceAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task StaleReinitializationRejectsCorruptCurrentGenerationSeed()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        var seed = await fixture.PublishSeedAsync(22);
        File.WriteAllBytes(seed.PayloadPath, [1, 2, 3]);
        await using var service = fixture.CreateService();

        var result = await service.ReinitializeStaleDeviceAsync();

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
    }

    [TestMethod]
    public async Task SuccessfulStaleReinitializationPreservesOldDataInstallsSeedAndJoinsCurrentGenerationReadOnly()
    {
        using var fixture = await ServiceFixture.CreateAsync(phase: AuthorityPhase.StaleGeneration, systemGeneration: 2);
        await fixture.CreateLiveDatabaseAsync(3);
        var seed = await fixture.PublishSeedAsync(22);
        await using var service = fixture.CreateService();

        var result = await service.ReinitializeStaleDeviceAsync();

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.NonAuthoritativeReadOnly, state.Phase);
        Assert.AreEqual(2, state.Generation);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Guard.State);
        Assert.AreEqual(seed.Metadata.PayloadSha256, await HashAsync(fixture.Paths.LiveDatabasePath));
        Assert.AreEqual(1, fixture.SnapshotService.CreateCalls, "Old local data is preserved before replacement.");
        var devices = await fixture.SystemMetadata.ListCurrentGenerationDevicesAsync(fixture.LineageId, 2);
        Assert.IsTrue(devices.Any(device => device.DeviceId == fixture.DeviceId));
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private sealed class ServiceFixture : IDisposable
    {
        private ServiceFixture(AuthorityPhase phase, long systemGeneration)
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.Service", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Paths = new ServicePaths(Root);
            LineageId = Guid.NewGuid();
            DeviceId = Guid.NewGuid();
            Clock = new FixedClock();
            Guard = new WriteAuthorityGuard(phase == AuthorityPhase.StaleGeneration ? WriteAuthorityState.NonAuthoritativeReadOnly : WriteAuthorityState.NonAuthoritativeReadOnly);
            SystemMetadata = new FaultingSystemMetadataStore(new JsonSystemMetadataStore(Path.Combine(Root, "OneDrive"), new FixedTimeProvider(Clock.UtcNow)), this);
            ProbeStore = new JsonAuthorityStateStore(Paths, Probe);
            Store = new RecordingAuthorityStore(ProbeStore, SavedPhases);
            Transport = new ActivationTransport();
            Candidates = new ServiceCandidateDiscovery(this);
            var state = new AuthorityProtocolState(1, DeviceId, "Recovery", LineageId, 1, 0, 3, phase);
            ProbeStore.SaveAsync(new AuthorityStateDocument(2, state.WriteState, Clock.UtcNow) { Protocol = state }).GetAwaiter().GetResult();
            SaveNumber = 0;
            SystemMetadata.Inner.EnsureCurrentLineageAsync(LineageId, systemGeneration).GetAwaiter().GetResult();
            Candidate = CreateCandidate();
            Candidates.Set(Candidate);
        }

        public string Root { get; }
        public Guid LineageId { get; }
        public Guid DeviceId { get; }
        public ServicePaths Paths { get; }
        public FixedClock Clock { get; }
        public WriteAuthorityGuard Guard { get; }
        public FaultingSystemMetadataStore SystemMetadata { get; }
        public JsonAuthorityStateStore ProbeStore { get; }
        public RecordingAuthorityStore Store { get; }
        public ActivationTransport Transport { get; }
        public ServiceCandidateDiscovery Candidates { get; }
        public RecoveryCandidate Candidate { get; }
        public List<AuthorityPhase> SavedPhases { get; } = [];
        public int? FailOnSaveNumber { get; set; }
        public int SaveNumber { get; private set; }
        public string? FailSystemOperation { get; set; }
        public bool FailCandidateValidation { get; set; }
        public SnapshotRecorder SnapshotService { get; } = new();

        public static Task<ServiceFixture> CreateAsync(AuthorityPhase phase = AuthorityPhase.NonAuthoritativeReadOnly, long systemGeneration = 1) => Task.FromResult(new ServiceFixture(phase, systemGeneration));

        public DisasterRecoveryService CreateService() => new(
            Store,
            Guard,
            SystemMetadata,
            Candidates,
            new RecoveryActivationService(Transport),
            Transport,
            SnapshotService,
            Paths,
            Clock);

        public async Task<SeedVector> PublishSeedAsync(long revision)
        {
            var bytes = CreateDatabase(revision);
            var seedId = Guid.NewGuid();
            var metadata = new ReadOnlySeedMetadata(
                1, "M07", SystemMetadataContract.ReadOnlySeedArtifactKind, seedId, LineageId, 2,
                Guid.NewGuid(), revision, SystemMetadataContract.SeedPayloadFileName(seedId), bytes.LongLength,
                Convert.ToHexString(SHA256.HashData(bytes)), Clock.UtcNow, 4);
            await SystemMetadata.Inner.PublishReadOnlySeedAsync(metadata, bytes);
            return new SeedVector(metadata, Path.Combine(SystemMetadata.Inner.SystemDirectoryPath, "Seeds", "2", metadata.PayloadFileName));
        }

        public async Task CreateLiveDatabaseAsync(long revision)
        {
            Paths.EnsureInitialized();
            await File.WriteAllBytesAsync(Paths.LiveDatabasePath, CreateDatabase(revision));
        }

        private RecoveryCandidate CreateCandidate()
        {
            var path = Path.Combine(Root, "candidate.db");
            var bytes = CreateDatabase(8);
            File.WriteAllBytes(path, bytes);
            return new RecoveryCandidate(RecoveryCandidateType.OneDriveCheckpoint, "checkpoint-exact-a", LineageId, 1, Guid.NewGuid(), 8, 4, Clock.UtcNow, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)), path);
        }

        private void Probe(string eventName)
        {
            if (eventName == "before-replace")
            {
                SaveNumber++;
                if (FailOnSaveNumber == SaveNumber) throw new IOException($"synthetic crash at authority save {SaveNumber}");
            }
        }

        public void Dispose()
        {
            Guard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static byte[] CreateDatabase(long revision)
        {
            var path = Path.Combine(Path.GetTempPath(), $"m07-service-db-{Guid.NewGuid():N}.db");
            try
            {
                using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = $"CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision','{revision}');";
                    command.ExecuteNonQuery();
                }
                return File.ReadAllBytes(path);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }

    private sealed record SeedVector(ReadOnlySeedMetadata Metadata, string PayloadPath);

    private sealed class ServiceCandidateDiscovery(ServiceFixture fixture) : IRecoveryCandidateDiscovery
    {
        private RecoveryCandidate? candidate;
        public void Set(RecoveryCandidate value) => candidate = value;
        public Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(AuthorityProtocolState localState, CancellationToken cancellationToken = default)
        {
            var value = fixture.FailCandidateValidation ? candidate! with { PayloadSha256 = new string('F', 64) } : candidate!;
            return Task.FromResult(new RecoveryCandidateDiscoveryResult([value], "synthetic production-service candidate"));
        }

        public Task<RecoveryCandidate?> FindExactAsync(AuthorityProtocolState localState, string candidateId, CancellationToken cancellationToken = default)
        {
            if (candidate is not { } value || value.CandidateId != candidateId) return Task.FromResult<RecoveryCandidate?>(null);
            return Task.FromResult<RecoveryCandidate?>(fixture.FailCandidateValidation ? value with { PayloadSha256 = new string('F', 64) } : value);
        }
    }

    private sealed class RecordingAuthorityStore(IAuthorityStateStore inner, List<AuthorityPhase> savedPhases) : IAuthorityStateStore
    {
        public Task<AuthorityStateDocument?> LoadAsync(CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);
        public async Task SaveAsync(AuthorityStateDocument document, CancellationToken cancellationToken = default)
        {
            if (document.Protocol is { } protocol) savedPhases.Add(protocol.Phase);
            await inner.SaveAsync(document, cancellationToken);
        }
        public Task<bool> HasBootstrapMarkerAsync(CancellationToken cancellationToken = default) => inner.HasBootstrapMarkerAsync(cancellationToken);
        public Task WriteBootstrapMarkerAsync(CancellationToken cancellationToken = default) => inner.WriteBootstrapMarkerAsync(cancellationToken);
        public Task<bool> HasLegacyBootstrapEvidenceAsync(CancellationToken cancellationToken = default) => inner.HasLegacyBootstrapEvidenceAsync(cancellationToken);
        public Task<bool> HasBootstrapAnchorAsync(CancellationToken cancellationToken = default) => inner.HasBootstrapAnchorAsync(cancellationToken);
        public Task WriteBootstrapAnchorAsync(CancellationToken cancellationToken = default) => inner.WriteBootstrapAnchorAsync(cancellationToken);
        public Task<bool> HasEstablishedAuthorityArtifactsAsync(CancellationToken cancellationToken = default) => inner.HasEstablishedAuthorityArtifactsAsync(cancellationToken);
    }

    private sealed class FaultingSystemMetadataStore(JsonSystemMetadataStore inner, ServiceFixture fixture) : ISystemMetadataStore
    {
        public JsonSystemMetadataStore Inner { get; } = inner;
        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Inner.EnsureCurrentLineageAsync(lineageId, generation, cancellationToken);
        public Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default) => Inner.ReadLineageAsync(cancellationToken);
        public Task<SystemLineageMetadata> AdvanceGenerationAsync(Guid lineageId, long expectedPriorGeneration, long nextGeneration, CancellationToken cancellationToken = default) =>
            fixture.FailSystemOperation == "advance" ? throw new IOException("synthetic generation advance crash") : Inner.AdvanceGenerationAsync(lineageId, expectedPriorGeneration, nextGeneration, cancellationToken);
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) =>
            fixture.FailSystemOperation == "join" ? throw new IOException("synthetic membership publication crash") : Inner.JoinCurrentGenerationAsync(deviceId, displayName, cancellationToken);
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Inner.ListCurrentGenerationDevicesAsync(lineageId, generation, cancellationToken);
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => Inner.FindValidatedReadOnlySeedAsync(lineageId, generation, cancellationToken);
        public Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) =>
            fixture.FailSystemOperation == "publish-seed" ? throw new IOException("synthetic optional seed failure") : Inner.PublishReadOnlySeedAsync(metadata, payload, cancellationToken);
    }

    private sealed class SnapshotRecorder : ILocalRecoverySnapshotService
    {
        public int CreateCalls { get; private set; }
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Task.FromResult(new RecoverySnapshotResult(string.Empty, string.Empty, new string('A', 64), change.CommittedAtUtc, change.Sequence, 1));
        }
    }

    private sealed class ActivationTransport : IGitHubHandoffTransport
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
        private readonly GitHubReleaseContainer release = new(301, "sushi81-handoff-v1", "https://uploads.example/releases/301/assets{?name}", false, false);
        private readonly List<GitHubRemoteAsset> assets = [];
        private readonly Dictionary<long, byte[]> contents = [];
        private long nextId = 5000;

        public string? EnsureFailure { get; set; }
        public bool UnknownAfterUpload { get; set; }
        public bool OtherWinnerOnUpload { get; set; }
        public bool UnknownOutcomeWasReobserved { get; private set; }
        public int UploadCount { get; private set; }
        public int DeleteCount { get; private set; }
        public IReadOnlyList<GitHubRemoteAsset> ActivationAssets => assets.Where(asset => GitHubHandoffAssetNames.IsActivationName(asset.Name)).ToArray();

        public Task<GitHubReleaseContainer> EnsureContainerAsync(bool createIfMissing, CancellationToken cancellationToken = default)
        {
            if (EnsureFailure is not null) throw CreateFailure(EnsureFailure);
            return Task.FromResult(release);
        }

        public async Task<GitHubAssetReceipt> UploadAssetAsync(GitHubReleaseContainer release, string name, Stream content, long contentLength, string localSha256, CancellationToken cancellationToken = default)
        {
            UploadCount++;
            using var memory = new MemoryStream();
            await content.CopyToAsync(memory, cancellationToken);
            var bytes = memory.ToArray();
            if (OtherWinnerOnUpload)
            {
                var artifact = JsonSerializer.Deserialize<RecoveryActivationArtifact>(bytes, JsonOptions)! with { WinnerDeviceId = Guid.NewGuid() };
                bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
            }
            var id = nextId++;
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.LongLength, "uploaded", "sha256:" + hash, DateTimeOffset.UtcNow));
            contents[id] = bytes;
            if (UnknownAfterUpload)
            {
                UnknownAfterUpload = false;
                UnknownOutcomeWasReobserved = true;
                throw new TimeoutException("synthetic unknown create outcome after server commit");
            }
            return new GitHubAssetReceipt(release.Id, id, name, bytes.LongLength, "sha256:" + hash, DateTimeOffset.UtcNow, "uploaded");
        }

        public Task<IReadOnlyList<GitHubRemoteAsset>> ListAssetsAsync(GitHubReleaseContainer release, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GitHubRemoteAsset>>(assets.ToArray());
        public Task<GitHubRemoteAsset> GetAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult(assets.Single(asset => asset.Id == assetId));
        public Task<Stream> DownloadAssetAsync(long assetId, CancellationToken cancellationToken = default) => Task.FromResult<Stream>(new MemoryStream(contents[assetId], writable: false));
        public Task DeleteAssetAsync(long assetId, CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            assets.RemoveAll(asset => asset.Id == assetId);
            contents.Remove(assetId);
            return Task.CompletedTask;
        }

        public void AddIncompleteStarter(Guid lineageId, long generation)
        {
            var name = GitHubHandoffAssetNames.CreateActivationName(lineageId, generation);
            AddAsset(nextId++, name, [1], "starter");
            if (assets.Count(asset => asset.Name == name) > 2) throw new InvalidOperationException();
        }

        public void AddCompleteOccupant(Guid lineageId, long generation)
        {
            var artifact = new RecoveryActivationArtifact("M07", Guid.NewGuid(), lineageId, generation - 1, generation, Guid.NewGuid(), "other-candidate", new string('A', 64), 8, DateTimeOffset.UtcNow);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(artifact, JsonOptions);
            AddAsset(nextId++, GitHubHandoffAssetNames.CreateActivationName(lineageId, generation), bytes, "uploaded");
        }

        private void AddAsset(long id, string name, byte[] bytes, string state)
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            assets.Add(new GitHubRemoteAsset(id, name, bytes.LongLength, state, "sha256:" + hash, DateTimeOffset.UtcNow));
            contents[id] = bytes;
        }

        private static Exception CreateFailure(string failure) => failure switch
        {
            "401" => new GitHubTransportException("synthetic unauthorized", HttpStatusCode.Unauthorized),
            "403" => new GitHubTransportException("synthetic forbidden", HttpStatusCode.Forbidden),
            "404" => new GitHubTransportException("synthetic missing", HttpStatusCode.NotFound),
            "5xx" => new GitHubTransportException("synthetic server error", HttpStatusCode.BadGateway),
            "timeout" => new TimeoutException("synthetic timeout"),
            _ => new HttpRequestException("synthetic offline")
        };
    }

    private sealed class ServicePaths(string root) : IAppPaths
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

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; } = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 7);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }
}
