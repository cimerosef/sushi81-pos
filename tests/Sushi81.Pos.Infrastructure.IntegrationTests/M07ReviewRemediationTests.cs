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
    public async Task PendingTransferResumesThroughExactPersistedIdentityWithoutRetargeting()
    {
        using var fixture = await ReviewFixture.CreateAsync(0);
        fixture.Transport.LoseFirstGrantResponse = true;
        await using (var first = fixture.CreateHandoff())
            Assert.IsFalse((await first.TransferAndCloseAsync(fixture.TargetDeviceId)).Succeeded);

        await using var retry = fixture.CreateHandoff();
        var result = await retry.ResumePendingTransferAsync();

        Assert.IsTrue(result.Succeeded, result.Error?.ToString());
        Assert.AreEqual(fixture.TargetDeviceId, (await fixture.Store.LoadAsync())!.Protocol!.Transfer!.TargetDeviceId);
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

    [TestMethod]
    public async Task DisasterRecoveryRejectsLowerRankedCandidateBeforePreparing()
    {
        using var fixture = await DisasterRecoveryFixture.CreateAsync(AuthorityPhase.NonAuthoritativeReadOnly);
        var older = fixture.Candidate("older", 4, 20);
        var newer = fixture.Candidate("newer", 5, 1);
        fixture.Candidates.Set(older, newer);

        await using var service = fixture.CreateService();
        var result = await service.StartOrResumeAsync(older.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Diagnostic, "deterministic freshest");
        Assert.AreEqual(AuthorityPhase.NonAuthoritativeReadOnly, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(0, fixture.Candidates.FindExactCalls);
    }

    [TestMethod]
    public async Task DisasterRecoveryRejectsLowerHandoffAtEqualBusinessRevision()
    {
        using var fixture = await DisasterRecoveryFixture.CreateAsync(AuthorityPhase.NonAuthoritativeReadOnly);
        var older = fixture.Candidate("older-handoff", 5, 8);
        var newer = fixture.Candidate("newer-handoff", 5, 9);
        fixture.Candidates.Set(older, newer);

        await using var service = fixture.CreateService();
        var result = await service.StartOrResumeAsync(older.CandidateId, true, true);

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Diagnostic, "deterministic freshest");
        Assert.AreEqual(AuthorityPhase.NonAuthoritativeReadOnly, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(0, fixture.Candidates.FindExactCalls);
    }

    [TestMethod]
    public async Task DisasterRecoveryPendingRestartUsesOnlyThePersistedCandidateIdentity()
    {
        using var fixture = await DisasterRecoveryFixture.CreateAsync(AuthorityPhase.NonAuthoritativeReadOnly);
        var exact = fixture.Candidate("persisted-exact", 7, 12);
        var other = fixture.Candidate("operator-retarget", 99, 99);
        fixture.Candidates.Set(exact, other);
        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        var recovery = new RecoveryActivationEvidence(
            Guid.NewGuid(), state.DeviceId, fixture.LineageId, 1, 2,
            exact.CandidateId, exact.PayloadSha256, exact.BusinessRevision,
            new RemoteAssetEvidence(1, 20, "recovery-grant.json", 1, new string('B', 64)),
            exact.TypeName, exact.StorageReference, exact.HandoffVersion, exact.CreatedAtUtc);
        await fixture.Store.SaveAsync(new AuthorityStateDocument(2, WriteAuthorityState.Transitioning, fixture.Clock.UtcNow)
        {
            Protocol = state with { Phase = AuthorityPhase.DisasterRecoveryPending, Recovery = recovery }
        });

        await using var service = fixture.CreateService();
        var result = await service.StartOrResumeAsync(other.CandidateId, false, true);

        Assert.IsFalse(result.Succeeded, "The fixture has no online activation, but the exact persisted candidate must be the only one examined.");
        StringAssert.Contains(result.Diagnostic, "activation");
        Assert.AreEqual(1, fixture.Candidates.FindExactCalls);
    }

    [TestMethod]
    public async Task DisasterRecoveryRequiresBothNormalPathAndQuarantineConfirmations()
    {
        using var fixture = await DisasterRecoveryFixture.CreateAsync(AuthorityPhase.PairedUninitializedReadOnly);
        var candidate = fixture.Candidate("selected", 7, 2);
        fixture.Candidates.Set(candidate);
        await using var service = fixture.CreateService();

        var noNormalPath = await service.StartOrResumeAsync(candidate.CandidateId, false, true);
        var noQuarantine = await service.StartOrResumeAsync(candidate.CandidateId, true, false);

        Assert.IsFalse(noNormalPath.Succeeded);
        Assert.IsFalse(noQuarantine.Succeeded);
        Assert.AreEqual(AuthorityPhase.PairedUninitializedReadOnly, (await fixture.Store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task DisasterRecoveryEntryContextBindsIrreversibleTransferTarget()
    {
        using var fixture = await DisasterRecoveryFixture.CreateAsync(AuthorityPhase.ReleasedNonAuthoritative);
        var state = (await fixture.Store.LoadAsync())!.Protocol!;
        var transfer = new TransferEvidence(
            Guid.NewGuid(), fixture.LineageId, 1, 8, fixture.SourceDeviceId, fixture.TargetDeviceId, 7,
            "snapshot.db", "snapshot.db", 3, new string('A', 64),
            new RemoteAssetEvidence(1, 10, "snapshot.db", 3, new string('A', 64)),
            new RemoteAssetEvidence(1, 11, "snapshot.db.grant.json", 3, new string('B', 64)),
            fixture.Clock.UtcNow);
        await fixture.Store.SaveAsync(new AuthorityStateDocument(2, WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Clock.UtcNow)
        {
            Protocol = state with
            {
                DeviceId = fixture.SourceDeviceId,
                Phase = AuthorityPhase.ReleasedNonAuthoritative,
                HandoffVersion = 8,
                BusinessRevision = 7,
                Transfer = transfer
            }
        });

        await using var service = fixture.CreateService();
        var context = await service.GetEntryContextAsync();

        Assert.AreEqual(AuthorityPhase.ReleasedNonAuthoritative, context.Phase);
        Assert.AreEqual(fixture.TargetDeviceId, context.PriorTargetDeviceId);
        Assert.AreEqual(8L, context.PriorHandoffVersion);
        Assert.IsTrue(context.NormalPathUnavailableConfirmationRequired);
    }

    [TestMethod]
    public async Task CurrentGenerationSeedPublicationCanRetryAfterAnInitialFailureWithoutRevokingAuthority()
    {
        using var fixture = await ReviewFixture.CreateAsync(4);
        var paths = new TestPaths(fixture.Root);
        paths.EnsureInitialized();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.LiveDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5); CREATE TABLE foundation_metadata(key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL); INSERT INTO foundation_metadata(key,value) VALUES ('business_data_revision','4');";
            await command.ExecuteNonQueryAsync();
        }
        var metadata = new SeedRetrySystemMetadataStore(fixture.SystemMetadata);
        await using var service = new DisasterRecoveryService(
            fixture.Store,
            fixture.Guard,
            metadata,
            new CandidateDiscoveryFake(),
            null,
            null,
            new NoOpSnapshotService(),
            paths,
            fixture.Clock);
        var state = (await fixture.Store.LoadAsync())!.Protocol!;

        Assert.IsFalse(await service.TryPublishCurrentGenerationSeedAsync(state));
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
        Assert.IsTrue(await service.TryPublishCurrentGenerationSeedAsync(state));
        Assert.IsTrue(await service.TryPublishCurrentGenerationSeedAsync(state));
        Assert.AreEqual(2, metadata.PublishAttempts, "The later startup/technical retry publishes once, then recognizes the exact current seed.");
        Assert.AreEqual(WriteAuthorityState.Authoritative, fixture.Guard.State);
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

    private sealed class DisasterRecoveryFixture : IDisposable
    {
        private DisasterRecoveryFixture(AuthorityPhase phase)
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.DR.Review", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Clock = new FixedClock();
            Guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            Store = new JsonAuthorityStateStore(new TestPaths(Root));
            Candidates = new CandidateDiscoveryFake();
            Phase = phase;
        }

        public string Root { get; }
        public Guid LineageId { get; } = Guid.NewGuid();
        public Guid SourceDeviceId { get; } = Guid.NewGuid();
        public Guid TargetDeviceId { get; } = Guid.NewGuid();
        public FixedClock Clock { get; }
        public WriteAuthorityGuard Guard { get; }
        public JsonAuthorityStateStore Store { get; }
        public CandidateDiscoveryFake Candidates { get; }
        public AuthorityPhase Phase { get; }

        public static async Task<DisasterRecoveryFixture> CreateAsync(AuthorityPhase phase)
        {
            var fixture = new DisasterRecoveryFixture(phase);
            var storedPhase = phase == AuthorityPhase.ReleasedNonAuthoritative
                ? AuthorityPhase.NonAuthoritativeReadOnly
                : phase;
            var state = new AuthorityProtocolState(
                1, fixture.TargetDeviceId, "Recovery", fixture.LineageId, 1, 0, 0, storedPhase);
            if (storedPhase == AuthorityPhase.PairedUninitializedReadOnly)
                state = state with { HandoffVersion = 0, BusinessRevision = 0 };
            await fixture.Store.SaveAsync(new AuthorityStateDocument(2, state.WriteState, fixture.Clock.UtcNow) { Protocol = state });
            return fixture;
        }

        public RecoveryCandidate Candidate(string id, long revision, long handoff) => new(
            RecoveryCandidateType.OneDriveCheckpoint, id, LineageId, 1, SourceDeviceId, revision, handoff,
            Clock.UtcNow, 1, new string('A', 64), id);

        public DisasterRecoveryService CreateService() => new(
            Store,
            Guard,
            new UnsupportedSystemMetadataStore(),
            Candidates,
            null,
            null,
            new NoOpSnapshotService(),
            new TestPaths(Root),
            Clock);

        public void Dispose()
        {
            Guard.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class CandidateDiscoveryFake : IRecoveryCandidateDiscovery
    {
        private IReadOnlyList<RecoveryCandidate> candidates = Array.Empty<RecoveryCandidate>();
        public int FindExactCalls { get; private set; }
        public void Set(params RecoveryCandidate[] values) => candidates = values;

        public Task<RecoveryCandidateDiscoveryResult> DiscoverAsync(AuthorityProtocolState localState, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoveryCandidateDiscoveryResult(
                candidates.OrderByDescending(c => c.BusinessRevision).ThenByDescending(c => c.HandoffVersion).ThenBy(c => c.CandidateId, StringComparer.Ordinal).ToArray(),
                "synthetic"));

        public Task<RecoveryCandidate?> FindExactAsync(AuthorityProtocolState localState, string candidateId, CancellationToken cancellationToken = default)
        {
            FindExactCalls++;
            return Task.FromResult(candidates.SingleOrDefault(candidate => candidate.CandidateId == candidateId));
        }
    }

    private sealed class UnsupportedSystemMetadataStore : ISystemMetadataStore
    {
        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default) => throw new SystemMetadataUnavailableException("synthetic");
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => throw new SystemMetadataUnavailableException("synthetic");
        public Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SeedRetrySystemMetadataStore(JsonSystemMetadataStore inner) : ISystemMetadataStore
    {
        private bool failFirstPublish = true;

        public int PublishAttempts { get; private set; }

        public Task<SystemLineageMetadata> EnsureCurrentLineageAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => inner.EnsureCurrentLineageAsync(lineageId, generation, cancellationToken);
        public Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default) => inner.ReadLineageAsync(cancellationToken);
        public Task<SystemLineageMetadata> AdvanceGenerationAsync(Guid lineageId, long expectedPriorGeneration, long nextGeneration, CancellationToken cancellationToken = default) => inner.AdvanceGenerationAsync(lineageId, expectedPriorGeneration, nextGeneration, cancellationToken);
        public Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(Guid deviceId, string displayName, CancellationToken cancellationToken = default) => inner.JoinCurrentGenerationAsync(deviceId, displayName, cancellationToken);
        public Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => inner.ListCurrentGenerationDevicesAsync(lineageId, generation, cancellationToken);
        public Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(Guid lineageId, long generation, CancellationToken cancellationToken = default) => inner.FindValidatedReadOnlySeedAsync(lineageId, generation, cancellationToken);

        public async Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(ReadOnlySeedMetadata metadata, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
        {
            PublishAttempts++;
            if (failFirstPublish)
            {
                failFirstPublish = false;
                throw new IOException("synthetic first seed publication failure");
            }

            return await inner.PublishReadOnlySeedAsync(metadata, payload, cancellationToken);
        }
    }

    private sealed class NoOpSnapshotService : ILocalRecoverySnapshotService
    {
        public Task<RecoverySnapshotResult> CreateAsync(DurableChange change, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RecoverySnapshotResult(string.Empty, string.Empty, new string('A', 64), DateTimeOffset.UtcNow, change.Sequence, 1));
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
