using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Authority;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M10Wp4DesktopTests
{
    private static readonly string[] RenderCultures = ["fr-FR", "zh-CN"];
    private static readonly (double Width, double Height)[] RenderSizes = [(640d, 480d), (940d, 700d), (1280d, 900d)];

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
            var syntheticIssue = throwingWorkflow.Issues.Single();
            Assert.AreEqual(string.Empty, syntheticIssue.Worksheet);
            Assert.AreEqual(string.Empty, syntheticIssue.Row);
            Assert.AreEqual(string.Empty, syntheticIssue.Field);
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
    public async Task WorkflowAuthorityAndPresentationBarrierRemainFailClosed()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(source, "synthetic");
        try
        {
            using var readOnlyGuard = new WriteAuthorityGuard(WriteAuthorityState.NonAuthoritativeReadOnly);
            var readOnlyWorkflow = CreateWorkflow(new FakeImportStore(CatalogueImportCommitResult.Success(changed: true)), readOnlyGuard);
            Assert.IsTrue(readOnlyWorkflow.CanExport);
            Assert.IsTrue(readOnlyWorkflow.CanImport);
            await readOnlyWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            Assert.IsFalse(readOnlyWorkflow.CanConfirm);
            Assert.AreEqual(CatalogueImportMode.Update, readOnlyWorkflow.Preview!.Preview.Mode);

            using var authoritativeGuard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            var blocked = false;
            var authoritativeWorkflow = CreateWorkflow(
                new FakeImportStore(CatalogueImportCommitResult.Success(changed: true)),
                authoritativeGuard,
                presentationBlocked: () => blocked);
            await authoritativeWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            Assert.IsTrue(authoritativeWorkflow.CanConfirm);
            await authoritativeWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.AddOnly);
            Assert.AreEqual(CatalogueImportMode.AddOnly, authoritativeWorkflow.Preview!.Preview.Mode);
            Assert.IsTrue(authoritativeWorkflow.CanConfirm);
            blocked = true;
            authoritativeWorkflow.RefreshPresentationState();
            Assert.IsFalse(authoritativeWorkflow.CanExport);
            Assert.IsFalse(authoritativeWorkflow.CanImport);
            Assert.IsFalse(authoritativeWorkflow.CanConfirm);
            blocked = false;
            authoritativeWorkflow.RefreshPresentationState();
            Assert.IsTrue(authoritativeWorkflow.CanExport);
            Assert.IsTrue(authoritativeWorkflow.CanImport);
            Assert.IsTrue(authoritativeWorkflow.CanConfirm);
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public async Task ProductionShellCatalogueWorkflowRefreshCallbackCompletesThroughThePresentationBarrier()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(source, "synthetic");
        try
        {
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            using var store = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
            var importer = new CatalogueImportService(new EmptyImportGateway(), new EmptyBaselineQueries(), store, guard, new CountingNotifier());
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(),
                startupSucceeded: true,
                authorityGuard: guard,
                catalogueWorkbookService: new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()),
                catalogueImportService: importer);
            Assert.IsNotNull(shell.CatalogueWorkflow);
            await shell.CatalogueWorkflow!.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            var committed = await shell.CatalogueWorkflow.CommitPreviewAsync();
            Assert.IsTrue(committed.Succeeded);
            Assert.IsTrue(committed.Changed);
            Assert.AreEqual(CatalogueImportWorkflowOutcome.Changed, shell.CatalogueWorkflow.Outcome);
            Assert.IsTrue(shell.CanWrite);
            Assert.IsTrue(shell.CatalogueWorkflow.CanExport);
            Assert.IsTrue(shell.CatalogueWorkflow.CanImport);
            Assert.AreEqual(1, store.CommitCount);
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public void CatalogueImportModeDialogRequiresAnExplicitChoiceOnRealSta()
    {
        RunOnSta(() =>
        {
            var localized = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true).Localized;
            var dialog = new CatalogueImportModeDialog(null!, localized);
            dialog.Show();
            dialog.UpdateLayout();
            var update = GetPrivateField<RadioButton>(dialog, "update");
            var addOnly = GetPrivateField<RadioButton>(dialog, "addOnly");
            var continueButton = GetPrivateField<Button>(dialog, "continueButton");
            Assert.AreNotEqual(true, update.IsChecked);
            Assert.AreNotEqual(true, addOnly.IsChecked);
            Assert.IsFalse(continueButton.IsEnabled);
            update.IsChecked = true;
            Assert.IsTrue(continueButton.IsEnabled);
            dialog.Close();

            var closeDialog = new CatalogueImportModeDialog(null!, localized);
            closeDialog.Show();
            closeDialog.Close();
            Assert.IsNull(closeDialog.SelectedMode);
        });
    }

    [TestMethod]
    public async Task PreviewDialogFailureKeepsStructuredContextAndDisablesRetryOnRealSta()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(source, "synthetic");
        try
        {
            RunOnSta(() =>
            {
                using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                var issue = new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "persistence-conflict", "synthetic persistence conflict", "Products", 7, "price-ttc");
                using var store = new FakeImportStore(CatalogueImportCommitResult.Failure(issue));
                var workflow = CreateWorkflow(store, guard);
                workflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var localized = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true).Localized;
                var dialog = new CatalogueImportPreviewDialog(null!, workflow, workflow.Preview!, localized)
                {
                    Width = 940,
                    Height = 700,
                    ShowInTaskbar = false,
                };
                dialog.Show();
                dialog.UpdateLayout();
                GetPrivateField<Button>(dialog, "confirm").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                dialog.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                dialog.UpdateLayout();
                var presented = workflow.Issues.Single();
                Assert.AreEqual("Products", presented.Worksheet);
                Assert.AreEqual("7", presented.Row);
                Assert.AreEqual("price-ttc", presented.Field);
                Assert.AreEqual(1, store.CommitCount);
                var confirm = GetPrivateField<Button>(dialog, "confirm");
                Assert.IsFalse(confirm.IsEnabled);
                Assert.AreEqual(localized["Close"], GetPrivateField<Button>(dialog, "cancel").Content?.ToString());
                dialog.Close();
            });
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public async Task PreviewDialogNoOpAndRefreshFailureRemainVisibleAndNonRetriableOnSta()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(source, "synthetic");
        try
        {
            RunOnSta(() =>
            {
                using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
                var localized = shell.Localized;

                using var noOpStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: false));
                var noOpWorkflow = CreateWorkflow(noOpStore, guard);
                noOpWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var noOpDialog = new CatalogueImportPreviewDialog(null!, noOpWorkflow, noOpWorkflow.Preview!, localized);
                noOpDialog.Show();
                noOpDialog.UpdateLayout();
                ClickButton(noOpDialog, localized["ConfirmImport"]);
                PumpDispatcher(noOpDialog);
                Assert.AreEqual(CatalogueImportWorkflowOutcome.NoChange, noOpWorkflow.Outcome);
                Assert.AreEqual(localized["CatalogueImportNoChange"], GetPrivateField<TextBlock>(noOpDialog, "status").Text);
                Assert.AreEqual(1, noOpStore.CommitCount);
                Assert.AreEqual(localized["Close"], GetPrivateField<Button>(noOpDialog, "cancel").Content?.ToString());
                noOpDialog.Close();

                using var changedStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
                var refreshFailureWorkflow = CreateWorkflow(changedStore, guard, _ => throw new InvalidOperationException("synthetic refresh failure"));
                refreshFailureWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var refreshDialog = new CatalogueImportPreviewDialog(null!, refreshFailureWorkflow, refreshFailureWorkflow.Preview!, localized);
                refreshDialog.Show();
                refreshDialog.UpdateLayout();
                ClickButton(refreshDialog, localized["ConfirmImport"]);
                PumpDispatcher(refreshDialog);
                Assert.AreEqual(CatalogueImportWorkflowOutcome.RefreshFailed, refreshFailureWorkflow.Outcome);
                Assert.AreEqual(localized["CatalogueImportRefreshFailed"], GetPrivateField<TextBlock>(refreshDialog, "status").Text);
                Assert.AreEqual(1, changedStore.CommitCount);
                Assert.IsFalse(refreshFailureWorkflow.CanConfirm);
                refreshDialog.Close();
            });
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public async Task CatalogueDialogsRenderActionsAtSupportedSizesInFrenchAndChinese()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(source, "synthetic");
        try
        {
            RunOnSta(() =>
            {
                foreach (var cultureName in RenderCultures)
                foreach (var size in RenderSizes)
                {
                    using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                    using var store = new FakeImportStore(CatalogueImportCommitResult.Success(changed: false));
                    var workflow = CreateWorkflow(store, guard);
                    workflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                    using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
                    if (cultureName == "zh-CN") shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == cultureName)).GetAwaiter().GetResult();
                    var dialog = new CatalogueImportPreviewDialog(null!, workflow, workflow.Preview!, shell.Localized)
                    {
                        Width = size.Width,
                        Height = size.Height,
                        ShowInTaskbar = false,
                    };
                    dialog.Show();
                    dialog.UpdateLayout();
                    dialog.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
                    dialog.UpdateLayout();
                    var buttons = new[] { GetPrivateField<Button>(dialog, "confirm"), GetPrivateField<Button>(dialog, "cancel") };
                    foreach (var button in buttons)
                    {
                        Assert.IsFalse(string.IsNullOrWhiteSpace(button.Content?.ToString()), $"{cultureName} action text is missing.");
                        Assert.IsGreaterThanOrEqualTo(80d, button.MinWidth, $"{cultureName} action '{button.Content}' has no usable minimum width.");
                    }
                    dialog.Close();
                }
            });
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
        using var chineseShell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        chineseShell.ChangeLanguageAsync(chineseShell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
        foreach (var code in CatalogueImportIssuePresenter.KnownStableCodes)
        {
            var issue = new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, code, "raw English exception text");
            var frMessage = CatalogueImportIssuePresenter.Message(issue, french);
            var zhMessage = CatalogueImportIssuePresenter.Message(issue, chineseShell.Localized);
            Assert.IsFalse(string.IsNullOrWhiteSpace(frMessage), $"French mapping is empty for {code}.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(zhMessage), $"Chinese mapping is empty for {code}.");
            Assert.IsFalse(frMessage.Contains("raw English exception text", StringComparison.Ordinal));
            Assert.IsFalse(zhMessage.Contains("raw English exception text", StringComparison.Ordinal));
        }

        var unknown = new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "future-code", "raw English exception text");
        var frUnknown = CatalogueImportIssuePresenter.Message(unknown, french);
        var zhUnknown = CatalogueImportIssuePresenter.Message(unknown, chineseShell.Localized);
        StringAssert.Contains(frUnknown, "future-code");
        StringAssert.Contains(zhUnknown, "future-code");
        Assert.IsFalse(frUnknown.Contains("raw English exception text", StringComparison.Ordinal));
        Assert.IsFalse(zhUnknown.Contains("raw English exception text", StringComparison.Ordinal));
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
        Func<CancellationToken, Task>? refresh = null,
        Func<bool>? presentationBlocked = null)
    {
        var importer = new CatalogueImportService(new EmptyImportGateway(), new EmptyBaselineQueries(), store, guard, new CountingNotifier());
        return new CatalogueWorkbookWorkflowViewModel(
            new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()),
            importer,
            guard,
            presentationRefreshBlocked: presentationBlocked,
            refreshAfterChangedImport: refresh);
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Missing private field {fieldName}.");
        return (T)field!.GetValue(instance)!;
    }

    private static void ClickButton(Window dialog, string content)
    {
        var button = content.Contains("Confirm", StringComparison.Ordinal)
            ? GetPrivateField<Button>(dialog, "confirm")
            : GetPrivateField<Button>(dialog, "cancel");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void PumpDispatcher(Window window) => window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));


    private static IEnumerable<T> GetVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T typed) yield return typed;
        int count;
        try { count = VisualTreeHelper.GetChildrenCount(root); }
        catch (InvalidOperationException) { yield break; }
        for (var index = 0; index < count; index++)
        {
            foreach (var descendant in GetVisualDescendants<T>(VisualTreeHelper.GetChild(root, index))) yield return descendant;
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
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
