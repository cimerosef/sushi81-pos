using System.IO;
using System.Xml.Linq;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M12Wp4ArchiveAccessTests
{
    [TestMethod]
    public void ArchiveSurfaceIsReadOnlyAndOffersOnlyExplicitKitchenAndCustomerReprints()
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
        StringAssert.Contains(archiveTab, "OnReprintArchiveKitchen");
        StringAssert.Contains(archiveTab, "OnReprintArchiveCustomer");
        Assert.AreEqual(2, archiveTab.Split("OnReprintArchive", StringSplitOptions.None).Length - 1);
        StringAssert.Contains(archiveTab, "ArchiveAccess.CanReprint");
        Assert.IsFalse(archiveTab.Contains("OnSave", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(archiveTab.Contains("OnCancel", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(archiveTab.Contains("AutoPrint", StringComparison.OrdinalIgnoreCase));
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
        Assert.IsFalse(infrastructure.Contains("OneDrive", StringComparison.OrdinalIgnoreCase));

        var requiredKeys = new[]
        {
            "ArchiveAccess", "ArchiveYear", "ArchiveSearch", "ArchiveCopy", "ArchiveReadOnlyNotice",
            "OrderReprintKitchen", "OrderReprintCustomer", "ArchiveStatusAll", "ArchiveNoArchives", "ArchiveAccessFailed", "ArchiveInvalidDateRange",
            "ArchiveCopySucceeded", "ArchiveCopyFailed"
        };
        foreach (var resource in new[] { "Resources.resx", "Resources.zh-CN.resx" })
        {
            var document = XDocument.Load(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "Properties", resource));
            var names = document.Root!.Elements("data").Select(element => (string?)element.Attribute("name")).ToHashSet(StringComparer.Ordinal);
            CollectionAssert.IsSubsetOf(requiredKeys, names.ToArray());
        }

        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs"));
        var copyHandlerStart = codeBehind.IndexOf("private async void OnCopyArchive", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, copyHandlerStart);
        var copyHandlerEnd = codeBehind.IndexOf("\n    private ", copyHandlerStart + 1, StringComparison.Ordinal);
        Assert.IsGreaterThan(copyHandlerStart, copyHandlerEnd);
        var copyHandler = codeBehind[copyHandlerStart..copyHandlerEnd];
        var dialogResultCheck = copyHandler.IndexOf("if (dialog.ShowDialog(this) != true) return;", StringComparison.Ordinal);
        var copyInvocation = copyHandler.IndexOf("CopySelectedAsync", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, dialogResultCheck);
        Assert.IsGreaterThan(dialogResultCheck, copyInvocation);
    }

    [TestMethod]
    public async Task ArchiveReprintRequiresExplicitHydratedSelectionAndUsesCapturedSnapshot()
    {
        var access = new ControlledArchiveAccess([Archive(2026)]);
        var printer = new RecordingArchivedPrintService();
        var viewModel = new AnnualArchiveAccessViewModel(access, printer);

        Assert.IsFalse(viewModel.CanReprint);
        await viewModel.ReprintSelectedAsync(PrintDocumentKind.Kitchen);
        Assert.AreEqual(0, printer.Calls);

        await viewModel.RefreshAsync();
        viewModel.SelectedArchive = access.Archives[0];
        var originalRow = Row(Guid.Parse("52700000-0000-0000-0000-000000000021"));
        viewModel.Orders.Add(originalRow);
        viewModel.SelectedRow = originalRow;
        await WaitForAsync(() => viewModel.SelectedOrder?.Id == originalRow.Id);

        var originalSnapshot = viewModel.SelectedOrder;
        Assert.AreEqual(0, printer.Calls, "Selecting or hydrating an archive order must never auto-print.");
        Assert.IsTrue(viewModel.CanReprint);
        var print = viewModel.ReprintSelectedAsync(PrintDocumentKind.Customer);
        Assert.AreEqual(1, printer.Calls);
        Assert.AreSame(originalSnapshot, printer.LastSnapshot);
        Assert.AreEqual(PrintDocumentKind.Customer, printer.LastKind);
        Assert.IsFalse(viewModel.CanReprint, "A concurrent archive operation must disable duplicate print actions.");

        var replacementRow = Row(Guid.Parse("52700000-0000-0000-0000-000000000022"));
        viewModel.Orders.Add(replacementRow);
        viewModel.SelectedRow = replacementRow;
        await WaitForAsync(() => viewModel.SelectedOrder?.Id == replacementRow.Id);
        printer.Completion.SetResult(PrintDocumentResult.Success(new(
            PrintDocumentKind.Customer,
            PrintIntent.ExplicitReprint,
            originalSnapshot!.Id,
            originalSnapshot.Reference,
            "synthetic",
            false,
            false)));
        await print;

        Assert.AreEqual(replacementRow.Id, viewModel.SelectedOrder!.Id);
        Assert.AreEqual(string.Empty, viewModel.PrintStatusMessage, "A late print result must not be presented as the outcome for the newly selected order.");
    }

    [TestMethod]
    public async Task DiscoveryDoesNotSelectOrQueryAnArchiveUntilTheOperatorSelectsAYear()
    {
        var newest = Archive(2027);
        var oldest = Archive(2026);
        var access = new ControlledArchiveAccess([newest, oldest]);
        var viewModel = new AnnualArchiveAccessViewModel(access);

        await viewModel.RefreshAsync();

        Assert.HasCount(2, viewModel.AvailableArchives);
        Assert.IsNull(viewModel.SelectedArchive);
        Assert.IsEmpty(viewModel.Orders);
        Assert.IsNull(viewModel.SelectedOrder);
        Assert.IsEmpty(access.Searches);

        viewModel.SelectedArchive = oldest;

        Assert.HasCount(1, access.Searches);
        Assert.AreEqual(2026, access.Searches[0].ArchiveYear);
    }

    [TestMethod]
    public async Task RefreshPreservesAnExplicitYearAndClearsItWhenThatArchiveDisappears()
    {
        var access = new ControlledArchiveAccess([Archive(2027), Archive(2026)]);
        var viewModel = new AnnualArchiveAccessViewModel(access);
        await viewModel.RefreshAsync();
        viewModel.SelectedArchive = access.Archives[1];

        access.Archives = [Archive(2028), Archive(2027), Archive(2026)];
        await viewModel.RefreshAsync();
        Assert.AreEqual(2026, viewModel.SelectedArchive!.ArchiveYear);

        access.Archives = [Archive(2028), Archive(2027)];
        await viewModel.RefreshAsync();
        Assert.IsNull(viewModel.SelectedArchive);
        Assert.IsEmpty(viewModel.Orders);
        Assert.IsNull(viewModel.SelectedRow);
        Assert.IsNull(viewModel.SelectedOrder);
    }

    [TestMethod]
    public async Task LateArchiveDiscoveryCannotReplaceARefreshThatCompletedLater()
    {
        var access = new ControlledArchiveAccess([Archive(2027)], holdDiscoveries: true);
        var viewModel = new AnnualArchiveAccessViewModel(access);

        var staleRefresh = viewModel.RefreshAsync();
        var currentRefresh = viewModel.RefreshAsync();
        Assert.HasCount(2, access.Discoveries);

        access.Discoveries[1].SetResult([Archive(2028)]);
        await currentRefresh;
        access.Discoveries[0].SetResult([Archive(2027)]);
        await staleRefresh;

        Assert.HasCount(1, viewModel.AvailableArchives);
        Assert.AreEqual(2028, viewModel.AvailableArchives.Single().ArchiveYear);
        Assert.IsNull(viewModel.SelectedArchive);
        Assert.IsEmpty(viewModel.Orders);
    }

    [TestMethod]
    public async Task LateSearchFromPreviouslySelectedYearCannotReplaceCurrentRows()
    {
        var access = new ControlledArchiveAccess([Archive(2027), Archive(2026)], holdSearches: true);
        var viewModel = new AnnualArchiveAccessViewModel(access);
        await viewModel.RefreshAsync();

        viewModel.SelectedArchive = access.Archives[0];
        viewModel.SelectedArchive = access.Archives[1];
        Assert.HasCount(2, access.Searches);

        var currentRow = Row(Guid.Parse("52700000-0000-0000-0000-000000000002"));
        access.Searches[1].Completion.SetResult([currentRow]);
        await WaitForAsync(() => viewModel.Orders.Any(row => row.Id == currentRow.Id));

        var staleRow = Row(Guid.Parse("52700000-0000-0000-0000-000000000001"));
        access.Searches[0].Completion.SetResult([staleRow]);
        await WaitForAsync(() => !viewModel.IsBusy);

        Assert.AreEqual(2026, viewModel.SelectedArchive!.ArchiveYear);
        Assert.AreEqual(currentRow.Id, viewModel.Orders.Single().Id);
    }

    [TestMethod]
    public async Task LateHydrationFromPreviouslySelectedOrderCannotReplaceCurrentDetail()
    {
        var access = new ControlledArchiveAccess([Archive(2026)], holdDetails: true);
        var viewModel = new AnnualArchiveAccessViewModel(access);
        await viewModel.RefreshAsync();
        viewModel.SelectedArchive = access.Archives[0];

        var staleRow = Row(Guid.Parse("52700000-0000-0000-0000-000000000011"));
        var currentRow = Row(Guid.Parse("52700000-0000-0000-0000-000000000012"));
        viewModel.SelectedRow = staleRow;
        viewModel.SelectedRow = currentRow;
        Assert.HasCount(2, access.Details);

        access.Details[1].Completion.SetResult(Snapshot(currentRow.Id));
        await WaitForAsync(() => viewModel.SelectedOrder?.Id == currentRow.Id);

        access.Details[0].Completion.SetResult(Snapshot(staleRow.Id));
        await WaitForAsync(() => !viewModel.IsBusy);

        Assert.AreEqual(currentRow.Id, viewModel.SelectedRow!.Id);
        Assert.AreEqual(currentRow.Id, viewModel.SelectedOrder!.Id);
    }

    private static AnnualArchiveDescriptor Archive(int year) => new(year, $"archive-{year}.db", 1, DateTimeOffset.UnixEpoch);

    private static OrderBrowserRow Row(Guid id) => new(id, new DateOnly(2026, 12, 31), new TimeOnly(12, 0), FulfilmentMode.Retrait, OrderStatus.Closed, Money.FromCents(100), null);

    private static OrderSnapshot Snapshot(Guid id) => new(
        id, OrderSourceType.Pos, OrderStatus.Closed, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch, null, FulfilmentMode.Retrait, new DateOnly(2026, 12, 31), new TimeOnly(12, 0),
        false, null, null, null, Money.FromCents(100), false, false, null, Money.Zero, [], []);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }

    private sealed class RecordingArchivedPrintService : IArchivedOrderPrintApplicationService
    {
        public int Calls { get; private set; }
        public OrderSnapshot? LastSnapshot { get; private set; }
        public PrintDocumentKind LastKind { get; private set; }
        public TaskCompletionSource<PrintDocumentResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PrintDocumentResult> ReprintAsync(OrderSnapshot archivedSnapshot, PrintDocumentKind kind, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastSnapshot = archivedSnapshot;
            LastKind = kind;
            return Completion.Task;
        }
    }

    private sealed class ControlledArchiveAccess(
        IReadOnlyList<AnnualArchiveDescriptor> archives,
        bool holdSearches = false,
        bool holdDetails = false,
        bool holdDiscoveries = false) : IAnnualArchiveAccess
    {
        public IReadOnlyList<AnnualArchiveDescriptor> Archives { get; set; } = archives;
        public List<TaskCompletionSource<IReadOnlyList<AnnualArchiveDescriptor>>> Discoveries { get; } = [];
        public List<(int ArchiveYear, TaskCompletionSource<IReadOnlyList<OrderBrowserRow>> Completion)> Searches { get; } = [];
        public List<(int ArchiveYear, Guid OrderId, TaskCompletionSource<OrderSnapshot?> Completion)> Details { get; } = [];

        public Task<IReadOnlyList<AnnualArchiveDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default)
        {
            if (!holdDiscoveries) return Task.FromResult(Archives);
            var completion = new TaskCompletionSource<IReadOnlyList<AnnualArchiveDescriptor>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Discoveries.Add(completion);
            return completion.Task;
        }

        public Task<IReadOnlyList<OrderBrowserRow>> SearchAsync(int archiveYear, AnnualArchiveSearchCriteria criteria, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<OrderBrowserRow>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Searches.Add((archiveYear, completion));
            if (!holdSearches) completion.SetResult([]);
            return completion.Task;
        }

        public Task<OrderSnapshot?> GetOrderAsync(int archiveYear, Guid orderId, CancellationToken cancellationToken = default)
        {
            if (!holdDetails) return Task.FromResult<OrderSnapshot?>(Snapshot(orderId));
            var completion = new TaskCompletionSource<OrderSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Details.Add((archiveYear, orderId, completion));
            return completion.Task;
        }

        public Task<AnnualArchiveCopyResult> CopyAsync(int archiveYear, string destinationPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AnnualArchiveCopyResult(archiveYear, destinationPath, 0, string.Empty));
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
