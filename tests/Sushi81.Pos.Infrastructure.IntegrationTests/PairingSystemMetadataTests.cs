using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class PairingSystemMetadataTests
{
    [TestMethod]
    public async Task GenerationAdvanceIsExactAndIdempotent()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var store = fixture.CreateStore();

        var advanced = await store.AdvanceGenerationAsync(lineage.LineageId, 1, 2);
        var retry = await store.AdvanceGenerationAsync(lineage.LineageId, 1, 2);

        Assert.AreEqual(2L, advanced.CurrentGeneration);
        Assert.AreEqual(advanced, retry);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdvanceGenerationAsync(lineage.LineageId, 1, 3));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdvanceGenerationAsync(Guid.NewGuid(), 2, 3));
    }

    [TestMethod]
    public async Task StartupObservingNewerSystemGenerationPersistsStaleReadOnlyFence()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage() with { CurrentGeneration = 2 };
        await fixture.WriteLineageAsync(lineage);
        var paths = new TestPaths(fixture.Root);
        var authorityStore = new JsonAuthorityStateStore(paths);
        var local = new AuthorityProtocolState(
            1, Guid.NewGuid(), "Old device", lineage.LineageId, 1, 4, 12, AuthorityPhase.Authoritative);
        await authorityStore.SaveAsync(new AuthorityStateDocument(2, local.WriteState, fixture.Now) { Protocol = local });
        await authorityStore.WriteBootstrapMarkerAsync();
        await authorityStore.WriteBootstrapAnchorAsync();
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);

        var result = await new AuthorityStateCoordinator(
            authorityStore,
            guard,
            new FixedBusinessClock(fixture.Now),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthorityStateCoordinator>.Instance,
            fixture.CreateStore()).InitializeAsync(true, true);

        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, result.State);
        Assert.AreEqual(AuthorityPhase.StaleGeneration, (await authorityStore.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
    }

    [TestMethod]
    public async Task SelfJoinIsIdempotentAndAlwaysReadOnly()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var store = fixture.CreateStore();
        var deviceId = Guid.NewGuid();

        var first = await store.JoinCurrentGenerationAsync(deviceId, "Replacement PC");
        var retry = await store.JoinCurrentGenerationAsync(deviceId, "A different local label");

        Assert.IsTrue(first.RegistrationCreated);
        Assert.IsFalse(retry.RegistrationCreated);
        Assert.AreEqual(deviceId, retry.Registration.DeviceId);
        Assert.AreEqual("Replacement PC", retry.Registration.DisplayName, "An immutable registration is never overwritten by a retry.");
        Assert.AreEqual(PairingReadiness.PairedUninitializedReadOnly, first.Readiness);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, first.WriteAuthorityState);
        Assert.AreNotEqual(WriteAuthorityState.Authoritative, first.WriteAuthorityState);

        var devices = await store.ListCurrentGenerationDevicesAsync(lineage.LineageId, lineage.CurrentGeneration);
        Assert.HasCount(1, devices);
        var artifactJson = await File.ReadAllTextAsync(fixture.DeviceArtifactPath(lineage.CurrentGeneration, deviceId));
        Assert.IsFalse(artifactJson.Contains("authority", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(artifactJson.Contains("grant", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ProductionSelfJoinPersistsCanonicalReadOnlyStateWithoutManualTransition()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);

        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Uninitialized);
        var authorityStore = new JsonAuthorityStateStore(new TestPaths(fixture.Root));
        var service = new SelfJoinService(
            authorityStore,
            guard,
            fixture.CreateStore(),
            new FixedBusinessClock(fixture.Now));

        var result = await service.JoinAsync("Replacement PC");
        var document = await authorityStore.LoadAsync();

        Assert.AreEqual(PairingReadiness.PairedUninitializedReadOnly, result.Readiness);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, result.WriteAuthorityState);
        Assert.IsNotNull(document);
        Assert.AreEqual(AuthorityPhase.PairedUninitializedReadOnly, document!.Protocol!.Phase);
        Assert.AreEqual(lineage.LineageId, document.Protocol.LineageId);
        Assert.AreEqual(lineage.CurrentGeneration, document.Protocol.Generation);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
        Assert.AreNotEqual(WriteAuthorityState.Authoritative, guard.State);

        var devices = await fixture.CreateStore().ListCurrentGenerationDevicesAsync(
            lineage.LineageId,
            lineage.CurrentGeneration);
        Assert.HasCount(1, devices);
        Assert.AreEqual(result.Registration.DeviceId, devices[0].DeviceId);
    }

    [TestMethod]
    public async Task FreshSelfJoinEstablishesIndependentEvidenceAndRestartsReadOnly()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var paths = new TestPaths(fixture.Root);
        var store = new JsonAuthorityStateStore(paths);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Uninitialized);

        var first = await new SelfJoinService(store, guard, fixture.CreateStore(), new FixedBusinessClock(fixture.Now))
            .JoinAsync("Fresh B");
        var firstDocument = await store.LoadAsync();
        Assert.IsTrue(await store.HasBootstrapMarkerAsync());
        Assert.IsTrue(await store.HasBootstrapAnchorAsync());
        Assert.AreEqual(AuthorityPhase.PairedUninitializedReadOnly, firstDocument!.Protocol!.Phase);
        Assert.AreEqual(0L, firstDocument.Protocol.BusinessRevision);

        using var restartedGuard = new WriteAuthorityGuard();
        var restarted = await new AuthorityStateCoordinator(
            store,
            restartedGuard,
            new FixedBusinessClock(fixture.Now),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthorityStateCoordinator>.Instance)
            .InitializeAsync(legacyBootstrapEvidence: true, preMigrationLiveDatabaseEvidence: true);

        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, restarted.State);
        Assert.AreEqual(first.Registration.DeviceId, (await store.LoadAsync())!.Protocol!.DeviceId);
        Assert.Throws<WriteAuthorityException>(() => restartedGuard.RequireWriteAuthority());
    }

    [TestMethod]
    public async Task SelfJoinFailureAfterIdentityEvidenceCanRetrySameDevice()
    {
        using var fixture = new SystemMetadataFixture();
        var paths = new TestPaths(fixture.Root);
        var store = new JsonAuthorityStateStore(paths);
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Uninitialized);
        var service = new SelfJoinService(store, guard, fixture.CreateStore(), new FixedBusinessClock(fixture.Now));

        await Assert.ThrowsAsync<SystemMetadataUnavailableException>(() => service.JoinAsync("Retry B"));
        var failed = await store.LoadAsync();
        var deviceId = failed!.Protocol!.DeviceId;
        Assert.AreEqual(AuthorityPhase.Uninitialized, failed.Protocol.Phase);

        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var retry = await service.JoinAsync("Retry B");
        Assert.AreEqual(deviceId, retry.Registration.DeviceId);
        Assert.AreEqual(AuthorityPhase.PairedUninitializedReadOnly, (await store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task EstablishedRecoveryRequiredCannotBeResetBySelfJoin()
    {
        using var fixture = new SystemMetadataFixture();
        var paths = new TestPaths(fixture.Root);
        var store = new JsonAuthorityStateStore(paths);
        var deviceId = Guid.NewGuid();
        var lineageId = Guid.NewGuid();
        await store.SaveAsync(new AuthorityStateDocument(2, WriteAuthorityState.RecoveryRequired, fixture.Now)
        {
            Protocol = new AuthorityProtocolState(1, deviceId, "Established", lineageId, 1, 4, 8, AuthorityPhase.RecoveryRequired)
        });
        await store.WriteBootstrapMarkerAsync();
        await store.WriteBootstrapAnchorAsync();
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.RecoveryRequired);
        var service = new SelfJoinService(store, guard, fixture.CreateStore(), new FixedBusinessClock(fixture.Now));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.JoinAsync("Must not reset"));
        Assert.AreEqual(AuthorityPhase.RecoveryRequired, (await store.LoadAsync())!.Protocol!.Phase);
        Assert.AreEqual(WriteAuthorityState.RecoveryRequired, guard.State);
    }

    [TestMethod]
    public async Task SafeUnboundM06ReadOnlyStateCanSelfJoinWithoutPromotion()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var paths = new TestPaths(fixture.Root);
        var store = new JsonAuthorityStateStore(paths);
        var deviceId = Guid.NewGuid();
        await store.SaveAsync(new AuthorityStateDocument(2, WriteAuthorityState.NonAuthoritativeReadOnly, fixture.Now)
        {
            Protocol = new AuthorityProtocolState(1, deviceId, "Legacy", null, 0, 0, 0, AuthorityPhase.NonAuthoritativeReadOnly)
        });
        await store.WriteBootstrapMarkerAsync();
        await store.WriteBootstrapAnchorAsync();
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);

        var result = await new SelfJoinService(store, guard, fixture.CreateStore(), new FixedBusinessClock(fixture.Now))
            .JoinAsync("Legacy joined");

        Assert.AreEqual(deviceId, result.Registration.DeviceId);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
        Assert.AreEqual(AuthorityPhase.PairedUninitializedReadOnly, (await store.LoadAsync())!.Protocol!.Phase);
    }

    [TestMethod]
    public async Task ProductionSelfJoinDoesNotClaimValidatedSeedUntilItIsInstalled()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var systemStore = fixture.CreateStore();
        var payload = await fixture.CreateSyntheticSqlitePayloadAsync();
        var seedId = Guid.NewGuid();
        await systemStore.PublishReadOnlySeedAsync(new ReadOnlySeedMetadata(
            SystemMetadataContract.SchemaVersion,
            SystemMetadataContract.ProtocolVersion,
            SystemMetadataContract.ReadOnlySeedArtifactKind,
            seedId,
            lineage.LineageId,
            lineage.CurrentGeneration,
            Guid.NewGuid(),
            99,
            SystemMetadataContract.SeedPayloadFileName(seedId),
            payload.LongLength,
            Convert.ToHexString(SHA256.HashData(payload)),
            fixture.Now), payload);

        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Uninitialized);
        var authorityStore = new JsonAuthorityStateStore(new TestPaths(fixture.Root));
        var result = await new SelfJoinService(
            authorityStore,
            guard,
            systemStore,
            new FixedBusinessClock(fixture.Now)).JoinAsync("Seed not yet installed");

        Assert.IsNull(result.Seed);
        var persisted = (await authorityStore.LoadAsync())!.Protocol!;
        Assert.AreEqual(AuthorityPhase.PairedUninitializedReadOnly, persisted.Phase);
        Assert.AreEqual(0L, persisted.BusinessRevision);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, guard.State);
    }

    [TestMethod]
    public async Task IndependentAndDuplicateRegistrationsUseNonOverwritingSemantics()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var storeA = fixture.CreateStore();
        var storeB = fixture.CreateStore();
        var sameDevice = Guid.NewGuid();

        var sameDeviceResults = await Task.WhenAll(
            storeA.JoinCurrentGenerationAsync(sameDevice, "Same device"),
            storeB.JoinCurrentGenerationAsync(sameDevice, "Same device"));
        var independentResults = await Task.WhenAll(
            storeA.JoinCurrentGenerationAsync(Guid.NewGuid(), "Device B"),
            storeB.JoinCurrentGenerationAsync(Guid.NewGuid(), "Device C"));

        Assert.HasCount(1, sameDeviceResults.Where(result => result.RegistrationCreated));
        Assert.IsTrue(independentResults.All(result => result.RegistrationCreated));
        var devices = await storeA.ListCurrentGenerationDevicesAsync(lineage.LineageId, lineage.CurrentGeneration);
        Assert.HasCount(3, devices);
    }

    [TestMethod]
    public async Task ContradictoryRegistrationIdentityAndGenerationFailClosed()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var deviceId = Guid.NewGuid();
        var contradictory = new DeviceRegistrationArtifact(
            SystemMetadataContract.SchemaVersion,
            SystemMetadataContract.ProtocolVersion,
            SystemMetadataContract.DeviceArtifactKind,
            deviceId,
            "Synthetic device",
            Guid.NewGuid(),
            lineage.CurrentGeneration,
            fixture.Now);
        await SystemMetadataFixture.WriteJsonAsync(
            fixture.DeviceArtifactPath(lineage.CurrentGeneration, deviceId),
            contradictory);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => fixture.CreateStore().JoinCurrentGenerationAsync(deviceId, "Synthetic device"));

        using var generationFixture = new SystemMetadataFixture();
        var generationLineage = generationFixture.CreateLineage();
        await generationFixture.WriteLineageAsync(generationLineage);
        var futureDevice = Guid.NewGuid();
        var futureArtifact = new DeviceRegistrationArtifact(
            SystemMetadataContract.SchemaVersion,
            SystemMetadataContract.ProtocolVersion,
            SystemMetadataContract.DeviceArtifactKind,
            futureDevice,
            "Future device",
            generationLineage.LineageId,
            generationLineage.CurrentGeneration + 1,
            generationFixture.Now);
        await SystemMetadataFixture.WriteJsonAsync(
            generationFixture.DeviceArtifactPath(generationLineage.CurrentGeneration, futureDevice),
            futureArtifact);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => generationFixture.CreateStore().ListCurrentGenerationDevicesAsync(
                generationLineage.LineageId,
                generationLineage.CurrentGeneration));
    }

    [TestMethod]
    public async Task ReadOnlySeedIsIntegrityValidatedAndCannotCreateAuthority()
    {
        using var fixture = new SystemMetadataFixture();
        var lineage = fixture.CreateLineage();
        await fixture.WriteLineageAsync(lineage);
        var store = fixture.CreateStore();
        var payload = await fixture.CreateSyntheticSqlitePayloadAsync();
        var seedId = Guid.NewGuid();
        var metadata = new ReadOnlySeedMetadata(
            SystemMetadataContract.SchemaVersion,
            SystemMetadataContract.ProtocolVersion,
            SystemMetadataContract.ReadOnlySeedArtifactKind,
            seedId,
            lineage.LineageId,
            lineage.CurrentGeneration,
            Guid.NewGuid(),
            12,
            SystemMetadataContract.SeedPayloadFileName(seedId),
            payload.LongLength,
            Convert.ToHexString(SHA256.HashData(payload)),
            fixture.Now);

        var first = await store.PublishReadOnlySeedAsync(metadata, payload);
        var retry = await store.PublishReadOnlySeedAsync(metadata, payload);
        var joined = await store.JoinCurrentGenerationAsync(Guid.NewGuid(), "Seeded read-only PC");

        Assert.IsTrue(first.Created);
        Assert.IsFalse(retry.Created);
        Assert.IsNotNull(joined.Seed);
        Assert.AreEqual(PairingReadiness.NonAuthoritativeReadOnly, joined.Readiness);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, joined.WriteAuthorityState);
        Assert.AreNotEqual(WriteAuthorityState.Authoritative, joined.WriteAuthorityState);
        var persistedPayload = await File.ReadAllBytesAsync(joined.Seed!.PayloadPath);
        Assert.IsTrue(payload.AsSpan().SequenceEqual(persistedPayload));

        var corruptedPayload = Encoding.UTF8.GetBytes("corrupted synthetic seed");
        await File.WriteAllBytesAsync(joined.Seed.PayloadPath, corruptedPayload);
        var noSeedAfterCorruption = await store.JoinCurrentGenerationAsync(Guid.NewGuid(), "Uninitialized read-only PC");
        Assert.IsNull(noSeedAfterCorruption.Seed);
        Assert.AreEqual(PairingReadiness.PairedUninitializedReadOnly, noSeedAfterCorruption.Readiness);
        Assert.AreEqual(WriteAuthorityState.NonAuthoritativeReadOnly, noSeedAfterCorruption.WriteAuthorityState);

        var wrongGeneration = metadata with { Generation = lineage.CurrentGeneration + 1 };
        await Assert.ThrowsAsync<InvalidDataException>(
            () => store.PublishReadOnlySeedAsync(wrongGeneration, payload));
    }

    private sealed class SystemMetadataFixture : IDisposable
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public SystemMetadataFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "Sushi81.POS.M07.SystemMetadata", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Now = new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
        }

        public string Root { get; }

        public DateTimeOffset Now { get; }

        public SystemLineageMetadata CreateLineage() => new(
            SystemMetadataContract.SchemaVersion,
            SystemMetadataContract.ProtocolVersion,
            Guid.NewGuid(),
            1,
            Now);

        public JsonSystemMetadataStore CreateStore() => new(Root, new FixedTimeProvider(Now));

        public string DeviceArtifactPath(long generation, Guid deviceId) => Path.Combine(
            Root,
            SystemMetadataContract.SystemDirectoryName,
            SystemMetadataContract.DevicesDirectoryName,
            generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SystemMetadataContract.DeviceFileName(deviceId));

        public async Task WriteLineageAsync(SystemLineageMetadata lineage)
        {
            await WriteJsonAsync(
                Path.Combine(
                    Root,
                    SystemMetadataContract.SystemDirectoryName,
                    SystemMetadataContract.LineageDirectoryName,
                    SystemMetadataContract.LineageFileName),
                lineage);
        }

        public async Task<byte[]> CreateSyntheticSqlitePayloadAsync()
        {
            var path = Path.Combine(Root, "seed-build.db");
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString()))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE schema_migrations(version INTEGER NOT NULL); INSERT INTO schema_migrations(version) VALUES (5);";
                await command.ExecuteNonQueryAsync();
            }

            var bytes = await File.ReadAllBytesAsync(path);
            File.Delete(path);
            return bytes;
        }

        public static async Task WriteJsonAsync<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, SerializerOptions));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedBusinessClock(DateTimeOffset now) : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; } = now;
        public DateOnly BusinessDate => DateOnly.FromDateTime(UtcNow.DateTime);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
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

        public void EnsureInitialized() => Directory.CreateDirectory(ConfigDirectory);
    }
}
