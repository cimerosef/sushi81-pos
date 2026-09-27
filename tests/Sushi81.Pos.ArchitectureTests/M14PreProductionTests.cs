using System.IO;
using System.Globalization;
using System.Resources;
using Sushi81.Pos.Application.Foundation;
using Sushi81.Pos.Desktop;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M14PreProductionTests
{
    [TestMethod]
    public void InitialSeedOperatorMessagesExistInFrenchAndChinese()
    {
        var resources = new ResourceManager("Sushi81.Pos.Desktop.Properties.Resources", typeof(CompositionRoot).Assembly);
        var keys = new[]
        {
            "PreProductionSeedTitle", "PreProductionSeedOffer", "PreProductionSeedConfirm", "PreProductionSeedSuccess",
            "PreProductionSeedFailure", "PreProductionSeedFailureProcess", "PreProductionSeedFailureTarget",
            "PreProductionSeedFailureSourceMissing", "PreProductionSeedFailureSourceInvalid",
            "PreProductionSeedFailureMigration", "PreProductionSeedFailureInstall"
        };

        foreach (var cultureName in new[] { "fr-FR", "zh-CN" })
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var key in keys)
                Assert.IsFalse(string.IsNullOrWhiteSpace(resources.GetString(key, culture)), $"Missing {cultureName} resource {key}.");
        }

        StringAssert.Contains(resources.GetString("PreProductionSeedOffer", CultureInfo.GetCultureInfo("fr-FR"))!, "PROD");
        StringAssert.Contains(resources.GetString("PreProductionSeedOffer", CultureInfo.GetCultureInfo("zh-CN"))!, "PROD");
    }

    [TestMethod]
    public async Task PreProductionShellHasDistinctIdentityAndLocalizedPersistentMarker()
    {
        using var shell = new ShellViewModel(
            new InMemorySelectedCultureStore(),
            startupSucceeded: true,
            deploymentProfile: DeploymentProfile.PreProduction);

        Assert.AreEqual("Sushi81 POS PREPROD", shell.Title);
        Assert.AreEqual("PREPROD — DONNÉES DE TEST", shell.EnvironmentBanner);
        Assert.IsTrue(shell.IsPreProduction);

        await shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN"));

        Assert.AreEqual("Sushi81 POS PREPROD", shell.Title);
        Assert.AreEqual("PREPROD — 测试数据", shell.EnvironmentBanner);
    }

    [TestMethod]
    public void ProductionShellAndGestionExportNamesRemainUnmarked()
    {
        using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        var batchId = Guid.Parse("46c1aa94-1ef2-4c19-9ae4-a92e67a891b4");
        var instant = new DateTimeOffset(2026, 9, 27, 19, 0, 0, TimeSpan.Zero);
        var localTimestamp = instant.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        Assert.AreEqual("Sushi81 POS", shell.Title);
        Assert.AreEqual(string.Empty, shell.EnvironmentBanner);
        Assert.IsFalse(shell.IsPreProduction);
        Assert.AreEqual("Sushi81_POS_Export_20260927_190000.xlsx", GestionExportFileNames.NewExport(instant.DateTime, DeploymentProfile.Production));
        Assert.AreEqual($"Sushi81_POS_Export_{localTimestamp}_{batchId:D}.xlsx", GestionExportFileNames.Regenerate(instant, batchId, DeploymentProfile.Production));
        Assert.AreEqual($"Sushi81_POS_Export_Retry_{localTimestamp}_{batchId:D}.xlsx", GestionExportFileNames.Retry(instant, batchId, DeploymentProfile.Production));
    }

    [TestMethod]
    public void AllDefaultPreProductionGestionExportNamesHaveEnvironmentPrefix()
    {
        var batchId = Guid.Parse("46c1aa94-1ef2-4c19-9ae4-a92e67a891b4");
        var instant = new DateTimeOffset(2026, 9, 27, 19, 0, 0, TimeSpan.Zero);

        StringAssert.StartsWith(GestionExportFileNames.NewExport(instant.DateTime, DeploymentProfile.PreProduction), "PREPROD_");
        StringAssert.StartsWith(GestionExportFileNames.Regenerate(instant, batchId, DeploymentProfile.PreProduction), "PREPROD_");
        StringAssert.StartsWith(GestionExportFileNames.Retry(instant, batchId, DeploymentProfile.PreProduction), "PREPROD_");
    }

    [TestMethod]
    public void MainOperationalWindowPresentsTheEnvironmentMarkerPersistently()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));

        StringAssert.Contains(xaml, "Text=\"{Binding EnvironmentBanner}\"");
        StringAssert.Contains(xaml, "Visibility=\"{Binding IsPreProduction, Converter={StaticResource BoolToVisibility}}\"");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sushi81.Pos.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the Sushi81 POS solution root.");
    }
}
