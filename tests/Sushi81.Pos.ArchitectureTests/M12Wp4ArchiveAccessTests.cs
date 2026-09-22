using System.IO;
using System.Xml.Linq;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M12Wp4ArchiveAccessTests
{
    [TestMethod]
    public void ArchiveSurfaceIsSeparateReadOnlyAndHasNoLifecycleOrPrintActions()
    {
        var xaml = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        var start = xaml.IndexOf("Header=\"{Binding DataContext.Localized[ArchiveAccess]", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        var end = xaml.IndexOf("</TabItem>", start, StringComparison.Ordinal);
        Assert.IsGreaterThan(start, end);
        var archiveTab = xaml[start..end];

        StringAssert.Contains(archiveTab, "DataContext.ArchiveAccess");
        StringAssert.Contains(archiveTab, "SelectedArchive");
        StringAssert.Contains(archiveTab, "IsReadOnly=\"True\"");
        StringAssert.Contains(archiveTab, "OnCopyArchive");
        Assert.IsFalse(archiveTab.Contains("OnReprint", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(archiveTab.Contains("OnSave", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(archiveTab.Contains("OnCancel", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(archiveTab.Contains("Print", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ArchiveCompositionAndResourcesAreWiredForBothSupportedCultures()
    {
        var composition = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "CompositionRoot.cs"));
        var shell = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "Localization.cs"));
        var infrastructure = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Infrastructure", "Archive", "SqliteAnnualArchiveAccess.cs"));

        StringAssert.Contains(composition, "SqliteAnnualArchiveAccess");
        StringAssert.Contains(composition, "annualArchiveAccess");
        StringAssert.Contains(shell, "AnnualArchiveAccessViewModel");
        StringAssert.Contains(shell, "ArchiveAccess?.ApplyLocalization");
        StringAssert.Contains(infrastructure, "OpenReadOnlyConnectionAsync");
        Assert.IsFalse(infrastructure.Contains("LiveDatabasePath", StringComparison.Ordinal));
        Assert.IsFalse(infrastructure.Contains("IWriteAuthorityGuard", StringComparison.Ordinal));

        var requiredKeys = new[]
        {
            "ArchiveAccess", "ArchiveYear", "ArchiveSearch", "ArchiveCopy", "ArchiveReadOnlyNotice",
            "ArchiveStatusAll", "ArchiveNoArchives", "ArchiveAccessFailed", "ArchiveInvalidDateRange",
            "ArchiveCopySucceeded", "ArchiveCopyFailed"
        };
        foreach (var resource in new[] { "Resources.resx", "Resources.zh-CN.resx" })
        {
            var document = XDocument.Load(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "Properties", resource));
            var names = document.Root!.Elements("data").Select(element => (string?)element.Attribute("name")).ToHashSet(StringComparer.Ordinal);
            CollectionAssert.IsSubsetOf(requiredKeys, names.ToArray());
        }
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
