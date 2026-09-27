using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Infrastructure.Paths;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M14DeploymentProfileTests
{
    [TestMethod]
    public void UnpackagedDeveloperRunWithoutEvidenceKeepsProductionProfile()
    {
        var profile = DeploymentProfileResolver.Resolve(
            @"C:\repo\src\Sushi81.Pos.Desktop\bin\Release",
            @"C:\Users\test\AppData\Local",
            _ => false,
            _ => throw new AssertFailedException("No evidence file should be read."));

        Assert.AreSame(DeploymentProfile.Production, profile);
    }

    [TestMethod]
    public void InstalledProductionAndPreProductionRequireMatchingEvidence()
    {
        var localAppData = @"C:\Users\test\AppData\Local";
        var productionInstall = Path.Combine(localAppData, "Programs", "Sushi81 POS");
        var preProductionInstall = Path.Combine(localAppData, "Programs", "Sushi81 POS PREPROD");

        Assert.AreSame(DeploymentProfile.Production, Resolve(productionInstall, localAppData, "prod\r\n"));
        Assert.AreSame(DeploymentProfile.PreProduction, Resolve(preProductionInstall, localAppData, "preprod\n"));
        Assert.Throws<DeploymentProfileResolutionException>(() => Resolve(productionInstall, localAppData, null));
        Assert.Throws<DeploymentProfileResolutionException>(() => Resolve(preProductionInstall, localAppData, null));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("production")]
    [DataRow(" prod")]
    [DataRow("prod\npreprod")]
    [DataRow("prod\n\n")]
    public void UnknownOrMalformedEvidenceFailsClosed(string contents)
    {
        var localAppData = @"C:\Users\test\AppData\Local";
        var installedPath = Path.Combine(localAppData, "Programs", "Sushi81 POS PREPROD");

        Assert.Throws<DeploymentProfileResolutionException>(() => Resolve(installedPath, localAppData, contents));
    }

    [TestMethod]
    public void EvidenceThatContradictsFixedInstalledIdentityFailsClosed()
    {
        var localAppData = @"C:\Users\test\AppData\Local";
        var productionInstall = Path.Combine(localAppData, "Programs", "Sushi81 POS");

        Assert.Throws<DeploymentProfileResolutionException>(() => Resolve(productionInstall, localAppData, "preprod"));
    }

    [TestMethod]
    public void ProfilesDeriveDisjointCompleteRuntimePathSetsAndKeepProductionRootStable()
    {
        const string localAppData = @"C:\Users\test\AppData\Local";
        var production = new WindowsAppPaths(DeploymentProfile.Production, localAppData);
        var preProduction = new WindowsAppPaths(DeploymentProfile.PreProduction, localAppData);

        Assert.AreEqual(Path.Combine(localAppData, "Sushi81 POS"), production.RootDirectory);
        Assert.AreEqual(Path.Combine(localAppData, "Sushi81 POS PREPROD"), preProduction.RootDirectory);
        CollectionAssert.AreEquivalent(
            new[] { "Data", "Recovery", "Archive", "Cache", "Logs", "Config", "Temp", Path.Combine("Data", "live.db") },
            RelativeRuntimePaths(production));

        var productionPaths = RuntimePaths(production);
        var preProductionPaths = RuntimePaths(preProduction);
        foreach (var productionPath in productionPaths)
        foreach (var preProductionPath in preProductionPaths)
        {
            Assert.IsFalse(IsSameOrNested(productionPath, preProductionPath), $"Runtime paths overlap: {productionPath} and {preProductionPath}");
            Assert.IsFalse(IsSameOrNested(preProductionPath, productionPath), $"Runtime paths overlap: {preProductionPath} and {productionPath}");
        }
    }

    private static DeploymentProfile Resolve(string applicationDirectory, string localAppData, string? evidence) =>
        DeploymentProfileResolver.Resolve(
            applicationDirectory,
            localAppData,
            _ => evidence is not null,
            _ => evidence ?? throw new AssertFailedException("No profile evidence was expected."));

    private static string[] RelativeRuntimePaths(WindowsAppPaths paths) => RuntimePaths(paths)
        .Select(path => Path.GetRelativePath(paths.RootDirectory, path))
        .ToArray();

    private static string[] RuntimePaths(WindowsAppPaths paths) =>
    [
        paths.DataDirectory,
        paths.RecoveryDirectory,
        paths.ArchiveDirectory,
        paths.CacheDirectory,
        paths.LogsDirectory,
        paths.ConfigDirectory,
        paths.TempDirectory,
        paths.LiveDatabasePath
    ];

    private static bool IsSameOrNested(string candidate, string parent)
    {
        var normalizedParent = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
