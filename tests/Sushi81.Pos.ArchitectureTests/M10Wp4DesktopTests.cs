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
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;
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
    public async Task AuthoritativeErrorAndWarningPreviewsKeepConfirmFailClosedOrEnabled()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            using var errorStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
            var errorGateway = new FixedImportGateway(new CatalogueImportWorkbook(
                CatalogueWorkbookSchema.ContractVersion, [], [], [], [],
                [new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "required-field", "synthetic error")]));
            var errorWorkflow = CreateWorkflow(errorGateway, errorStore, guard);
            await errorWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            Assert.IsTrue(errorWorkflow.Preview!.HasErrors);
            Assert.IsNull(errorWorkflow.Preview.Plan);
            Assert.IsFalse(errorWorkflow.CanConfirm);

            using var warningStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
            var warningGateway = new FixedImportGateway(new CatalogueImportWorkbook(
                CatalogueWorkbookSchema.ContractVersion, [], [], [], [],
                [new CatalogueImportIssue(CatalogueImportIssueSeverity.Warning, "future-code", "synthetic warning")]));
            var warningWorkflow = CreateWorkflow(warningGateway, warningStore, guard);
            await warningWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.AddOnly);
            Assert.IsFalse(warningWorkflow.Preview!.HasErrors);
            Assert.IsNotNull(warningWorkflow.Preview.Plan);
            Assert.AreEqual(1, warningWorkflow.Preview.Preview.WarningCount);
            Assert.IsTrue(warningWorkflow.CanConfirm);
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public async Task OwnerRealisticExistingCategoryShortCodeChangeUsesSpecificFrenchAndChineseGuidance()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            var workbook = new CatalogueImportWorkbook(CatalogueWorkbookSchema.ContractVersion,
                [
                    new CatalogueImportProductRow(2, "P-1", "Normal", "Plats", "PL", Money.FromCents(100), 20m, true, false, false, null, null),
                    new CatalogueImportProductRow(3, "P-2", "Blank preserve", "Plats", null, Money.FromCents(100), 20m, true, false, false, null, null),
                    new CatalogueImportProductRow(4, "P-3", "Changed", "Plats", "XX", Money.FromCents(100), 20m, true, false, false, null, null)
                ], [], [], [], []);
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            using var store = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true))
            {
                Baseline = new CatalogueImportBaseline([new CatalogueImportCategory(Guid.NewGuid(), "Plats", "PL")], [])
            };
            var notifier = new CountingNotifier();
            var importer = new CatalogueImportService(new FixedImportGateway(workbook), store, store, guard, notifier);
            var workflow = new CatalogueWorkbookWorkflowViewModel(
                new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()), importer, guard);

            await workflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);

            Assert.IsTrue(workflow.Preview!.HasErrors);
            Assert.IsNull(workflow.Preview.Plan);
            Assert.IsFalse(workflow.CanConfirm);
            Assert.IsNull(workflow.LastCommit);
            Assert.AreEqual(0, store.CommitCount);
            Assert.AreEqual(0, notifier.Count);

            using var french = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            workflow.ApplyLocalization(french.Localized);
            var frenchIssue = workflow.Issues.Single(issue => issue.Code == "existing-category-short-code-change");
            Assert.AreEqual("Products", frenchIssue.Worksheet);
            Assert.AreEqual("4", frenchIssue.Row);
            Assert.AreEqual("category_short_code", frenchIssue.Field);
            Assert.AreEqual(french.Localized["CatalogueImportCategoryShortCodeChange"], frenchIssue.Message);
            Assert.IsFalse(workflow.Issues.Any(issue => issue.Code == "conflicting-category-short-code"));

            using var chinese = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            await chinese.ChangeLanguageAsync(chinese.Languages.Single(language => language.CultureName == "zh-CN"));
            workflow.ApplyLocalization(chinese.Localized);
            var chineseIssue = workflow.Issues.Single(issue => issue.Code == "existing-category-short-code-change");
            Assert.AreEqual("Products", chineseIssue.Worksheet);
            Assert.AreEqual("4", chineseIssue.Row);
            Assert.AreEqual("category_short_code", chineseIssue.Field);
            Assert.AreEqual(chinese.Localized["CatalogueImportCategoryShortCodeChange"], chineseIssue.Message);
            Assert.IsFalse(workflow.Issues.Any(issue => issue.Code == "conflicting-category-short-code"));
            Assert.AreEqual(0, store.CommitCount);
            Assert.AreEqual(0, notifier.Count);
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
    public void ProductionShellCatalogueRefreshBarrierCoversAdminAndEntryOnSuccessAndFailure()
    {
        RunOnSta(() =>
        {
            var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
            File.WriteAllText(source, "synthetic");
            try
            {
                using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                var notifier = new CountingNotifier();
                using var successStore = new CatalogueRefreshStore();
                var successCatalogue = new CatalogueService(successStore, guard, notifier);
                var successSettings = new BusinessSettingsService(successStore, guard, notifier);
                using var successEntry = new OrderEntryService(
                    new OrderEntryCatalogueService(successStore), successStore, successStore,
                    new NoOpPrintDispatcher(), new DeterministicIdGenerator(), new FixedBusinessClock(), guard, notifier);
                using var successShell = CreateProductionShell(successStore, guard, successCatalogue, successSettings, successEntry);
                var successWorkflow = successShell.CatalogueWorkflow!;
                successWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var successCommit = successWorkflow.CommitPreviewAsync();
                PumpUntil(successStore.RefreshStarted.Task);
                Assert.IsFalse(successShell.CanWrite);
                Assert.IsFalse(successWorkflow.CanExport);
                Assert.IsFalse(successWorkflow.CanImport);
                Assert.IsFalse(successWorkflow.CanConfirm);
                successStore.ReleaseRefresh.TrySetResult(null);
                PumpUntil(successCommit);
                successCommit.GetAwaiter().GetResult();
                Assert.IsTrue(successShell.CanWrite);
                Assert.IsTrue(successWorkflow.CanExport);
                Assert.IsTrue(successWorkflow.CanImport);
                Assert.AreEqual(1, successWorkflow.Preview!.Preview.ProductCreateCount);
                Assert.AreEqual(2, successStore.CategoryReadCount, "Admin and Entry must each refresh Catalogue categories once.");
                Assert.AreEqual(2, successStore.ProductReadCount, "Admin and Entry must each refresh Catalogue products once.");
                Assert.AreEqual(1, successStore.CommitCount);

                using var failureStore = new CatalogueRefreshStore { ThrowOnRefresh = true };
                var failureCatalogue = new CatalogueService(failureStore, guard, notifier);
                var failureSettings = new BusinessSettingsService(failureStore, guard, notifier);
                using var failureEntry = new OrderEntryService(
                    new OrderEntryCatalogueService(failureStore), failureStore, failureStore,
                    new NoOpPrintDispatcher(), new DeterministicIdGenerator(), new FixedBusinessClock(), guard, notifier);
                using var failureShell = CreateProductionShell(failureStore, guard, failureCatalogue, failureSettings, failureEntry);
                var failureWorkflow = failureShell.CatalogueWorkflow!;
                failureWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var failure = failureWorkflow.CommitPreviewAsync().GetAwaiter().GetResult();
                Assert.IsTrue(failure.Succeeded);
                Assert.AreEqual(CatalogueImportWorkflowOutcome.RefreshFailed, failureWorkflow.Outcome);
                Assert.IsFalse(failureShell.CanWrite);
                Assert.IsFalse(failureWorkflow.CanExport);
                Assert.IsFalse(failureWorkflow.CanImport);
                Assert.IsFalse(failureWorkflow.CanConfirm);
                Assert.AreEqual(1, failureStore.CommitCount);
            }
            finally { File.Delete(source); }
        });
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
    public void CatalogueImportModeDialogRendersBothModesAndClosePathsOnRealSta()
    {
        RunOnSta(() =>
        {
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            var dialog = new CatalogueImportModeDialog(null!, shell.Localized) { ShowInTaskbar = false };
            dialog.Show();
            dialog.UpdateLayout();
            PumpDispatcher(dialog);

            var update = GetPrivateField<RadioButton>(dialog, "update");
            var addOnly = GetPrivateField<RadioButton>(dialog, "addOnly");
            var continueButton = GetPrivateField<Button>(dialog, "continueButton");
            Assert.AreNotEqual(true, update.IsChecked);
            Assert.AreNotEqual(true, addOnly.IsChecked);
            Assert.IsFalse(continueButton.IsEnabled);

            update.IsChecked = true;
            Assert.IsTrue(continueButton.IsEnabled);
            addOnly.IsChecked = true;
            Assert.IsTrue(continueButton.IsEnabled);
            dialog.Close();
            Assert.IsNull(dialog.SelectedMode);
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
                workflow.ApplyLocalization(localized);
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
                var issueRow = GetPrivateField<DataGrid>(dialog, "issuesGrid").Items.Cast<CatalogueImportIssuePresentation>().Single();
                Assert.AreEqual("Products", issueRow.Worksheet);
                Assert.AreEqual("7", issueRow.Row);
                Assert.AreEqual("price-ttc", issueRow.Field);
                Assert.AreEqual(localized["Errors"], issueRow.SeverityText);
                Assert.AreEqual(presented.Message, issueRow.Message);
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
    public void PreviewDialogCancelEscapeAndCloseDoNotCommitOnRealSta()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            RunOnSta(() =>
            {
                using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
                var localized = shell.Localized;

                using var cancelStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
                var cancelWorkflow = CreateWorkflow(cancelStore, guard);
                cancelWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var cancelDialog = CreatePreviewDialog(cancelWorkflow, localized);
                cancelDialog.ContentRendered += (_, _) => ClickButton(cancelDialog, localized["CatalogueImportCancel"]);
                Assert.AreNotEqual(true, cancelDialog.ShowDialog());
                Assert.AreEqual(0, cancelStore.CommitCount);

                using var escapeStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
                var escapeWorkflow = CreateWorkflow(escapeStore, guard);
                escapeWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var escapeDialog = CreatePreviewDialog(escapeWorkflow, localized);
                escapeDialog.ContentRendered += (_, _) => RaiseEscape(escapeDialog);
                Assert.AreNotEqual(true, escapeDialog.ShowDialog());
                Assert.AreEqual(0, escapeStore.CommitCount);

                using var closeStore = new FakeImportStore(CatalogueImportCommitResult.Success(changed: true));
                var closeWorkflow = CreateWorkflow(closeStore, guard);
                closeWorkflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var closeDialog = CreatePreviewDialog(closeWorkflow, localized);
                closeDialog.ContentRendered += (_, _) => closeDialog.Close();
                Assert.AreNotEqual(true, closeDialog.ShowDialog());
                Assert.AreEqual(0, closeStore.CommitCount);
            });
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public void PreviewDialogCommitInProgressBlocksCloseEscapeAndReentrancyOnRealSta()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            RunOnSta(() =>
            {
                using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
                var store = new BlockingImportStore(CatalogueImportCommitResult.Success(changed: true));
                var workflow = CreateWorkflow(store, guard);
                workflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                var dialog = CreatePreviewDialog(workflow, shell.Localized);
                Exception? callbackFailure = null;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        ClickButton(dialog, shell.Localized["ConfirmImport"]);
                        PumpUntil(store.CommitStarted.Task);

                        var confirm = GetPrivateField<Button>(dialog, "confirm");
                        var cancel = GetPrivateField<Button>(dialog, "cancel");
                        Assert.AreEqual(1, store.CommitCount);
                        Assert.IsFalse(confirm.IsEnabled);
                        Assert.IsFalse(cancel.IsEnabled);
                        RaiseEscape(dialog);
                        Assert.IsTrue(dialog.IsVisible, "Escape must not close a preview while commit is in progress.");
                        dialog.Close();
                        Assert.IsTrue(dialog.IsVisible, "Window close must be cancelled while commit is in progress.");
                        confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.AreEqual(1, store.CommitCount, "A second confirm cannot start a second commit.");

                        store.Release.TrySetResult(null);
                        PumpUntil(store.CommitFinished.Task);
                        PumpDispatcher(dialog);
                        Assert.AreEqual(1, store.CommitCount);
                    }
                    catch (Exception exception)
                    {
                        callbackFailure = exception;
                        store.Release.TrySetResult(null);
                    }
                    finally
                    {
                        if (dialog.IsVisible) dialog.Close();
                    }
                };
                dialog.ShowDialog();
                if (callbackFailure is not null)
                    ExceptionDispatchInfo.Capture(callbackFailure).Throw();
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
    public void PreviewDialogPreResultExceptionUsesCommitFailureContextAndNoRetryOnRealSta()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            RunOnSta(() =>
            {
                using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
                using var store = new ThrowingImportStore();
                var workflow = CreateWorkflow(store, guard);
                workflow.PreviewFromPathAsync(source, CatalogueImportMode.Update).GetAwaiter().GetResult();
                workflow.ApplyLocalization(shell.Localized);
                var dialog = CreatePreviewDialog(workflow, shell.Localized);
                dialog.Show();
                dialog.UpdateLayout();
                ClickButton(dialog, shell.Localized["ConfirmImport"]);
                PumpDispatcher(dialog);

                Assert.AreEqual(CatalogueImportWorkflowOutcome.CommitFailed, workflow.Outcome);
                Assert.AreEqual(0, workflow.Issues.Single().Worksheet.Length);
                Assert.AreEqual(0, workflow.Issues.Single().Row.Length);
                Assert.AreEqual(0, workflow.Issues.Single().Field.Length);
                Assert.AreEqual(workflow.Issues.Single().Message,
                    GetPrivateField<TextBlock>(dialog, "status").Text);
                Assert.IsFalse(GetPrivateField<Button>(dialog, "confirm").IsEnabled);
                Assert.AreEqual(shell.Localized["Close"], GetPrivateField<Button>(dialog, "cancel").Content?.ToString());
                Assert.AreEqual(1, store.CommitCount);
                Assert.IsFalse(GetPrivateField<TextBlock>(dialog, "status").Text.Contains(shell.Localized["CatalogueImportRefreshFailed"], StringComparison.Ordinal));
                dialog.Close();
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
    public void CatalogueImportModeDialogRendersLocalizedControlsOnSta()
    {
        RunOnSta(() =>
        {
            foreach (var cultureName in RenderCultures)
            {
                using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
                if (cultureName == "zh-CN")
                    shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == cultureName)).GetAwaiter().GetResult();

                var dialog = new CatalogueImportModeDialog(null!, shell.Localized) { ShowInTaskbar = false };
                Exception? callbackFailure = null;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        Assert.IsTrue(GetVisualDescendants<TextBlock>(dialog).Any(text => text.ActualWidth > 0 && text.ActualHeight > 0));
                        Assert.AreEqual(2, GetVisualDescendants<RadioButton>(dialog).Count());
                        foreach (var radio in GetVisualDescendants<RadioButton>(dialog))
                        {
                            Assert.IsGreaterThan(0d, radio.ActualWidth, cultureName);
                            Assert.IsGreaterThan(0d, radio.ActualHeight, cultureName);
                        }

                        Assert.AreEqual(shell.Localized["CatalogueImportUpdateMode"],
                            GetVisualDescendants<RadioButton>(dialog).Single(radio => radio.Content?.ToString() == shell.Localized["CatalogueImportUpdateMode"]).Content?.ToString());
                        Assert.AreEqual(shell.Localized["CatalogueImportAddOnlyMode"],
                            GetVisualDescendants<RadioButton>(dialog).Single(radio => radio.Content?.ToString() == shell.Localized["CatalogueImportAddOnlyMode"]).Content?.ToString());

                        var buttons = GetVisualDescendants<Button>(dialog).ToArray();
                        Assert.HasCount(2, buttons);
                        Assert.IsTrue(buttons.All(button => button.ActualWidth > 0 && button.ActualHeight > 0));
                        Assert.IsTrue(buttons.Any(button => button.Content?.ToString() == shell.Localized["Continue"]));
                        Assert.IsTrue(buttons.Any(button => button.Content?.ToString() == shell.Localized["Cancel"]));
                        AssertButtonsStayInsideParent(dialog, buttons);
                    }
                    catch (Exception exception)
                    {
                        callbackFailure = exception;
                    }
                    finally
                    {
                        if (dialog.IsVisible) dialog.Close();
                    }
                };
                dialog.ShowDialog();
                if (callbackFailure is not null)
                    ExceptionDispatchInfo.Capture(callbackFailure).Throw();
            }
        });
    }

    [TestMethod]
    public void CatalogueImportPreviewRendersDataSurfacesAndActionsAtAllSupportedSizes()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            RunOnSta(() =>
            {
                var applicationField = typeof(System.Windows.Application).GetField("_appInstance", BindingFlags.Static | BindingFlags.NonPublic);
                var previousApplication = applicationField?.GetValue(null);
                applicationField?.SetValue(null, null);
                var measured = RenderCultures.ToDictionary(culture => culture, _ => new List<(double Issues, double Affected)>());
                foreach (var cultureName in RenderCultures)
                foreach (var size in RenderSizes)
                {
                    using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
                    using var store = new FakeImportStore(CatalogueImportCommitResult.Success(changed: false));
                    var workflow = CreateWorkflow(new FixedImportGateway(RichWorkbook()), store, guard);
                    workflow.PreviewFromPathAsync(source, CatalogueImportMode.AddOnly).GetAwaiter().GetResult();
                    using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
                    if (cultureName == "zh-CN")
                        shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == cultureName)).GetAwaiter().GetResult();
                    workflow.ApplyLocalization(shell.Localized);

                    var dialog = CreatePreviewDialog(workflow, shell.Localized);
                    dialog.Width = size.Width;
                    dialog.Height = size.Height;
                    dialog.ShowInTaskbar = false;
                    Exception? callbackFailure = null;
                    dialog.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                    {
                        try
                        {
                            dialog.UpdateLayout();
                            var confirm = GetPrivateField<Button>(dialog, "confirm");
                            var cancel = GetPrivateField<Button>(dialog, "cancel");
                            Assert.IsGreaterThan(0d, confirm.ActualWidth, cultureName);
                            Assert.IsGreaterThan(0d, confirm.ActualHeight, cultureName);
                            Assert.IsGreaterThan(0d, cancel.ActualWidth, cultureName);
                            Assert.IsGreaterThan(0d, cancel.ActualHeight, cultureName);
                            AssertButtonsStayInsideParent(dialog, [confirm, cancel]);

                            var issues = GetPrivateField<DataGrid>(dialog, "issuesGrid");
                            var affected = GetPrivateField<DataGrid>(dialog, "affectedRowsGrid");
                            Assert.IsGreaterThan(0d, issues.ActualWidth, cultureName);
                            Assert.IsGreaterThan(0d, issues.ActualHeight, cultureName);
                            Assert.IsGreaterThan(0d, affected.ActualWidth, cultureName);
                            Assert.IsGreaterThan(0d, affected.ActualHeight, cultureName);
                            Assert.IsGreaterThan(0, issues.Items.Count);
                            Assert.IsGreaterThan(0, affected.Items.Count);
                            Assert.IsNotNull(GetVisualDescendants<ScrollViewer>(issues).FirstOrDefault(viewer => viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Auto), "Issues grid must own an internal vertical scrollbar.");
                            Assert.IsNotNull(GetVisualDescendants<ScrollViewer>(affected).FirstOrDefault(viewer => viewer.VerticalScrollBarVisibility == ScrollBarVisibility.Auto), "Affected rows grid must own an internal vertical scrollbar.");
                            AssertColumnHeaders(issues, shell.Localized, "Severity", "Worksheet", "Row", "Field", "Message");
                            AssertColumnHeaders(affected, shell.Localized, "Worksheet", "Row", "Entity", "Actions");
                            var messageColumn = issues.Columns.OfType<DataGridTextColumn>().Single(column => string.Equals(column.Header?.ToString(), shell.Localized["Message"], StringComparison.Ordinal));
                            Assert.IsNotNull(messageColumn.ElementStyle);
                            Assert.IsTrue(messageColumn.ElementStyle!.Setters.OfType<Setter>().Any(setter => setter.Property == TextBlock.TextWrappingProperty && Equals(setter.Value, TextWrapping.Wrap)));
                            var categories = GetVisualDescendants<ListBox>(dialog).Single();
                            Assert.IsGreaterThan(0, categories.Items.Count);
                            Assert.IsGreaterThan(0d, categories.ActualHeight, $"{cultureName} New Categories surface must remain rendered.");
                            measured[cultureName].Add((issues.ActualHeight, affected.ActualHeight));
                        }
                        catch (Exception exception)
                        {
                            callbackFailure = exception;
                        }
                        finally
                        {
                            if (dialog.IsVisible) dialog.Close();
                        }
                    }));
                    dialog.ShowDialog();
                    if (callbackFailure is not null)
                        ExceptionDispatchInfo.Capture(callbackFailure).Throw();
                }

                foreach (var cultureName in RenderCultures)
                {
                    if (measured[cultureName].Count == 0)
                        continue;
                    Assert.HasCount(RenderSizes.Length, measured[cultureName], cultureName);
                    var minimum = measured[cultureName][0];
                    var maximized = measured[cultureName][^1];
                    Assert.IsGreaterThan(minimum.Affected, maximized.Affected, $"{cultureName} affected rows must gain height when the window grows.");
                    Assert.IsGreaterThan(maximized.Issues - minimum.Issues, maximized.Affected - minimum.Affected,
                        $"{cultureName} affected rows must receive the primary share of extra vertical space.");
                }
                applicationField?.SetValue(null, previousApplication);
            });
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public async Task AffectedRowsAndPreviewBusinessStateSurviveFrenchToChineseRelocalization()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            using var store = new FakeImportStore(CatalogueImportCommitResult.Success(changed: false));
            var workflow = CreateWorkflow(new FixedImportGateway(RichWorkbook()), store, guard);
            await workflow.PreviewFromPathAsync(source, CatalogueImportMode.AddOnly);
            using var shell = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            workflow.ApplyLocalization(shell.Localized);
            var before = workflow.Preview!;
            var beforeRows = workflow.AffectedRows.ToArray();
            Assert.IsGreaterThanOrEqualTo(3, beforeRows.Length);
            CollectionAssert.Contains(beforeRows.Select(row => row.EntityType).ToArray(), shell.Localized["Product"]);
            CollectionAssert.Contains(beforeRows.Select(row => row.EntityType).ToArray(), shell.Localized["OptionGroup"]);
            CollectionAssert.Contains(beforeRows.Select(row => row.EntityType).ToArray(), shell.Localized["Option"]);
            StringAssert.Contains(beforeRows.Single(row => row.EntityType == shell.Localized["Product"]).Actions, shell.Localized["Create"]);
            StringAssert.Contains(beforeRows.Single(row => row.EntityType == shell.Localized["Product"]).Actions, $"{shell.Localized["Vat"]} 20%");
            var plan = before.Plan;
            var baseline = before.PreviewBaseline;
            var counts = (before.Preview.ProductCreateCount, before.Preview.OptionGroupCreateCount, before.Preview.OptionCreateCount, before.Preview.NewCategoryCount);

            await shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN"));
            workflow.ApplyLocalization(shell.Localized);
            var after = workflow.Preview!;
            var afterRows = workflow.AffectedRows.ToArray();
            Assert.AreSame(before, after);
            Assert.AreSame(plan, after.Plan);
            Assert.AreSame(baseline, after.PreviewBaseline);
            Assert.AreEqual(counts, (after.Preview.ProductCreateCount, after.Preview.OptionGroupCreateCount, after.Preview.OptionCreateCount, after.Preview.NewCategoryCount));
            CollectionAssert.AreEqual(beforeRows.Select(row => (row.Worksheet, row.ExcelRow)).ToArray(), afterRows.Select(row => (row.Worksheet, row.ExcelRow)).ToArray());
            Assert.IsTrue(afterRows.Any(row => row.EntityType == shell.Localized["Product"]));
            StringAssert.Contains(afterRows.Single(row => row.EntityType == shell.Localized["Product"]).Actions, $"{shell.Localized["Vat"]} 20%");
            Assert.IsTrue(afterRows.Any(row => row.EntityType == shell.Localized["OptionGroup"]));
            Assert.IsTrue(afterRows.Any(row => row.EntityType == shell.Localized["Option"]));
            Assert.IsFalse(afterRows.Any(row => row.EntityType is "Product" or "OptionGroup" or "Option" || row.Actions.Contains("Create", StringComparison.Ordinal) || row.Actions.Contains("Modify", StringComparison.Ordinal)));
        }
        finally { File.Delete(source); }
    }

    [TestMethod]
    public async Task AffectedRowsLocalizeProductGroupOptionCreateModifyActivateDeactivateOperations()
    {
        var source = Path.Combine(Path.GetTempPath(), $"sushi81-catalogue-{Guid.NewGuid():N}.xlsx");
        File.WriteAllText(source, "synthetic");
        try
        {
            var fixture = OperationsWorkbookFixture();
            using var guard = new WriteAuthorityGuard(WriteAuthorityState.Authoritative);
            using var store = new FakeImportStore(CatalogueImportCommitResult.Success(changed: false)) { Baseline = fixture.Baseline };
            var workflow = CreateWorkflow(new FixedImportGateway(fixture.Workbook), store, guard);
            await workflow.PreviewFromPathAsync(source, CatalogueImportMode.Update);
            var french = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            workflow.ApplyLocalization(french.Localized);
            if (workflow.Preview?.Plan is null)
                Assert.Fail(string.Join(" | ", workflow.Issues.Select(issue => $"{issue.Code}:{issue.Worksheet}:{issue.Row}:{issue.Field}:{issue.Message}")));
            Assert.IsNotNull(workflow.Preview?.Plan);
            var frenchRows = workflow.AffectedRows.ToArray();
            Assert.IsTrue(frenchRows.Any(row => row.EntityType == french.Localized["Product"] && row.Actions.Contains(french.Localized["Modify"], StringComparison.Ordinal)));
            Assert.IsTrue(frenchRows.Any(row => row.EntityType == french.Localized["Product"] && row.Actions.Contains(french.Localized["Activate"], StringComparison.Ordinal)));
            Assert.IsTrue(frenchRows.Any(row => row.EntityType == french.Localized["Product"] && row.Actions.Contains(french.Localized["Deactivate"], StringComparison.Ordinal)));
            Assert.IsTrue(frenchRows.Any(row => row.EntityType == french.Localized["OptionGroup"] && row.Actions.Contains(french.Localized["Modify"], StringComparison.Ordinal)));
            Assert.IsTrue(frenchRows.Any(row => row.EntityType == french.Localized["Option"] && row.Actions.Contains(french.Localized["Modify"], StringComparison.Ordinal)));
            Assert.IsTrue(frenchRows.Any(row => row.EntityType == french.Localized["Option"] && row.Actions.Contains(french.Localized["Activate"], StringComparison.Ordinal)));
            Assert.IsTrue(frenchRows.Any(row => row.EntityType == french.Localized["Option"] && row.Actions.Contains(french.Localized["Deactivate"], StringComparison.Ordinal)));

            using var chinese = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
            await chinese.ChangeLanguageAsync(chinese.Languages.Single(language => language.CultureName == "zh-CN"));
            workflow.ApplyLocalization(chinese.Localized);
            var chineseRows = workflow.AffectedRows.ToArray();
            Assert.IsTrue(chineseRows.Any(row => row.EntityType == chinese.Localized["Product"] && row.Actions.Contains(chinese.Localized["Modify"], StringComparison.Ordinal)));
            Assert.IsTrue(chineseRows.Any(row => row.EntityType == chinese.Localized["OptionGroup"] && row.Actions.Contains(chinese.Localized["Modify"], StringComparison.Ordinal)));
            Assert.IsFalse(chineseRows.Any(row => row.EntityType is "Product" or "OptionGroup" or "Option" || row.Actions.Contains("Create", StringComparison.Ordinal) || row.Actions.Contains("Modify", StringComparison.Ordinal) || row.Actions.Contains("Activate", StringComparison.Ordinal) || row.Actions.Contains("Deactivate", StringComparison.Ordinal)));
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

    private sealed class FixedImportGateway(CatalogueImportWorkbook workbook) : ICatalogueWorkbookImportGateway
    {
        public Task<CatalogueImportWorkbook> ReadAsync(Stream source, string? sourceName = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(workbook with { SourceName = sourceName ?? workbook.SourceName });
    }

    private static CatalogueImportWorkbook RichWorkbook() => new(
        CatalogueWorkbookSchema.ContractVersion,
        [new CatalogueImportProductRow(2, "P-1", "Produit", "Plats", "PL", Money.FromCents(100), 20m, true, false, true, null, null)],
        [new CatalogueImportOptionGroupRow(2, "P-1", "Produit", "Extras", "MULTI", false, 0, 2, 0, null, null, null, null)],
        [new CatalogueImportOptionRow(2, "P-1", "Produit", "Extras", "Sauce", Money.FromCents(25), true, 0, null, null, null, null, null)],
        [],
        [new CatalogueImportIssue(CatalogueImportIssueSeverity.Warning, "future-code", "synthetic raw warning")],
        "synthetic-catalogue.xlsx");

    private static (CatalogueImportWorkbook Workbook, CatalogueImportBaseline Baseline) OperationsWorkbookFixture()
    {
        var categoryId = Guid.NewGuid();
        var productOneId = Guid.NewGuid();
        var productTwoId = Guid.NewGuid();
        var groupOneId = Guid.NewGuid();
        var groupTwoId = Guid.NewGuid();
        var optionOneId = Guid.NewGuid();
        var optionTwoId = Guid.NewGuid();
        var category = new CatalogueImportCategory(categoryId, "Plats", null);
        var productOne = new CatalogueImportBaselineProduct(productOneId, "P-1", "Produit 1", categoryId, "Plats", null, Money.FromCents(100), 20m, true, false, true,
                [new CatalogueImportBaselineOptionGroup(groupOneId, productOneId, "Extras", Sushi81.Pos.Domain.SelectionMode.Multi, false, 0, 2, 0,
                [new CatalogueImportBaselineOption(optionOneId, groupOneId, "Sauce", Money.FromCents(10), true, 0)])]);
        var productTwo = new CatalogueImportBaselineProduct(productTwoId, "P-2", "Produit 2", categoryId, "Plats", null, Money.FromCents(200), 20m, false, false, true,
                [new CatalogueImportBaselineOptionGroup(groupTwoId, productTwoId, "Sauces", Sushi81.Pos.Domain.SelectionMode.Single, false, null, null, 0,
                [new CatalogueImportBaselineOption(optionTwoId, groupTwoId, "Ketchup", Money.FromCents(15), false, 0)])]);
        var baseline = new CatalogueImportBaseline([category], [productOne, productTwo]);
        var exportedOne = new CatalogueWorkbookProduct(productOneId, "P-1", "Produit 1", "Plats", null, Money.FromCents(100), 20m, true, false, true,
            [new CatalogueWorkbookOptionGroup(groupOneId, productOneId, "P-1", "Produit 1", "Extras", Sushi81.Pos.Domain.SelectionMode.Multi, false, 0, 2, 0,
                [new CatalogueWorkbookOption(optionOneId, groupOneId, "P-1", "Produit 1", "Extras", "Sauce", Money.FromCents(10), true, 0)])]);
        var exportedTwo = new CatalogueWorkbookProduct(productTwoId, "P-2", "Produit 2", "Plats", null, Money.FromCents(200), 20m, false, false, true,
            [new CatalogueWorkbookOptionGroup(groupTwoId, productTwoId, "P-2", "Produit 2", "Sauces", Sushi81.Pos.Domain.SelectionMode.Single, false, null, null, 0,
                [new CatalogueWorkbookOption(optionTwoId, groupTwoId, "P-2", "Produit 2", "Sauces", "Ketchup", Money.FromCents(15), false, 0)])]);
        var productOneKey = $"product:{productOneId:N}";
        var productTwoKey = $"product:{productTwoId:N}";
        var groupOneKey = $"group:{groupOneId:N}";
        var groupTwoKey = $"group:{groupTwoId:N}";
        var manifests = new CatalogueImportManifestEntry[]
        {
            new("Product", productOneKey, productOneId, null, "Products", 2, CatalogueWorkbookFingerprint.Product(exportedOne)),
            new("Product", productTwoKey, productTwoId, null, "Products", 3, CatalogueWorkbookFingerprint.Product(exportedTwo)),
            new("OptionGroup", groupOneKey, groupOneId, productOneKey, "OptionGroups", 2, CatalogueWorkbookFingerprint.OptionGroup(exportedOne.OptionGroups[0]), CatalogueWorkbookFingerprint.ParentProduct("P-1", "Produit 1")),
            new("OptionGroup", groupTwoKey, groupTwoId, productTwoKey, "OptionGroups", 3, CatalogueWorkbookFingerprint.OptionGroup(exportedTwo.OptionGroups[0]), CatalogueWorkbookFingerprint.ParentProduct("P-2", "Produit 2")),
            new("Option", $"option:{optionOneId:N}", optionOneId, groupOneKey, "Options", 2, CatalogueWorkbookFingerprint.Option(exportedOne.OptionGroups[0].Options[0]), CatalogueWorkbookFingerprint.ParentOptionGroup("P-1", "Produit 1", "Extras")),
            new("Option", $"option:{optionTwoId:N}", optionTwoId, groupTwoKey, "Options", 3, CatalogueWorkbookFingerprint.Option(exportedTwo.OptionGroups[0].Options[0]), CatalogueWorkbookFingerprint.ParentOptionGroup("P-2", "Produit 2", "Sauces")),
        };
        var workbook = new CatalogueImportWorkbook(
            CatalogueWorkbookSchema.ContractVersion,
            [
                new CatalogueImportProductRow(2, "P-1", "Produit 1 modifié", "Plats", null, Money.FromCents(125), 20m, false, false, true, productOneKey, productOneId.ToString("D")),
                new CatalogueImportProductRow(3, "P-2", "Produit 2", "Plats", null, Money.FromCents(200), 20m, true, false, true, productTwoKey, productTwoId.ToString("D")),
            ],
            [
                new CatalogueImportOptionGroupRow(2, "P-1", "Produit 1", "Extras", "MULTI", false, 0, 2, 1, productOneKey, groupOneKey, productOneId.ToString("D"), groupOneId.ToString("D")),
                new CatalogueImportOptionGroupRow(3, "P-2", "Produit 2", "Sauces", "SINGLE", false, null, null, 0, productTwoKey, groupTwoKey, productTwoId.ToString("D"), groupTwoId.ToString("D")),
            ],
            [
                new CatalogueImportOptionRow(2, "P-1", "Produit 1", "Extras", "Sauce modifiée", Money.FromCents(20), false, 0, productOneKey, groupOneKey, $"option:{optionOneId:N}", optionOneId.ToString("D"), groupOneId.ToString("D")),
                new CatalogueImportOptionRow(3, "P-2", "Produit 2", "Sauces", "Ketchup", Money.FromCents(15), true, 0, productTwoKey, groupTwoKey, $"option:{optionTwoId:N}", optionTwoId.ToString("D"), groupTwoId.ToString("D")),
            ], manifests, []);
        return (workbook, baseline);
    }

    private sealed class EmptyBaselineQueries : ICatalogueImportBaselineQueries
    {
        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogueImportBaseline.Empty);
    }

    private sealed class FakeImportStore(CatalogueImportCommitResult result) : ICatalogueImportStore, ICatalogueImportBaselineQueries, IDisposable
    {
        public int CommitCount { get; private set; }
        public int RefreshCount { get; set; }
        public CatalogueImportBaseline Baseline { get; set; } = CatalogueImportBaseline.Empty;

        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Baseline);

        public Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.FromResult(result);
        }

        public void Dispose() { }
    }

    private sealed class BlockingImportStore(CatalogueImportCommitResult result) : ICatalogueImportStore, IDisposable
    {
        public int CommitCount { get; private set; }
        public TaskCompletionSource<object?> CommitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> CommitFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogueImportBaseline.Empty);

        public async Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            CommitStarted.TrySetResult(null);
            await Release.Task.WaitAsync(cancellationToken);
            CommitFinished.TrySetResult(null);
            return result;
        }

        public void Dispose() { }
    }

    private sealed class ThrowingImportStore : ICatalogueImportStore, IDisposable
    {
        public int CommitCount { get; private set; }

        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CatalogueImportBaseline.Empty);

        public Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default) =>
            Throw();

        private Task<CatalogueImportCommitResult> Throw()
        {
            CommitCount++;
            throw new InvalidOperationException("synthetic commit failure");
        }

        public void Dispose() { }
    }

    private sealed class CatalogueRefreshStore : ICatalogueStore, IBusinessSettingsStore, IOrderStore, ICatalogueImportStore, IDisposable
    {
        private readonly CategorySummary category = new(Guid.NewGuid(), "Plats", "PL");
        private readonly ProductSummary product;
        private readonly TaskCompletionSource<object?> releaseRefresh = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int categoryReadCount;

        public CatalogueRefreshStore()
        {
            product = new ProductSummary(Guid.NewGuid(), "P-1", "Produit", category.Id, category.Name, Money.FromCents(100), 20m, true, false, true, category.ShortCode);
        }

        public bool ThrowOnRefresh { get; init; }
        public int CategoryReadCount => Volatile.Read(ref categoryReadCount);
        public int ProductReadCount { get; private set; }
        public int CommitCount { get; private set; }
        public TaskCompletionSource<object?> RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> ReleaseRefresh => releaseRefresh;

        public async Task<IReadOnlyList<CategorySummary>> ListCategoriesAsync(CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref categoryReadCount);
            if (count == 1)
            {
                RefreshStarted.TrySetResult(null);
                if (ThrowOnRefresh) throw new IOException("synthetic Catalogue refresh failure");
                await releaseRefresh.Task.WaitAsync(cancellationToken);
            }
            if (ThrowOnRefresh) throw new IOException("synthetic Catalogue refresh failure");
            return [category];
        }

        public Task<IReadOnlyList<ProductSummary>> ListProductsAsync(string? search = null, Guid? categoryId = null, bool? active = null, CancellationToken cancellationToken = default)
        {
            ProductReadCount++;
            if (ThrowOnRefresh) throw new IOException("synthetic Catalogue product refresh failure");
            return Task.FromResult<IReadOnlyList<ProductSummary>>([product]);
        }

        public Task<ProductDraft?> GetProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProductDraft?>(new(product.Id, product.Code, product.Name, product.CategoryId, product.PriceTtc, product.VatRate, product.IsActive, product.DiscountEligible, product.OptionsEnabled, []));

        public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid categoryId, string name, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> CreateCategoryWithCodeAsync(string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<CategorySummary>> RenameCategoryWithCodeAsync(Guid categoryId, string name, string? shortCode, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<CategorySummary>.Success(category));
        public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<Guid>.Success(product.Id));
        public Task<OperationResult> UpdateProductAsync(Guid productId, ProductDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult> SetProductActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());
        public Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<BulkProductActiveStateResult>.Success(new(request.Items.Count, 0)));
        public Task<OperationResult> DeleteProductAsync(Guid productId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());

        public Task<BusinessSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(BusinessSettings.Defaults(DateTimeOffset.UtcNow));
        public Task<OperationResult> UpdateAsync(BusinessSettings settings, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Success());

        public Task SaveAsync(OrderSnapshot snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<OrderSnapshot?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken = default) => Task.FromResult<OrderSnapshot?>(null);
        public Task<IReadOnlyList<OrderBrowserRow>> ListByPlannedDateAsync(DateOnly plannedDate, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OrderBrowserRow>>([]);

        public Task<CatalogueImportBaseline> ReadCatalogueImportBaselineAsync(CancellationToken cancellationToken = default) => Task.FromResult(CatalogueImportBaseline.Empty);

        public Task<CatalogueImportCommitResult> CommitAsync(CatalogueImportCommitRequest request, CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.FromResult(CatalogueImportCommitResult.Success(changed: true));
        }

        public void Dispose() { }
    }

    private static CatalogueWorkbookWorkflowViewModel CreateWorkflow(
        ICatalogueImportStore store,
        IWriteAuthorityGuard guard,
        Func<CancellationToken, Task>? refresh = null,
        Func<bool>? presentationBlocked = null)
        => CreateWorkflow(new EmptyImportGateway(), store, guard, refresh, presentationBlocked);

    private static CatalogueWorkbookWorkflowViewModel CreateWorkflow(
        ICatalogueWorkbookImportGateway gateway,
        ICatalogueImportStore store,
        IWriteAuthorityGuard guard,
        Func<CancellationToken, Task>? refresh = null,
        Func<bool>? presentationBlocked = null)
    {
        var importer = new CatalogueImportService(gateway, store as ICatalogueImportBaselineQueries ?? new EmptyBaselineQueries(), store, guard, new CountingNotifier());
        return new CatalogueWorkbookWorkflowViewModel(
            new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()),
            importer,
            guard,
            presentationRefreshBlocked: presentationBlocked,
            refreshAfterChangedImport: refresh);
    }

    private static ShellViewModel CreateProductionShell(
        CatalogueRefreshStore store,
        IWriteAuthorityGuard guard,
        CatalogueService catalogue,
        BusinessSettingsService settings,
        OrderEntryService entry)
    {
        var importer = new CatalogueImportService(
            new FixedImportGateway(RichWorkbook()), store, store, guard, new CountingNotifier());
        return new ShellViewModel(
            new InMemorySelectedCultureStore(),
            startupSucceeded: true,
            catalogueService: catalogue,
            settingsService: settings,
            orderEntryService: entry,
            authorityGuard: guard,
            catalogueWorkbookService: new CatalogueWorkbookService(new EmptySnapshotQueries(), new EmptyWorkbookGateway()),
            catalogueImportService: importer);
    }

    private static CatalogueImportPreviewDialog CreatePreviewDialog(
        CatalogueWorkbookWorkflowViewModel workflow,
        IReadOnlyDictionary<string, string> localized) =>
        new(null!, workflow, workflow.Preview!, localized) { ShowInTaskbar = false };

    private static void AssertButtonsStayInsideParent(Window window, IReadOnlyList<Button> buttons)
    {
        foreach (var button in buttons)
        {
            Assert.IsNotNull(VisualTreeHelper.GetParent(button));
            var parent = (FrameworkElement)VisualTreeHelper.GetParent(button)!;
            if (parent.ActualWidth <= 0d || parent.ActualHeight <= 0d) Assert.Fail("The button parent did not render.");
            var point = button.TransformToAncestor(parent).Transform(new Point(0, 0));
            if (point.X < -1d || point.Y < -1d) Assert.Fail("The button escaped its parent origin.");
            if (point.X + button.ActualWidth > parent.ActualWidth + 1d) Assert.Fail("The button is clipped by its parent width.");
            if (point.Y + button.ActualHeight > parent.ActualHeight + 1d) Assert.Fail("The button is clipped by its parent height.");
            var windowPoint = button.TransformToAncestor(window).Transform(new Point(0, 0));
            if (windowPoint.X < -1d || windowPoint.Y < -1d) Assert.Fail("The button escaped the window origin.");
            if (windowPoint.X + button.ActualWidth > window.ActualWidth + 1d) Assert.Fail("The button is clipped by the window width.");
            if (windowPoint.Y + button.ActualHeight > window.ActualHeight + 1d) Assert.Fail("The button is clipped by the window height.");
        }
    }

    private static void RaiseEscape(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        Assert.IsNotNull(source, "The real STA dialog must have a presentation source before key input.");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, Key.Escape)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        window.RaiseEvent(args);
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

    private static void AssertColumnHeaders(
        DataGrid grid,
        IReadOnlyDictionary<string, string> localized,
        params string[] keys)
    {
        Assert.HasCount(keys.Length, grid.Columns);
        for (var index = 0; index < keys.Length; index++)
            Assert.AreEqual(localized[keys[index]], grid.Columns[index].Header?.ToString(), keys[index]);
    }

    private static void AssertReachableThroughScroll(ScrollViewer scroll, FrameworkElement element, Window dialog)
    {
        var before = scroll.VerticalOffset;
        element.BringIntoView();
        PumpDispatcher(dialog);
        scroll.UpdateLayout();
        var after = scroll.VerticalOffset;
        var point = element.TransformToAncestor(scroll).Transform(new Point(0, 0));
        var bounds = new Rect(point, new Size(element.ActualWidth, element.ActualHeight));
        var viewport = new Rect(0, 0, scroll.ViewportWidth, scroll.ViewportHeight);
        Assert.IsTrue(Math.Abs(after - before) > 0.1d || bounds.IntersectsWith(viewport),
            "The preview surface could not be reached through the outer ScrollViewer.");
    }

    private static void PumpDispatcher(Window window) => window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void PumpUntil(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        Assert.IsTrue(task.IsCompleted, "The STA test operation did not complete within the bounded test window.");
    }


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
            try
            {
                ResetStaleWpfApplication();
                action();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void ResetStaleWpfApplication()
    {
        var current = System.Windows.Application.Current;
        if (current is null || !current.Dispatcher.HasShutdownFinished) return;
        var field = typeof(System.Windows.Application).GetField("_appInstance", BindingFlags.Static | BindingFlags.NonPublic);
        field?.SetValue(null, null);
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

    private sealed class NoOpPrintDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class DeterministicIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.NewGuid();
    }

    private sealed class FixedBusinessClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 18);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }
}
