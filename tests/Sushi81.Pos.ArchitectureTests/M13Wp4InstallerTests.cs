using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M13Wp4InstallerTests
{
    [TestMethod]
    public void InstallerUsesStablePerUserIdentityAndOwnsNoDurableDataRules()
    {
        var configPath = LocateRepositoryFile("installer", "release-config.json");
        using var config = JsonDocument.Parse(File.ReadAllText(configPath));
        var root = config.RootElement;
        Assert.AreEqual("win-x64", root.GetProperty("runtimeIdentifier").GetString());
        Assert.IsTrue(root.GetProperty("selfContained").GetBoolean());
        Assert.IsFalse(root.GetProperty("publishSingleFile").GetBoolean());
        Assert.AreEqual("6.7.3", root.GetProperty("innoSetupVersion").GetString());
        Assert.AreEqual("C7A1B9E2-1E62-4B4B-A2EA-7802814408FC", root.GetProperty("innoAppId").GetString());
        Assert.AreEqual(90, root.GetProperty("artifactRetentionDays").GetInt32());

        var setup = File.ReadAllText(LocateRepositoryFile("installer", "sushi81-pos.iss"));
        StringAssert.Contains(setup, "#if Ver != (6 * 16777216 + 7 * 65536 + 3 * 256)");
        StringAssert.Contains(setup, "AppId={{C7A1B9E2-1E62-4B4B-A2EA-7802814408FC}");
        StringAssert.Contains(setup, "AppVersion={#ProductVersion}");
        Assert.IsFalse(setup.Contains("UninstallDisplayVersion", StringComparison.Ordinal));
        StringAssert.Contains(setup, "PrivilegesRequired=lowest");
        StringAssert.Contains(setup, "ArchitecturesAllowed=x64compatible");
        StringAssert.Contains(setup, "ArchitecturesInstallIn64BitMode=x64compatible");
        StringAssert.Contains(setup, "DefaultDirName={localappdata}\\Programs\\Sushi81 POS");
        StringAssert.Contains(setup, "CloseApplications=yes");
        StringAssert.Contains(setup, "recursesubdirs createallsubdirs");
        Assert.IsFalse(setup.Contains("live.db", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(setup.Contains("%LOCALAPPDATA%\\Sushi81 POS", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(setup.Contains("UninstallDelete", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(setup.Contains("[Run]", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(setup.Contains("[UninstallRun]", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(setup.Contains("[Registry]", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ProductionPipelinePinsExactSourceAndRunsPreservationLifecycle()
    {
        var workflow = File.ReadAllText(LocateRepositoryFile(".github", "workflows", "ci.yml"));
        StringAssert.Contains(workflow, "github.event.pull_request.head.sha || github.sha");
        StringAssert.Contains(workflow, "codex/m13-installer-final-acceptance-authorized");
        StringAssert.Contains(workflow, "retention-days: 90");
        StringAssert.Contains(workflow, "Pyrsys B\\.V\\.");
        StringAssert.Contains(workflow, "m13-production-installer");
        Assert.IsFalse(workflow.Contains("m11-owner-candidate-artifact", StringComparison.Ordinal));

        var lifecycle = File.ReadAllText(LocateRepositoryFile("installer", "scripts", "Verify-InstallerLifecycle.ps1"));
        foreach (var required in new[]
        {
            "Data\\live.db", "Archive\\", "Recovery\\", "Config\\", "Get-FileHash", "Assert-SyntheticDataUnchanged",
            "ordinary uninstall", "in-place upgrade", "0.9.0", "GITHUB_ACTIONS -ne 'true'", "Get-ScheduledTask", "Win32_Service", "release-provenance.json"
        }) {
            StringAssert.Contains(lifecycle, required);
        }
        StringAssert.Contains(lifecycle, "not a claim of a historical installer");
        StringAssert.Contains(lifecycle, "interactive WPF launch smoke: deferred");
    }

    [TestMethod]
    public void PackagingRecordsMachineReadableProvenanceAndScansOnlyRuntimeRelevantForbiddenContent()
    {
        var build = File.ReadAllText(LocateRepositoryFile("installer", "scripts", "Build-InstallerPackage.ps1"));
        StringAssert.Contains(build, "rev-parse HEAD");
        StringAssert.Contains(build, "--self-contained', 'true'");
        StringAssert.Contains(build, "-p:PublishSingleFile=false");
        StringAssert.Contains(build, "IncludeSourceRevisionInInformationalVersion=false");
        StringAssert.Contains(build, "release-provenance.json");
        StringAssert.Contains(build, "installerSha256");
        StringAssert.Contains(build, "installerBytes");
        StringAssert.Contains(build, "fr-FR");
        StringAssert.Contains(build, "zh-CN");
        Assert.IsFalse(build.Contains("Remove-Item", StringComparison.OrdinalIgnoreCase));

        var scanner = File.ReadAllText(LocateRepositoryFile("installer", "scripts", "Test-ForbiddenContent.ps1"));
        foreach (var forbidden in new[] { "sqlite3", "xlsx", "xlsm", "csv", "secret", "token", "handoff", "Recovery", "Config", "tests?" }) {
            StringAssert.Contains(scanner, forbidden);
        }
        Assert.IsFalse(scanner.Contains("\\.json$", StringComparison.Ordinal), "Runtime provenance and .NET runtimeconfig JSON must remain permitted.");
    }

    private static string LocateRepositoryFile(params string[] segments)
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Sushi81.Pos.sln")))
                {
                    return Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
                }
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root from the architecture-test process.");
    }
}
