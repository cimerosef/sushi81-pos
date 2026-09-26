using System.IO;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M13FinalNavigationTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] ExpectedTopLevelTags = ["catalogue", "settings", "data", "commandes", "caisse"];
    private static readonly string[] ExpectedDataChildTags = ["gestion-export", "archive"];
    private static readonly string[] NavigationResourceKeys = ["Catalogue", "Settings", "DataTools", "Commandes", "Caisse"];
    private static readonly string[] ExpectedFrenchCaptions = ["Catalogue", "Paramètres", "Données", "Commandes", "Caisse"];
    private static readonly string[] ExpectedChineseCaptions = ["商品目录", "设置", "数据", "订单", "收银台"];

    [TestMethod]
    public void MainWindowUsesRequestedTopLevelOrderAndGroupsExistingDataSurfaces()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        var mainTabs = document.Descendants(Presentation + "TabControl")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "mainTabs");
        var topLevel = mainTabs.Elements(Presentation + "TabItem").ToArray();

        CollectionAssert.AreEqual(
            ExpectedTopLevelTags,
            topLevel.Select(tab => (string?)tab.Attribute("Tag")).ToArray());

        var dataTools = topLevel.Single(tab => (string?)tab.Attribute("Tag") == "data");
        StringAssert.Contains((string)dataTools.Attribute("Header")!, "Localized[DataTools]");
        StringAssert.Contains((string)dataTools.Attribute("IsEnabled")!, "IsDataToolsAvailable");
        StringAssert.Contains((string)dataTools.Attribute("Visibility")!, "IsDataToolsAvailable");

        var nestedTabs = dataTools.Elements(Presentation + "TabControl").Single();
        var children = nestedTabs.Elements(Presentation + "TabItem").ToArray();
        CollectionAssert.AreEqual(
            ExpectedDataChildTags,
            children.Select(tab => (string?)tab.Attribute("Tag")).ToArray());
        Assert.AreEqual("gestionExportTab", (string?)children[0].Attribute(Xaml + "Name"));
        Assert.AreEqual("archiveAccessTab", (string?)children[1].Attribute(Xaml + "Name"));
        StringAssert.Contains((string)children[0].Attribute("DataContext")!, "DataContext.GestionExportWorkflow");
        StringAssert.Contains((string)children[1].Attribute("DataContext")!, "DataContext.ArchiveAccess");
        StringAssert.Contains((string)children[0].Attribute("IsEnabled")!, "IsGestionExportAvailable");
        StringAssert.Contains((string)children[0].Attribute("Visibility")!, "IsGestionExportAvailable");
        StringAssert.Contains((string)children[1].Attribute("IsEnabled")!, "IsArchiveAccessAvailable");
        StringAssert.Contains((string)children[1].Attribute("Visibility")!, "IsArchiveAccessAvailable");
    }

    [TestMethod]
    public async Task TopLevelNavigationCaptionsResolveInFrenchAndSimplifiedChinese()
    {
        using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);

        CollectionAssert.AreEqual(
            ExpectedFrenchCaptions,
            NavigationResourceKeys
                .Select(key => shell.Localized[key]).ToArray());

        await shell.ChangeLanguageAsync(shell.Languages.Single(option => option.CultureName == "zh-CN"));
        CollectionAssert.AreEqual(
            ExpectedChineseCaptions,
            NavigationResourceKeys
                .Select(key => shell.Localized[key]).ToArray());
    }

    [TestMethod]
    public void DataToolsAvailabilityIsTheOrOfExistingSurfaceAvailabilityAndArchiveOnlyRemainsUsable()
    {
        using var unavailable = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        Assert.IsFalse(unavailable.IsGestionExportAvailable);
        Assert.IsFalse(unavailable.IsArchiveAccessAvailable);
        Assert.IsFalse(unavailable.IsDataToolsAvailable);

        using var archiveOnly = new ShellViewModel(
            new InMemorySelectedCultureStore(),
            startupSucceeded: true,
            archiveAccess: new EmptyAnnualArchiveAccess());
        Assert.IsFalse(archiveOnly.IsGestionExportAvailable);
        Assert.IsTrue(archiveOnly.IsArchiveAccessAvailable);
        Assert.IsTrue(archiveOnly.IsDataToolsAvailable);

        var localizationSource = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "Localization.cs"));
        StringAssert.Contains(localizationSource, "IsDataToolsAvailable => IsGestionExportAvailable || IsArchiveAccessAvailable");

        var codeBehind = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs"));
        StringAssert.Contains(codeBehind, "dataToolsTabs.SelectedItem = shell.IsGestionExportAvailable ? gestionExportTab : archiveAccessTab;");
        StringAssert.Contains(codeBehind, "selected.Visibility == Visibility.Visible");
    }

    [TestMethod]
    public void OperationalTabStylesHaveSeparateStatesAndHighContrastSystemBrushFallback()
    {
        var document = XDocument.Load(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        var style = document.Descendants(Presentation + "Style")
            .Single(element => (string?)element.Attribute("TargetType") == "{x:Type TabItem}");
        var conditions = style.Descendants(Presentation + "Condition")
            .Select(condition => ((string?)condition.Attribute("Property"), (string?)condition.Attribute("Value")))
            .ToArray();

        Assert.IsGreaterThanOrEqualTo(1, conditions.Count(condition => condition == ("Tag", "commandes")));
        Assert.IsGreaterThanOrEqualTo(1, conditions.Count(condition => condition == ("Tag", "caisse")));
        Assert.IsGreaterThanOrEqualTo(2, conditions.Count(condition => condition == ("IsSelected", "False")));
        Assert.IsGreaterThanOrEqualTo(2, conditions.Count(condition => condition == ("IsSelected", "True")));

        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        StringAssert.Contains(xaml, "SystemParameters.HighContrast");
        StringAssert.Contains(xaml, "SystemColors.ControlBrushKey");
        StringAssert.Contains(xaml, "SystemColors.HighlightBrushKey");
        Assert.IsFalse(style.Descendants(Presentation + "ControlTemplate").Any(), "The built-in tab template preserves default focus, hover, and disabled behavior.");
        Assert.IsFalse(
            style.Descendants(Presentation + "Setter").Any(setter => (string?)setter.Attribute("Property") == "IsEnabled"),
            "The visual style must not override operational availability.");
    }

    [TestMethod]
    public void NavigationTelemetryUsesStableTagsAndBothDataChildIdentities()
    {
        var codeBehind = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs"));
        Assert.IsFalse(codeBehind.Contains("mainTabs.Items.IndexOf(selected)", StringComparison.Ordinal));
        StringAssert.Contains(codeBehind, "selected.Tag is not string key");
        StringAssert.Contains(codeBehind, "PerformanceTrace.Log($\"tab.selected.{key}\")");
        StringAssert.Contains(codeBehind, "PerformanceTrace.Log($\"tab.selected.data.{key}\")");

        var xaml = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        StringAssert.Contains(xaml, "SelectionChanged=\"OnMainTabsSelectionChanged\"");
        StringAssert.Contains(xaml, "SelectionChanged=\"OnDataToolsTabsSelectionChanged\"");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.EnumerateFiles(directory.FullName, "*.sln").Any())
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Sushi81 POS solution root.");
    }

    private sealed class EmptyAnnualArchiveAccess : IAnnualArchiveAccess
    {
        public Task<IReadOnlyList<AnnualArchiveDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AnnualArchiveDescriptor>>([]);

        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(
            int archiveYear,
            AnnualArchiveSearchCriteria criteria,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);

        public Task<OrderSnapshot?> GetOrderAsync(
            int archiveYear,
            Guid orderId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<OrderSnapshot?>(null);

        public Task<AnnualArchiveCopyResult> CopyAsync(
            int archiveYear,
            string destinationPath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AnnualArchiveCopyResult(archiveYear, destinationPath, 0, string.Empty));
    }
}