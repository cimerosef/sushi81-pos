using System.Text.Json;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class M14PreProductionIsolationTests
{
    [TestMethod]
    public async Task ProductionDefaultsStayFrozenAndPreProductionGetsItsOwnReleaseDefaults()
    {
        using var productionPaths = new TestAppPaths();
        using var productionConfiguration = new JsonLocalConfigurationService(productionPaths);
        var production = await productionConfiguration.LoadAsync();
        Assert.AreEqual("sushi81-handoff-v1", production.GitHubReleaseTag);
        Assert.AreEqual("Sushi81 POS Handoff Transport", production.GitHubReleaseName);

        using var preProductionPaths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var preProductionConfiguration = new JsonLocalConfigurationService(preProductionPaths);
        var initial = await preProductionConfiguration.LoadAsync();
        Assert.AreEqual("sushi81-handoff-preprod-v1", initial.GitHubReleaseTag);
        Assert.AreEqual("Sushi81 POS PREPROD Handoff Transport", initial.GitHubReleaseName);

        await preProductionConfiguration.SaveAsync(new LocalConfiguration(
            GitHubReleaseTag: LocalConfiguration.ProductionGitHubReleaseTag,
            GitHubReleaseName: LocalConfiguration.ProductionGitHubReleaseName));
        var migratedDefaults = await preProductionConfiguration.LoadAsync();
        Assert.AreEqual("sushi81-handoff-preprod-v1", migratedDefaults.GitHubReleaseTag);
        Assert.AreEqual("Sushi81 POS PREPROD Handoff Transport", migratedDefaults.GitHubReleaseName);

        await preProductionConfiguration.SaveAsync(new LocalConfiguration(
            GitHubOwner: "custom-owner",
            GitHubRepository: "custom-runtime",
            GitHubReleaseTag: "custom-preprod-tag",
            GitHubReleaseName: "Custom PreProd Release",
            GitHubCredentialTarget: "custom-preprod-credential"));
        var customized = await preProductionConfiguration.LoadAsync();
        Assert.AreEqual("custom-owner", customized.GitHubOwner);
        Assert.AreEqual("custom-runtime", customized.GitHubRepository);
        Assert.AreEqual("custom-preprod-tag", customized.GitHubReleaseTag);
        Assert.AreEqual("Custom PreProd Release", customized.GitHubReleaseName);
        Assert.AreEqual("custom-preprod-credential", customized.GitHubCredentialTarget);
    }

    [TestMethod]
    public async Task SameAndNestedOneDriveRootsAreRejectedButSiblingRootsAreAccepted()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var productionSettings = new ProductionSettingsFile();
        var shared = Path.Combine(paths.RootDirectory, "SharedRoot");
        await CreateLineagedRootAsync(shared);

        await productionSettings.WriteAsync(Settings(oneDriveRoot: shared));
        var same = await ValidateAsync(paths, productionSettings.Path, shared);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.OneDriveRootCollision, same.FailureKind);

        var productionParent = Path.Combine(paths.RootDirectory, "ProdParent");
        var nestedPreProduction = Path.Combine(productionParent, "PreProdChild");
        await CreateLineagedRootAsync(nestedPreProduction);
        await productionSettings.WriteAsync(Settings(oneDriveRoot: productionParent));
        var preProductionInsideProduction = await ValidateAsync(paths, productionSettings.Path, nestedPreProduction);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.OneDriveRootCollision, preProductionInsideProduction.FailureKind);

        var productionChild = Path.Combine(paths.RootDirectory, "PreProdParent", "ProdChild");
        var preProductionParent = Path.GetDirectoryName(productionChild)!;
        await CreateLineagedRootAsync(preProductionParent);
        await productionSettings.WriteAsync(Settings(oneDriveRoot: productionChild));
        var productionInsidePreProduction = await ValidateAsync(paths, productionSettings.Path, preProductionParent);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.OneDriveRootCollision, productionInsidePreProduction.FailureKind);

        var productionSibling = Path.Combine(paths.RootDirectory, "Sushi81 POS");
        var preProductionSibling = Path.Combine(paths.RootDirectory, "Sushi81 POS PREPROD");
        await CreateLineagedRootAsync(preProductionSibling);
        await productionSettings.WriteAsync(Settings(oneDriveRoot: productionSibling));
        var siblings = await ValidateAsync(paths, productionSettings.Path, preProductionSibling);
        Assert.IsTrue(siblings.Succeeded, siblings.Diagnostic);
    }

    [TestMethod]
    public async Task OneDriveComparisonTrimsCaseAndTrailingSeparatorsWithoutPrefixFalsePositives()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var productionSettings = new ProductionSettingsFile();
        var productionRoot = Path.Combine(paths.RootDirectory, "Cloud");
        var preProductionRoot = Path.Combine(paths.RootDirectory, "Cloud PREPROD");
        await CreateLineagedRootAsync(preProductionRoot);
        await productionSettings.WriteAsync(Settings(oneDriveRoot: productionRoot.ToUpperInvariant() + Path.DirectorySeparatorChar));

        var independentPrefix = await ValidateAsync(
            paths,
            productionSettings.Path,
            "  " + preProductionRoot.ToUpperInvariant() + Path.DirectorySeparatorChar + "  ");
        Assert.IsTrue(independentPrefix.Succeeded, independentPrefix.Diagnostic);

        await productionSettings.WriteAsync(Settings(oneDriveRoot: preProductionRoot.ToUpperInvariant() + Path.DirectorySeparatorChar));
        var sameNormalizedPath = await ValidateAsync(
            paths,
            productionSettings.Path,
            preProductionRoot.ToLowerInvariant() + Path.DirectorySeparatorChar);
        Assert.AreEqual(M07ConfigurationSetupFailureKind.OneDriveRootCollision, sameNormalizedPath.FailureKind);
    }

    [TestMethod]
    public async Task GitHubRepositoryComparisonIsCaseInsensitiveAndAllowsAnotherRepository()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var productionSettings = new ProductionSettingsFile();
        var productionRoot = Path.Combine(paths.RootDirectory, "Prod");
        var preProductionRoot = Path.Combine(paths.RootDirectory, "PreProd");
        await CreateLineagedRootAsync(preProductionRoot);
        await productionSettings.WriteAsync(Settings(
            oneDriveRoot: productionRoot,
            owner: " Owner ",
            repository: " Runtime-Handoff "));

        var collision = await ValidateAsync(
            paths,
            productionSettings.Path,
            preProductionRoot,
            owner: "owner",
            repository: "runtime-handoff");
        Assert.AreEqual(M07ConfigurationSetupFailureKind.GitHubRepositoryCollision, collision.FailureKind);

        var separate = await ValidateAsync(
            paths,
            productionSettings.Path,
            preProductionRoot,
            owner: "OWNER",
            repository: "another-runtime");
        Assert.IsTrue(separate.Succeeded, separate.Diagnostic);
    }

    [TestMethod]
    public async Task SourceRepositoryIsAlwaysRejectedEvenWhenProductionSettingsAreAbsent()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var absentProductionSettings = new ProductionSettingsFile();
        var root = Path.Combine(paths.RootDirectory, "PreProd");
        await CreateLineagedRootAsync(root);

        var result = await ValidateAsync(
            paths,
            absentProductionSettings.Path,
            root,
            owner: " CIMERosEF ",
            repository: " SUSHI81-POS ");
        Assert.AreEqual(M07ConfigurationSetupFailureKind.GitHubRepositoryCollision, result.FailureKind);
        Assert.IsFalse(File.Exists(absentProductionSettings.Path));
    }

    [TestMethod]
    public async Task CredentialTargetComparisonIsCaseInsensitiveAndAllowsADistinctTarget()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var productionSettings = new ProductionSettingsFile();
        var productionRoot = Path.Combine(paths.RootDirectory, "Prod");
        var preProductionRoot = Path.Combine(paths.RootDirectory, "PreProd");
        await CreateLineagedRootAsync(preProductionRoot);
        await productionSettings.WriteAsync(Settings(
            oneDriveRoot: productionRoot,
            credentialTarget: " Sushi81POS-GitHub "));

        var collision = await ValidateAsync(
            paths,
            productionSettings.Path,
            preProductionRoot,
            credentialTarget: "sushi81pos-github");
        Assert.AreEqual(M07ConfigurationSetupFailureKind.CredentialTargetCollision, collision.FailureKind);

        var distinct = await ValidateAsync(
            paths,
            productionSettings.Path,
            preProductionRoot,
            credentialTarget: "Sushi81POS-PREPROD-GitHub-Handoff");
        Assert.IsTrue(distinct.Succeeded, distinct.Diagnostic);
    }

    [TestMethod]
    public async Task MalformedDuplicateOrWronglyTypedProductionSettingsFailClosedBeforePersistence()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var productionSettings = new ProductionSettingsFile();
        var root = Path.Combine(paths.RootDirectory, "PreProd");
        await CreateLineagedRootAsync(root);
        var configurationService = new JsonLocalConfigurationService(paths);
        var prior = await configurationService.LoadAsync() with
        {
            OneDriveRoot = "prior-root",
            GitHubOwner = "prior-owner",
            GitHubRepository = "prior-repository",
            GitHubCredentialTarget = "prior-target"
        };
        await configurationService.SaveAsync(prior);
        var settingsBefore = await File.ReadAllBytesAsync(Path.Combine(paths.ConfigDirectory, "local-settings.json"));

        foreach (var invalidJson in new[]
        {
            "{",
            "{\"oneDriveRoot\":\"C:\\\\Prod\",\"OneDriveRoot\":\"C:\\\\Other\"}",
            "{\"githubOwner\":42}"
        })
        {
            await productionSettings.WriteAsync(invalidJson);
            var result = await new M07ConfigurationSetupService(
                    configurationService,
                    appPaths: paths,
                    productionSettingsFilePath: productionSettings.Path)
                .ValidateAndPersistAsync(prior, SetupInput(root));

            Assert.AreEqual(M07ConfigurationSetupFailureKind.ProductionSettingsUnavailable, result.FailureKind, invalidJson);
            CollectionAssert.AreEqual(
                settingsBefore,
                await File.ReadAllBytesAsync(Path.Combine(paths.ConfigDirectory, "local-settings.json")),
                invalidJson);
        }

        File.Delete(productionSettings.Path);
        Directory.CreateDirectory(productionSettings.Path);
        var unreadable = await new M07ConfigurationSetupService(
                configurationService,
                appPaths: paths,
                productionSettingsFilePath: productionSettings.Path)
            .ValidateAndPersistAsync(prior, SetupInput(root));
        Assert.AreEqual(M07ConfigurationSetupFailureKind.ProductionSettingsUnavailable, unreadable.FailureKind);
        CollectionAssert.AreEqual(settingsBefore, await File.ReadAllBytesAsync(Path.Combine(paths.ConfigDirectory, "local-settings.json")));
    }

    [TestMethod]
    public async Task MissingProductionSettingsDoNotBlockPreProductionAndTheFileIsReadOnly()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var productionSettings = new ProductionSettingsFile();
        var root = Path.Combine(paths.RootDirectory, "PreProd");
        await CreateLineagedRootAsync(root);
        var result = await ValidateAsync(paths, productionSettings.Path, root, owner: "owner", repository: "preprod-runtime");

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.IsFalse(File.Exists(productionSettings.Path));

        await productionSettings.WriteAsync(Settings(
            oneDriveRoot: Path.Combine(paths.RootDirectory, "Prod"),
            owner: "prod-owner",
            repository: "prod-runtime",
            credentialTarget: "prod-credential"));
        var productionBytesBefore = await File.ReadAllBytesAsync(productionSettings.Path);
        var independent = await ValidateAsync(
            paths,
            productionSettings.Path,
            root,
            owner: "preprod-owner",
            repository: "preprod-runtime",
            credentialTarget: "preprod-credential");
        Assert.IsTrue(independent.Succeeded, independent.Diagnostic);
        CollectionAssert.AreEqual(productionBytesBefore, await File.ReadAllBytesAsync(productionSettings.Path));
    }

    [TestMethod]
    public async Task RejectedCollisionLeavesPreProductionConfigurationByteForByteUnchanged()
    {
        using var paths = new TestAppPaths(DeploymentProfile.PreProduction);
        using var productionSettings = new ProductionSettingsFile();
        var root = Path.Combine(paths.RootDirectory, "PreProd");
        await CreateLineagedRootAsync(root);
        var configurationService = new JsonLocalConfigurationService(paths);
        var prior = await configurationService.LoadAsync() with
        {
            OneDriveRoot = "kept-root",
            GitHubOwner = "kept-owner",
            GitHubRepository = "kept-repository",
            GitHubCredentialTarget = "kept-target"
        };
        await configurationService.SaveAsync(prior);
        var settingsPath = Path.Combine(paths.ConfigDirectory, "local-settings.json");
        var before = await File.ReadAllBytesAsync(settingsPath);
        await productionSettings.WriteAsync(Settings(
            oneDriveRoot: root,
            owner: "production",
            repository: "production-runtime",
            credentialTarget: "production-credential"));

        var result = await new M07ConfigurationSetupService(
                configurationService,
                appPaths: paths,
                productionSettingsFilePath: productionSettings.Path)
            .ValidateAndPersistAsync(
                prior,
                SetupInput(root, owner: "different", repository: "different-runtime", credentialTarget: "different-target"));

        Assert.AreEqual(M07ConfigurationSetupFailureKind.OneDriveRootCollision, result.FailureKind);
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(settingsPath));
        var after = await configurationService.LoadAsync();
        Assert.AreEqual("kept-root", after.OneDriveRoot);
        Assert.AreEqual("kept-owner", after.GitHubOwner);
        Assert.AreEqual("kept-repository", after.GitHubRepository);
        Assert.AreEqual("kept-target", after.GitHubCredentialTarget);
    }

    [TestMethod]
    public async Task ProductionSetupDoesNotReadOrDependOnPreProductionSettings()
    {
        using var paths = new TestAppPaths(DeploymentProfile.Production);
        using var invalidProductionSettings = new ProductionSettingsFile();
        var root = Path.Combine(paths.RootDirectory, "Prod");
        await CreateLineagedRootAsync(root);
        await invalidProductionSettings.WriteAsync("{malformed");
        using var configurationService = new JsonLocalConfigurationService(paths);

        var result = await new M07ConfigurationSetupService(
                configurationService,
                appPaths: paths,
                productionSettingsFilePath: invalidProductionSettings.Path)
            .ValidateAndPersistAsync(
                await configurationService.LoadAsync(),
                SetupInput(root, owner: "prod-owner", repository: "prod-runtime"));

        Assert.IsTrue(result.Succeeded, result.Diagnostic);
        Assert.AreEqual("{malformed", await File.ReadAllTextAsync(invalidProductionSettings.Path));
    }

    private static async Task<M07ConfigurationSetupResult> ValidateAsync(
        TestAppPaths paths,
        string productionSettingsPath,
        string selectedRoot,
        string? owner = "preprod-owner",
        string? repository = "preprod-runtime",
        string? credentialTarget = "preprod-credential")
    {
        using var configurationService = new JsonLocalConfigurationService(paths);
        var current = await configurationService.LoadAsync();
        return await new M07ConfigurationSetupService(
                configurationService,
                appPaths: paths,
                productionSettingsFilePath: productionSettingsPath)
            .ValidateAndPersistAsync(
                current,
                SetupInput(selectedRoot, owner, repository, credentialTarget));
    }

    private static M07ConfigurationSetupInput SetupInput(
        string root,
        string? owner = "preprod-owner",
        string? repository = "preprod-runtime",
        string? credentialTarget = "preprod-credential") =>
        new(
            root,
            owner,
            repository,
            null,
            null,
            credentialTarget);

    private static string Settings(
        string? oneDriveRoot = null,
        string? owner = null,
        string? repository = null,
        string? credentialTarget = null) =>
        JsonSerializer.Serialize(new LocalConfiguration(
            OneDriveRoot: oneDriveRoot,
            GitHubOwner: owner,
            GitHubRepository: repository,
            GitHubCredentialTarget: credentialTarget));

    private static async Task CreateLineagedRootAsync(string root)
    {
        Directory.CreateDirectory(root);
        await new JsonSystemMetadataStore(root).EnsureCurrentLineageAsync(Guid.NewGuid(), 1);
    }

    private sealed class ProductionSettingsFile : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Sushi81.Pos.Wp4Tests", Guid.NewGuid().ToString("N"));

        public string Path => System.IO.Path.Combine(directory, "Config", "local-settings.json");

        public async Task WriteAsync(string contents)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            await File.WriteAllTextAsync(Path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
