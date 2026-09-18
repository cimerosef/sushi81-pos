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

    [TestMethod]
    public async Task CommitOutcomeSeparatesPreResultFailureNoOpAndRefreshFailure()
    {
        using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(source, "synthetic");
        try
        {
            using var throwingStore = new ThrowingImportStore();
            var throwingWorkflow = CreateWorkflow(throwingStore, guard);
            await throwingWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            var thrown = await throwingWorkflow.CommitPreviewAsync();
            Assert.IsFalse(thrown.Succeeded);
            Assert.AreEqual(CatalogueImportWorkflowOutcome.CommitFailed, throwingWorkflow.Outcome);
            Assert.IsFalse(throwingWorkflow.CanConfirm);
            Assert.IsEmpty(throwingWorkflow.Issues.Where(issue => issue.Message.Contains("synthetic", StringComparison.OrdinalIgnoreCase)));

            using var noOpStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: false));
            var noOpWorkflow = CreateWorkflow(noOpStore, guard);
            await noOpWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            var noOp = await noOpWorkflow.CommitPreviewAsync();
            Assert.IsTrue(noOp.Succeeded);
            Assert.IsFalse(noOp.Changed);
            Assert.AreEqual(CatalogueImportWorkflowOutcome.NoChange, noOpWorkflow.Outcome);
            Assert.AreEqual(0, noOpStore.RefreshCount);

            using var changedStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
            var refreshFailureWorkflow = CreateWorkflow(changedStore, guard, _ => throw new InvalidOperationException("synthetic refresh failure"));
            await refreshFailureWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            var refreshed = await refreshFailureWorkflow.CommitPreviewAsync();
            Assert.IsTrue(refreshed.Succeeded);
            Assert.IsTrue(refreshed.Changed);
            Assert.AreEqual(CatalogueImportWorkflowOutcome.RefreshFailed, refreshFailureWorkflow.Outcome);
            Assert.AreEqual(1, changedStore.CommitCount);
            Assert.IsFalse(refreshFailureWorkflow.CanConfirm);
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public void EveryAcceptedImportCodeHasDeliberateLocalizedCoverageAndUnknownFallbackIsSafe()
    {
        var required = new[]
        {
            "unreadable-workbook", "missing-sheet", "unsupported-sheet", "unsupported-contract-version", "business-sheet-visibility",
            "descriptor-corrupt", "duplicate-manifest-key", "duplicate-row-binding", "formula-not-allowed", "malformed-entity-id",
            "manifest-corrupt", "metadata-corrupt", "metadata-visibility", "misbound-identity", "missing-header", "sheet-order",
            "unknown-row-key", "wrong-parent-binding", "add-only-existing-binding", "add-only-product-code-collision", "ambiguous-parent",
            "category-short-code-duplicate", "category-short-code-too-long", "conflicting-category-short-code", "duplicate-category-name",
            "duplicate-entity-id", "duplicate-product-code", "duplicate-row-key", "existing-category-short-code-change", "invalid-group-structure",
            "invalid-mode", "invalid-option-structure", "invalid-scalar", "missing-parent", "price-negative", "required-active-choices",
            "required-field", "stale-conflict", "unknown-entity-id", "vat-range", "wrong-entity-type", "category-name-duplicate",
            "category-operation-forbidden", "category-payload-mismatch", "category-reference-missing", "category-short-code-invalid",
            "category-short-code-mismatch", "contradictory-operation", "contradictory-state-operation", "duplicate-baseline-category-id",
            "duplicate-baseline-group-id", "duplicate-baseline-option-id", "duplicate-baseline-product-id", "duplicate-create", "duplicate-modify",
            "duplicate-operation", "duplicate-state-operation", "invalid-category-key", "invalid-group-action", "invalid-local-key",
            "invalid-operation", "invalid-operation-id", "invalid-product", "misbound-reference", "mixed-create", "modify-missing",
            "modify-redundant", "orphan-category", "parent-payload-mismatch", "parent-reference-missing", "reparent-forbidden",
            "state-operation-missing", "state-payload-mismatch", "state-redundant", "unexpected-category-reference", "unexpected-parent-reference",
            "unknown-category-reference", "unknown-parent-reference", "authority-blocked", "baseline-token-mismatch", "commit-store-unavailable",
            "concurrent-write-conflict", "invalid-allocated-id", "invalid-request", "persistence-conflict", "preview-required", "stale-baseline",
            "transaction-failed", "unknown-reference"
        };
        CollectionAssert.IsSubsetOf(required, CatalogueImportIssuePresenter.KnownStableCodes.ToArray());

        var french = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true).Localized;
        var issue = new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "future-code", "raw English exception text");
        var message = CatalogueImportIssuePresenter.Message(issue, french);
        StringAssert.Contains(message, "future-code");
        Assert.IsFalse(message.Contains("raw English exception text", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProductionCompositionKeepsOneCatalogueStoreAndOneImportServicePath()
    {
        var composition = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "CompositionRoot.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        StringAssert.Contains(composition, "var catalogueStore = new SqliteCatalogueStore");
        Assert.AreEqual(1, CountOccurrences(composition, "new CatalogueWorkbookService(catalogueStore, catalogueWorkbookGateway)"));
        Assert.AreEqual(1, CountOccurrences(composition, "new CatalogueImportService(") );
        StringAssert.Contains(composition, "catalogueStore,\n                catalogueStore,");
        var localization = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "Localization.cs"));
        StringAssert.Contains(localization, "RefreshAfterCatalogueImportAsync");
    }

    [TestMethod]
    public void WpfDialogsHaveThreeSizeLayoutAndLifecycleSafetyHooks()
    {
        var dialogs = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "CatalogueImportDialogs.cs"));
        StringAssert.Contains(dialogs, "Width = 520");
        StringAssert.Contains(dialogs, "MinWidth = 440");
        StringAssert.Contains(dialogs, "Width = 940");
        StringAssert.Contains(dialogs, "Height = 700");
        StringAssert.Contains(dialogs, "MinWidth = 640");
        StringAssert.Contains(dialogs, "MinHeight = 480");
        StringAssert.Contains(dialogs, "VerticalScrollBarVisibility = ScrollBarVisibility.Auto");
        StringAssert.Contains(dialogs, "PreviewKeyDown");
        StringAssert.Contains(dialogs, "workflow.IsBusy");
        StringAssert.Contains(dialogs, "CatalogueImportWorkflowOutcome.NoChange");
        StringAssert.Contains(dialogs, "Read(localized, \"Close\"");
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

    private sealed class ThrowingImportStore : ICatalogueImportStore, IDisposable
    {
        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogueImportBaseline.Empty);

        public Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("synthetic commit failure");

        public void Dispose() { }
    }

    private static CatalogueWorkbookWorkflowViewModel CreateWorkflow(
        ICatalogueImportStore store,
        IWriteAuthorityGuard guard,
        Func<CancellationToken, Task>? refresh = null)
    {
        var importer = new CatalogueImportService(new EmptyImportGateway(), new EmptyBaselineQueries(), store, guard, new CountingNotifier());
        return new CatalogueWorkbookWorkflowViewModel(
            new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()),
            importer,
            guard,
            refreshAfterChangedImport: refresh);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;

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
