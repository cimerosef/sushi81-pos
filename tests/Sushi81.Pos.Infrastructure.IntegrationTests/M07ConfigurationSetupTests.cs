using System.Text.Json;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
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
        Assert.AreEqual(M07ConfigurationSetupFailureKind.LineageUnavailable, missing.FailureKind);

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
}
