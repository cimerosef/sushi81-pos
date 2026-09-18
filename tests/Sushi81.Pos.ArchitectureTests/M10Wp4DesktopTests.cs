using System.IO;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Authority;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M10Wp4DesktopTests
{
    [TestMethod]
    public async Task CatalogueWorkflowStringsRemainPresentInFrenchAndChinese()
    {
        var french = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        var chinese = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        await chinese.ChangeLanguageAsync(chinese.Languages.Single(language => language.CultureName == "zh-CN"));

        foreach (var key in new[]
        {
            "CatalogueExport", "CatalogueImport", "CatalogueFileFilter", "CatalogueImportModeTitle",
            "CatalogueImportPreviewTitle", "NoDatabaseChangeYet", "OmittedRowsNotDeleted",
            "ConfirmImport", "CatalogueImportRefreshFailed", "CatalogueImportGenericIssue"
        })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(french.Localized[key]), $"French resource missing: {key}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(chinese.Localized[key]), $"Chinese resource missing: {key}");
            Assert.AreNotEqual(french.Localized[key], chinese.Localized[key], $"Language resources unexpectedly match: {key}");
        }
    }

    [TestMethod]
    public void MainWindowExposesLocalizedCatalogueActionsAndWorkflowHandlers()
    {
        var xaml = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs"));

        StringAssert.Contains(xaml, "CatalogueExport");
        StringAssert.Contains(xaml, "CatalogueImport");
        StringAssert.Contains(xaml, "CatalogueWorkflow.CanExport");
        StringAssert.Contains(xaml, "CatalogueWorkflow.CanImport");
        StringAssert.Contains(codeBehind, "ExportToPathAsync");
        StringAssert.Contains(codeBehind, "PreviewFromPathAsync");
        StringAssert.Contains(codeBehind, "CatalogueImportPreviewDialog");
    }

    [TestMethod]
    public void WorkflowStartsBusyAndAuthoritySafeWithoutAPreview()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var exporter = new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway());
        var importer = new CatalogueImportService(new EmptyImportGateway(), new EmptyBaselineQueries());
        var workflow = new CatalogueWorkbookWorkflowViewModel(exporter, importer, guard);

        Assert.IsFalse(workflow.IsBusy);
        Assert.IsTrue(workflow.CanExport);
        Assert.IsTrue(workflow.CanImport);
        Assert.IsTrue(workflow.IsAuthoritative);
        Assert.IsFalse(workflow.CanConfirm);
        Assert.IsEmpty(workflow.Issues);
        Assert.IsEmpty(workflow.AffectedRows);
    }

    [TestMethod]
    public async Task ExportFailurePreservesExistingTargetAndCleansSiblingTempFile()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var exporter = new CatalogueWorkbookService(new EmptySnapshotQueries(), new ThrowingWorkbookGateway());
        var importer = new CatalogueImportService(new EmptyImportGateway(), new EmptyBaselineQueries());
        var workflow = new CatalogueWorkbookWorkflowViewModel(exporter, importer, guard);
        var directory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "catalogue.xlsx");
            await File.WriteAllTextAsync(target, "original");
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => workflow.ExportToPathAsync(target));
            Assert.AreEqual("original", await File.ReadAllTextAsync(target));
            Assert.IsEmpty(Directory.EnumerateFiles(directory, ".catalogue.xlsx.*.tmp"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task ChangedCommitRefreshesExactlyOnceAndFailedCommitCannotBeRetried()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        using var changedStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
        var changedNotifier = new CountingNotifier();
        var changedImporter = new CatalogueImportService(new EmptyImportGateway(), new EmptyBaselineQueries(), changedStore, guard, changedNotifier);
        var changedWorkflow = new CatalogueWorkbookWorkflowViewModel(
            new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()),
            changedImporter,
            guard,
            refreshAfterChangedImport: _ => { changedStore.RefreshCount++; return Task.CompletedTask; });
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(source, "synthetic");
        try
        {
            await changedWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.AddOnly);
            Assert.IsTrue(changedWorkflow.CanConfirm);
            var changed = await changedWorkflow.CommitPreviewAsync();
            Assert.IsTrue(changed.Succeeded);
            Assert.IsTrue(changed.Changed);
            Assert.AreEqual(1, changedStore.RefreshCount);
            Assert.AreEqual(1, changedStore.CommitCount);
            Assert.AreEqual(1, changedNotifier.Count);

            using var failedStore = new FakeImportStore(CatalogueImportCommitResult.Failure(new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "persistence-conflict", "synthetic failure")));
            var failedImporter = new CatalogueImportService(new EmptyImportGateway(), new EmptyBaselineQueries(), failedStore, guard, new CountingNotifier());
            var failedWorkflow = new CatalogueWorkbookWorkflowViewModel(
                new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()),
                failedImporter,
                guard);
            await failedWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.AddOnly);
            Assert.IsTrue(failedWorkflow.CanConfirm);
            var failed = await failedWorkflow.CommitPreviewAsync();
            Assert.IsFalse(failed.Succeeded);
            Assert.IsFalse(failedWorkflow.CanConfirm);
            Assert.AreEqual(1, failedStore.CommitCount);
        }
        finally { File.Delete(source); }
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

        Assert.Fail($"Could not locate repository file: {Path.Combine(parts)}");
        return string.Empty;
    }

    private sealed class EmptySnapshotQueries : ICatalogueWorkbookSnapshotQueries
    {
        public Task<IReadOnlyList<CatalogueWorkbookProduct>> ReadCatalogueWorkbookSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CatalogueWorkbookProduct>>([]);
    }

    private sealed class EmptyWorkbookGateway : ICatalogueWorkbookGateway
    {
        public Task WriteAsync(CatalogueWorkbookExport model, Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ThrowingWorkbookGateway : ICatalogueWorkbookGateway
    {
        public async Task WriteAsync(CatalogueWorkbookExport model, Stream destination, CancellationToken cancellationToken = default)
        {
            await destination.WriteAsync("partial"u8.ToArray(), cancellationToken);
            throw new InvalidOperationException("synthetic export failure");
        }
    }

    private sealed class EmptyImportGateway : ICatalogueWorkbookImportGateway
    {
        public Task<CatalogueImportWorkbook> ReadAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion, [], [], [], [], [], sourceName));
    }

    private sealed class EmptyBaselineQueries : ICatalogueImportBaselineQueries
    {
        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogueImportBaseline.Empty);
    }

    private sealed class FakeImportStore(CatalogueImportCommitResult result) : ICatalogueImportStore, IDisposable
    {
        public int CommitCount { get; private set; }
        public int RefreshCount { get; set; }

        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogueImportBaseline.Empty);

        public Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.FromResult(result);
        }

        public void Dispose() { }
    }

    private sealed class CountingNotifier : IDurableChangeNotifier
    {
        public int Count { get; private set; }

        public Task NotifyCommittedAsync(CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.CompletedTask;
        }
    }
}
