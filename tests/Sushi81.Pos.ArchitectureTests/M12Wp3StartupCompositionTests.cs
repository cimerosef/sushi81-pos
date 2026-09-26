using System.IO;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M12Wp3StartupCompositionTests
{
    [TestMethod]
    public void AnnualArchiveStartupRunsAfterMigrationAndFinalAuthorityRefresh()
    {
        var composition = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "CompositionRoot.cs"));
        var migrations = composition.IndexOf("await migrations.InitializeAsync();", StringComparison.Ordinal);
        var authority = composition.IndexOf("authorityResolution = await new AuthorityStateCoordinator", StringComparison.Ordinal);
        var refresh = composition.IndexOf("await m07Runtime.RefreshAuthorityStateAsync();", StringComparison.Ordinal);
        var archive = composition.IndexOf("await annualArchiveStartupCoordinator.RunAsync();", StringComparison.Ordinal);
        var compaction = composition.IndexOf("await gestionExportCompactionStartupCoordinator.RunAsync();", StringComparison.Ordinal);
        var success = composition.IndexOf("LogFoundationStartupSucceeded(logger);", StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, migrations);
        Assert.IsGreaterThan(migrations, authority);
        Assert.IsGreaterThan(authority, refresh);
        Assert.IsGreaterThan(refresh, archive);
        Assert.IsGreaterThan(archive, success);
        Assert.IsGreaterThan(archive, compaction);
        Assert.IsGreaterThan(compaction, success);
        Assert.AreEqual(1, composition.Split("await gestionExportCompactionStartupCoordinator.RunAsync();", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(composition, "SqliteAnnualArchiveFinalizationService");
        StringAssert.Contains(composition, "AnnualArchiveStartupCoordinator");
        StringAssert.Contains(composition, "annualArchiveFinalizationService.FinalizeNextArchiveAsync");
    }

    [TestMethod]
    public void StartupCoordinatorIsStartupOnlyAndDoesNotOwnWatcherOrNotificationPolicy()
    {
        var coordinator = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Infrastructure", "Archive", "AnnualArchiveStartupCoordinator.cs"));

        Assert.IsFalse(coordinator.Contains("OneDrive", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(coordinator.Contains("NotifyCommitted", StringComparison.Ordinal));
        Assert.IsFalse(coordinator.Contains("Task.Delay", StringComparison.Ordinal));
        StringAssert.Contains(coordinator, "FailedRetryable");
        StringAssert.Contains(coordinator, "WriteAuthorityState.Authoritative");
    }

    [TestMethod]
    public void ExportCompactionStartupCoordinatorSkipsReadOnlyAndDoesNotOwnBackgroundTriggers()
    {
        var coordinator = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Infrastructure", "Export", "GestionExportCompactionStartupCoordinator.cs"));

        StringAssert.Contains(coordinator, "WriteAuthorityState.Authoritative");
        StringAssert.Contains(coordinator, "SkippedNotAuthoritative");
        StringAssert.Contains(coordinator, "FailedRetryable");
        StringAssert.Contains(coordinator, "LogCompactionFailure");
        Assert.IsFalse(coordinator.Contains("PeriodicTimer", StringComparison.Ordinal));
        Assert.IsFalse(coordinator.Contains("Task.Delay", StringComparison.Ordinal));
        Assert.IsFalse(coordinator.Contains("Button", StringComparison.Ordinal));
    }

    private static string LocateRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not locate repository file '{Path.Combine(parts)}'.");
    }
}
