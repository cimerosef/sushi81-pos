using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M07ConfigurationSetupTests
{
    [TestMethod]
    public async Task FreshDefaultConfigurationCanValidateAndPersistSharedRootAndNonSecretTransportValues()
    {
        using var paths = new TestAppPaths();
        var configurationService = new JsonLocalConfigurationService(paths);
        var initial = await configurationService.LoadAsync();
        Assert.IsNull(initial.OneDriveRoot);

        var oneDriveRoot = Path.Combine(paths.RootDirectory, "Shared OneDrive");
        Directory.CreateDirectory(oneDriveRoot);
        var lineageId = Guid.NewGuid();
        await new JsonSystemMetadataStore(oneDriveRoot).EnsureCurrentLineageAsync(lineageId, 1);

        var result = await new M07ConfigurationSetupService(configurationService).ValidateAndPersistAsync(
            initial,
            new M07ConfigurationSetupInput(
                oneDriveRoot,
                "cimerosef",
                "sushi81-pos-handoff",
                "sushi81-handoff-v1",
                "Sushi81 POS Handoff Transport",
                "sushi81-pos-github"));

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(lineageId, result.ValidatedLineage!.LineageId);
        var persisted = await configurationService.LoadAsync();
        Assert.AreEqual(Path.GetFullPath(oneDriveRoot), persisted.OneDriveRoot);
        Assert.AreEqual("cimerosef", persisted.GitHubOwner);
        Assert.AreEqual("sushi81-pos-handoff", persisted.GitHubRepository);
        Assert.AreEqual("sushi81-handoff-v1", persisted.GitHubReleaseTag);
        Assert.AreEqual("Sushi81 POS Handoff Transport", persisted.GitHubReleaseName);
        Assert.AreEqual("sushi81-pos-github", persisted.GitHubCredentialTarget);

        var settingsJson = await File.ReadAllTextAsync(Path.Combine(paths.ConfigDirectory, "local-settings.json"));
        Assert.IsFalse(settingsJson.Contains("PAT", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(settingsJson.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(settingsJson.Contains("authorization", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(File.Exists(Path.Combine(paths.ConfigDirectory, "authority-state.json")));
        Assert.IsEmpty(Directory.Exists(Path.Combine(oneDriveRoot, "System", "Devices"))
            ? Directory.GetFiles(Path.Combine(oneDriveRoot, "System", "Devices"), "*", SearchOption.AllDirectories)
            : []);
    }

    [TestMethod]
    public async Task InvalidFreshSetupDoesNotPersistOrChangeExistingConfiguration()
    {
        using var paths = new TestAppPaths();
        var configurationService = new JsonLocalConfigurationService(paths);
        var initial = await configurationService.LoadAsync();
        var validRoot = Path.Combine(paths.RootDirectory, "Valid OneDrive");
        Directory.CreateDirectory(validRoot);
        await new JsonSystemMetadataStore(validRoot).EnsureCurrentLineageAsync(Guid.NewGuid(), 1);
        var setup = new M07ConfigurationSetupService(configurationService);
        var valid = await setup.ValidateAndPersistAsync(initial, new(validRoot, "owner", "repo", null, null, null));
        Assert.IsTrue(valid.Succeeded);

        var relative = await setup.ValidateAndPersistAsync(valid.Configuration, new("relative-root", "changed", "changed", null, null, null));
        Assert.IsFalse(relative.Succeeded);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.RootNotAbsolute, relative.FailureKind);

        var missingLineageRoot = Path.Combine(paths.RootDirectory, "Missing Lineage");
        Directory.CreateDirectory(missingLineageRoot);
        var missing = await setup.ValidateAndPersistAsync(valid.Configuration, new(missingLineageRoot, "changed", "changed", null, null, null));
        Assert.IsFalse(missing.Succeeded);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.LineageRequired, missing.FailureKind);

        var persisted = await configurationService.LoadAsync();
        Assert.AreEqual(valid.Configuration.OneDriveRoot, persisted.OneDriveRoot);
        Assert.AreEqual("owner", persisted.GitHubOwner);
        Assert.AreEqual("repo", persisted.GitHubRepository);
    }

    [TestMethod]
    public async Task ContradictoryLineageIsRejectedWithoutRewritingMetadata()
    {
        using var paths = new TestAppPaths();
        var configurationService = new JsonLocalConfigurationService(paths);
        var root = Path.Combine(paths.RootDirectory, "Shared OneDrive");
        var lineageDirectory = Path.Combine(root, "System", "Lineage");
        Directory.CreateDirectory(lineageDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(lineageDirectory, "lineage.json"),
            JsonSerializer.Serialize(new SystemLineageMetadata(99, "future", Guid.NewGuid(), 0, DateTimeOffset.UtcNow)));

        var result = await new M07ConfigurationSetupService(configurationService).ValidateAndPersistAsync(
            await configurationService.LoadAsync(),
            new(root, null, null, null, null, null));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.LineageInvalid, result.FailureKind);
        Assert.IsNull((await configurationService.LoadAsync()).OneDriveRoot);
        var raw = await File.ReadAllTextAsync(Path.Combine(lineageDirectory, "lineage.json"));
        StringAssert.Contains(raw, "future");
    }

    [TestMethod]
    public async Task StableAuthoritativeSetupCanBindAnEmptyRootAndRestartPublishesExactLineage()
    {
        using var paths = new TestAppPaths();
        var configurationService = new JsonLocalConfigurationService(paths);
        var initial = await configurationService.LoadAsync();
        var root = Path.Combine(paths.RootDirectory, "New Shared OneDrive");
        Directory.CreateDirectory(root);
        var lineageId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var store = new JsonAuthorityStateStore(paths);
        await SaveCanonicalStateAsync(store, new AuthorityProtocolState(
            1, deviceId, "Authoritative device", lineageId, 1, 0, 0, AuthorityPhase.Authoritative));
        var authorityPath = Path.Combine(paths.ConfigDirectory, "authority-state.json");
        var authorityBefore = await File.ReadAllBytesAsync(authorityPath);

        var result = await new M07ConfigurationSetupService(configurationService, store).ValidateAndPersistAsync(
            initial,
            new(root, "owner", "repo", null, null, "credential-target"));

        Assert.IsTrue(result.Succeeded);
        Assert.IsNull(result.ValidatedLineage);
        CollectionAssert.AreEqual(authorityBefore, await File.ReadAllBytesAsync(authorityPath));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, SystemMetadataContract.SystemDirectoryName)));

        var guard = new WriteAuthorityGuard();
        var systemMetadata = new JsonSystemMetadataStore(root);
        var resolution = await new AuthorityStateCoordinator(
            store,
            guard,
            new FixedClock(),
            NullLogger<AuthorityStateCoordinator>.Instance,
            systemMetadata).InitializeAsync(
                legacyBootstrapEvidence: false,
                preMigrationLiveDatabaseEvidence: true);

        Assert.AreEqual(WriteAuthorityState.Authoritative, resolution.State);
        Assert.AreEqual(WriteAuthorityState.Authoritative, guard.State);
        var published = await systemMetadata.ReadLineageAsync();
        Assert.AreEqual(lineageId, published.LineageId);
        Assert.AreEqual(1, published.CurrentGeneration);
        var devices = await systemMetadata.ListCurrentGenerationDevicesAsync(lineageId, 1);
        Assert.HasCount(1, devices);
        Assert.AreEqual(deviceId, devices[0].DeviceId);
        CollectionAssert.AreEqual(authorityBefore, await File.ReadAllBytesAsync(authorityPath));
        guard.Dispose();
    }

    [TestMethod]
    public async Task BoundAuthoritativeSetupAcceptsExactExistingLineageAndRejectsMismatchWithoutRewrite()
    {
        using var paths = new TestAppPaths();
        var configurationService = new JsonLocalConfigurationService(paths);
        var initial = await configurationService.LoadAsync();
        var root = Path.Combine(paths.RootDirectory, "Shared OneDrive");
        Directory.CreateDirectory(root);
        var localLineage = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var store = new JsonAuthorityStateStore(paths);
        await SaveCanonicalStateAsync(store, new AuthorityProtocolState(
            1, deviceId, "Authoritative device", localLineage, 3, 0, 0, AuthorityPhase.ClosedRetainedAuthority));
        var metadata = new JsonSystemMetadataStore(root);
        await metadata.EnsureCurrentLineageAsync(localLineage, 3);

        var setup = new M07ConfigurationSetupService(configurationService, store);
        var accepted = await setup.ValidateAndPersistAsync(initial, new(root, "owner", "repo", null, null, null));
        Assert.IsTrue(accepted.Succeeded);
        Assert.AreEqual(localLineage, accepted.ValidatedLineage!.LineageId);
        Assert.AreEqual(3, accepted.ValidatedLineage.CurrentGeneration);

        var lineagePath = Path.Combine(root, SystemMetadataContract.SystemDirectoryName, SystemMetadataContract.LineageDirectoryName, SystemMetadataContract.LineageFileName);
        var lineageBefore = await File.ReadAllBytesAsync(lineagePath);
        var mismatchRoot = Path.Combine(paths.RootDirectory, "Other OneDrive");
        Directory.CreateDirectory(mismatchRoot);
        await new JsonSystemMetadataStore(mismatchRoot).EnsureCurrentLineageAsync(Guid.NewGuid(), 3);
        var mismatchPath = Path.Combine(mismatchRoot, SystemMetadataContract.SystemDirectoryName, SystemMetadataContract.LineageDirectoryName, SystemMetadataContract.LineageFileName);
        var mismatchBefore = await File.ReadAllBytesAsync(mismatchPath);
        var persistedBefore = await configurationService.LoadAsync();
        var rejected = await setup.ValidateAndPersistAsync(persistedBefore, new(mismatchRoot, "changed", "changed", null, null, null));

        Assert.IsFalse(rejected.Succeeded);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.LineageInvalid, rejected.FailureKind);
        CollectionAssert.AreEqual(lineageBefore, await File.ReadAllBytesAsync(lineagePath));
        CollectionAssert.AreEqual(mismatchBefore, await File.ReadAllBytesAsync(mismatchPath));
        Assert.AreEqual(persistedBefore.OneDriveRoot, (await configurationService.LoadAsync()).OneDriveRoot);
    }

    [TestMethod]
    public async Task FreshOrUninitializedSetupCannotCreateAFirstLineage()
    {
        using var paths = new TestAppPaths();
        var configurationService = new JsonLocalConfigurationService(paths);
        var initial = await configurationService.LoadAsync();
        var root = Path.Combine(paths.RootDirectory, "Empty Shared OneDrive");
        Directory.CreateDirectory(root);
        var store = new JsonAuthorityStateStore(paths);
        var result = await new M07ConfigurationSetupService(configurationService, store).ValidateAndPersistAsync(
            initial,
            new(root, "owner", "repo", null, null, null));

        Assert.IsFalse(result.Succeeded);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.LineageRequired, result.FailureKind);
        Assert.IsFalse(File.Exists(Path.Combine(root, SystemMetadataContract.SystemDirectoryName, SystemMetadataContract.LineageDirectoryName, SystemMetadataContract.LineageFileName)));
        Assert.IsNull((await configurationService.LoadAsync()).OneDriveRoot);
    }

    [TestMethod]
    public async Task TechnicalSetupRejectsEveryUnsafeAuthorityPhaseWithoutChangingLocalState()
    {
        var phases = new[]
        {
            AuthorityPhase.TransferPreparing,
            AuthorityPhase.RelinquishedPendingGrant,
            AuthorityPhase.TargetAcquisitionPending,
            AuthorityPhase.DisasterRecoveryPending,
            AuthorityPhase.RecoveryRequired,
            AuthorityPhase.StaleGeneration
        };

        foreach (var phase in phases)
        {
            using var paths = new TestAppPaths();
            var configurationService = new JsonLocalConfigurationService(paths);
            var current = await configurationService.LoadAsync() with { OneDriveRoot = "C:\\prior-root", GitHubOwner = "prior-owner" };
            await configurationService.SaveAsync(current);
            var root = Path.Combine(paths.RootDirectory, "Candidate Shared OneDrive");
            Directory.CreateDirectory(root);
            var store = new JsonAuthorityStateStore(paths);
            var source = Guid.NewGuid();
            var target = Guid.NewGuid();
            var lineage = Guid.NewGuid();
            await SaveCanonicalStateAsync(store, CreateProtocolForPhase(phase, source, target, lineage));
            var authorityPath = Path.Combine(paths.ConfigDirectory, "authority-state.json");
            var authorityBefore = await File.ReadAllBytesAsync(authorityPath);
            var settingsPath = Path.Combine(paths.ConfigDirectory, "local-settings.json");
            var settingsBefore = await File.ReadAllBytesAsync(settingsPath);

            var result = await new M07ConfigurationSetupService(configurationService, store).ValidateAndPersistAsync(
                current,
                new(root, "changed-owner", "changed-repo", null, null, "changed-target"));

            Assert.IsFalse(result.Succeeded, phase.ToString());
            Assert.AreEqual(M07ConfigurationSetupFailureKind.AuthorityPhaseUnsafe, result.FailureKind, phase.ToString());
            CollectionAssert.AreEqual(authorityBefore, await File.ReadAllBytesAsync(authorityPath), phase.ToString());
            CollectionAssert.AreEqual(settingsBefore, await File.ReadAllBytesAsync(settingsPath), phase.ToString());
            Assert.AreEqual(current.OneDriveRoot, (await configurationService.LoadAsync()).OneDriveRoot, phase.ToString());
        }
    }

    private static async Task SaveCanonicalStateAsync(JsonAuthorityStateStore store, AuthorityProtocolState protocol)
    {
        await store.SaveAsync(new AuthorityStateDocument(2, protocol.WriteState, DateTimeOffset.UtcNow)
        {
            Protocol = protocol
        });
        await store.WriteBootstrapMarkerAsync();
        await store.WriteBootstrapAnchorAsync();
    }

    private static AuthorityProtocolState CreateProtocolForPhase(
        AuthorityPhase phase,
        Guid source,
        Guid target,
        Guid lineage)
    {
        if (phase == AuthorityPhase.RecoveryRequired)
            return new AuthorityProtocolState(1, source, "Device", null, 0, 0, 0, phase);

        if (phase == AuthorityPhase.StaleGeneration)
            return new AuthorityProtocolState(1, source, "Device", lineage, 1, 0, 0, phase);

        if (phase == AuthorityPhase.DisasterRecoveryPending)
        {
            var receipt = new RemoteAssetEvidence(1, 1, "recovery-candidate", 0, new string('a', 64));
            return new AuthorityProtocolState(
                1,
                target,
                "Device",
                lineage,
                1,
                1,
                0,
                phase,
                Recovery: new RecoveryActivationEvidence(
                    Guid.NewGuid(), target, lineage, 1, 2, "candidate", new string('b', 64), 0, receipt));
        }

        var transferId = Guid.NewGuid();
        var snapshotName = "snapshot.db";
        var snapshotSha = new string('c', 64);
        var snapshotReceipt = new RemoteAssetEvidence(1, 2, snapshotName, 1, snapshotSha);
        var grantReceipt = phase == AuthorityPhase.TargetAcquisitionPending
            ? new RemoteAssetEvidence(1, 3, "grant.json", 1, new string('d', 64))
            : null;
        var transfer = phase == AuthorityPhase.TransferPreparing
            ? TransferEvidence.Pending(transferId, lineage, 1, 1, source, target, 0)
            : new TransferEvidence(
                transferId,
                lineage,
                1,
                1,
                source,
                target,
                0,
                snapshotName,
                "snapshot.db",
                1,
                snapshotSha,
                snapshotReceipt,
                grantReceipt,
                DateTimeOffset.UtcNow,
                true,
                grantReceipt is null ? null : DateTimeOffset.UtcNow);
        var device = phase == AuthorityPhase.TargetAcquisitionPending ? target : source;
        return new AuthorityProtocolState(1, device, "Device", lineage, 1, 1, 0, phase, transfer);
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 8);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }
}
