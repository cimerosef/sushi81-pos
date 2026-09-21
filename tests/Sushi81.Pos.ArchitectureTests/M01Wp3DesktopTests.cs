using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Ids;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Authority;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M01Wp3DesktopTests
{
    [TestMethod]
    public async Task DefaultPreviewIsReadOnlyAndUsesAllApplicableDates()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);

        await fixture.Workflow.PreviewAsync();

        Assert.IsTrue(fixture.Workflow.HasPreview);
        Assert.AreEqual("All applicable dates", fixture.Workflow.InclusiveDateText);
        Assert.AreEqual(0, fixture.Store.PrepareCount);
        Assert.AreEqual(0, fixture.Store.SuccessCount);
        Assert.AreEqual(0, fixture.Workflow.ActionSummary.Sum(item => item.Count));
    }

    [TestMethod]
    public async Task InvalidInclusiveDateRangeIsVisibleAndDoesNotReadOrWrite()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        fixture.Workflow.StartDate = new DateTime(2026, 9, 21);
        fixture.Workflow.EndDate = new DateTime(2026, 9, 20);

        await fixture.Workflow.PreviewAsync();

        StringAssert.Contains(fixture.Workflow.ValidationMessage, "start date");
        Assert.AreEqual(0, fixture.Store.SelectionReadCount);
        Assert.AreEqual(0, fixture.Store.PrepareCount);
    }

    [TestMethod]
    public async Task InclusiveDateRangeForwardsExactBoundariesAndSelectsBothBoundariesOnly()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27100000-0000-0000-0000-000000000001"), new DateOnly(2026, 9, 19), "before"));
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27100000-0000-0000-0000-000000000002"), new DateOnly(2026, 9, 20), "start"));
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27100000-0000-0000-0000-000000000003"), new DateOnly(2026, 9, 21), "end"));
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27100000-0000-0000-0000-000000000004"), new DateOnly(2026, 9, 22), "after"));
        fixture.Workflow.StartDate = new DateTime(2026, 9, 20, 23, 59, 59);
        fixture.Workflow.EndDate = new DateTime(2026, 9, 21, 1, 2, 3);

        await fixture.Workflow.PreviewAsync();

        Assert.HasCount(2, fixture.Workflow.Preview!.Actions);
        CollectionAssert.Contains(fixture.Workflow.Preview.Actions.Select(action => action.OrderId.ToString("D")).ToArray(), "27100000-0000-0000-0000-000000000002");
        CollectionAssert.Contains(fixture.Workflow.Preview.Actions.Select(action => action.OrderId.ToString("D")).ToArray(), "27100000-0000-0000-0000-000000000003");
    }

    [TestMethod]
    public async Task PreviewSummarizesMixedCreateUpdateAndCancelActions()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        var create = ValidSource(Guid.Parse("27200000-0000-0000-0000-000000000001"), new DateOnly(2026, 9, 20), "create");
        var updateId = Guid.Parse("27200000-0000-0000-0000-000000000002");
        var update = ValidSource(updateId, new DateOnly(2026, 9, 20), "updated");
        var oldUpdate = ValidSource(updateId, new DateOnly(2026, 9, 20), "old");
        var cancelId = Guid.Parse("27200000-0000-0000-0000-000000000003");
        var cancelled = ValidSource(cancelId, new DateOnly(2026, 9, 20), "cancelled") with
        {
            Snapshot = ValidSource(cancelId, new DateOnly(2026, 9, 20), "cancelled").Snapshot with
            {
                Status = OrderStatus.Cancelled,
                CancelledAt = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero)
            }
        };
        fixture.Store.Sources.AddRange([create, update, cancelled]);
        fixture.Store.LatestEmissions.Add(EmissionFor(oldUpdate, ExportAction.Create));
        fixture.Store.LatestEmissions.Add(EmissionFor(ValidSource(cancelId, new DateOnly(2026, 9, 20), "cancelled"), ExportAction.Create));

        await fixture.Workflow.PreviewAsync();

        Assert.AreEqual(1, fixture.Workflow.ActionSummary.Single(item => item.Action == "CREATE").Count);
        Assert.AreEqual(1, fixture.Workflow.ActionSummary.Single(item => item.Action == "UPDATE").Count);
        Assert.AreEqual(1, fixture.Workflow.ActionSummary.Single(item => item.Action == "CANCEL").Count);
    }

    [TestMethod]
    public async Task ExportKeepsBlockingSelectionDistinctFromAnEmptySelection()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        fixture.Store.Sources.Add(BlockingSource());

        var path = Path.Combine(Path.GetTempPath(), $"sushi81-blocked-{Guid.NewGuid():N}.xlsx");
        try
        {
            var result = await fixture.Workflow.ExportAsync(path);

            Assert.IsNull(result);
            StringAssert.Contains(fixture.Workflow.StatusMessage, "blocked");
            Assert.HasCount(1, fixture.Workflow.Diagnostics);
            Assert.AreEqual("SETTLEMENT_DATE_UNAVAILABLE", fixture.Workflow.Diagnostics[0].Code);
            StringAssert.Contains(fixture.Workflow.Diagnostics[0].Message, "Settlement date");
            StringAssert.Contains(fixture.Workflow.StatusMessage, "Settlement date");
            Assert.AreEqual(0, fixture.Store.PrepareCount);
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public async Task AuthoritativeExportPreparesSucceedsRefreshesHistoryAndClearsPendingPreview()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27300000-0000-0000-0000-000000000001"), new DateOnly(2026, 9, 20), "new export"));
        var path = Path.Combine(Path.GetTempPath(), $"sushi81-success-{Guid.NewGuid():N}.xlsx");
        try
        {
            var result = await fixture.Workflow.ExportAsync(path);

            Assert.IsNotNull(result);
            Assert.AreEqual(1, fixture.Store.PrepareCount);
            Assert.AreEqual(1, fixture.Store.SuccessCount);
            Assert.HasCount(1, fixture.Workflow.History);
            Assert.IsFalse(fixture.Workflow.Preview!.IsBlocked);
            Assert.IsEmpty(fixture.Workflow.Preview.Actions);
            StringAssert.Contains(fixture.Workflow.StatusMessage, "Export succeeded");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [TestMethod]
    public async Task NonAuthoritativeExportDoesNotPrepareEmitOrCreateOutput()
    {
        using var fixture = CreateFixture(WriteAuthorityState.NonAuthoritativeReadOnly);
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27300000-0000-0000-0000-000000000002"), new DateOnly(2026, 9, 20), "read only"));
        var path = Path.Combine(Path.GetTempPath(), $"sushi81-readonly-{Guid.NewGuid():N}.xlsx");

        var result = await fixture.Workflow.ExportAsync(path);

        Assert.IsNull(result);
        Assert.AreEqual(0, fixture.Store.PrepareCount);
        Assert.AreEqual(0, fixture.Store.SuccessCount);
        Assert.IsEmpty(fixture.Store.LatestEmissions);
        Assert.IsFalse(File.Exists(path));
    }

    [TestMethod]
    public async Task ReentrantExportIsRejectedWhileTheFirstExportIsBusyAndRestoresCommandState()
    {
        var gateway = new BlockingWorkbookGateway();
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative, gateway);
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27300000-0000-0000-0000-000000000003"), new DateOnly(2026, 9, 20), "busy"));
        var firstPath = Path.Combine(Path.GetTempPath(), $"sushi81-busy-first-{Guid.NewGuid():N}.xlsx");
        var secondPath = Path.Combine(Path.GetTempPath(), $"sushi81-busy-second-{Guid.NewGuid():N}.xlsx");
        try
        {
            var first = fixture.Workflow.ExportAsync(firstPath);
            await gateway.WriteStarted.Task;
            Assert.IsTrue(fixture.Workflow.IsBusy);
            Assert.IsFalse(fixture.Workflow.CanExport);
            Assert.IsNull(await fixture.Workflow.ExportAsync(secondPath));
            Assert.AreEqual(1, fixture.Store.PrepareCount);
            gateway.Release.TrySetResult(null);
            Assert.IsNotNull(await first);
            Assert.IsFalse(fixture.Workflow.IsBusy);
            Assert.IsTrue(fixture.Workflow.CanExport);
        }
        finally
        {
            if (File.Exists(firstPath)) File.Delete(firstPath);
            if (File.Exists(secondPath)) File.Delete(secondPath);
        }
    }

    [TestMethod]
    public async Task SuccessfulBatchCanBeRegeneratedReadOnlyWithoutNewLedgerMutation()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27300000-0000-0000-0000-000000000004"), new DateOnly(2026, 9, 20), "regenerate"));
        var firstPath = Path.Combine(Path.GetTempPath(), $"sushi81-regenerate-source-{Guid.NewGuid():N}.xlsx");
        var secondPath = Path.Combine(Path.GetTempPath(), $"sushi81-regenerate-copy-{Guid.NewGuid():N}.xlsx");
        try
        {
            Assert.IsNotNull(await fixture.Workflow.ExportAsync(firstPath));
            var batchId = fixture.Workflow.SelectedHistory!.BatchId;
            var prepareCount = fixture.Store.PrepareCount;
            var successCount = fixture.Store.SuccessCount;
            var emissionCount = fixture.Store.LatestEmissions.Count;
            fixture.Guard.SetState(WriteAuthorityState.NonAuthoritativeReadOnly);
            fixture.Workflow.RefreshAuthorityState();

            var result = await fixture.Workflow.RegenerateAsync(secondPath);

            Assert.IsNotNull(result);
            Assert.AreEqual(batchId, result!.BatchId);
            Assert.AreEqual(prepareCount, fixture.Store.PrepareCount);
            Assert.AreEqual(successCount, fixture.Store.SuccessCount);
            Assert.HasCount(emissionCount, fixture.Store.LatestEmissions);
            Assert.IsFalse(fixture.Workflow.CanExport);
            Assert.IsTrue(fixture.Workflow.CanRegenerate);
        }
        finally
        {
            if (File.Exists(firstPath)) File.Delete(firstPath);
            if (File.Exists(secondPath)) File.Delete(secondPath);
        }
    }

    [TestMethod]
    public async Task OrdinaryExportFailureRestoresBusyStateAndLeavesRetryablePreparedBatch()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative, new ThrowingWorkbookGateway());
        fixture.Store.Sources.Add(ValidSource(Guid.Parse("27300000-0000-0000-0000-000000000005"), new DateOnly(2026, 9, 20), "failure"));
        var path = Path.Combine(Path.GetTempPath(), $"sushi81-failure-{Guid.NewGuid():N}.xlsx");

        Assert.IsNull(await fixture.Workflow.ExportAsync(path));

        Assert.IsFalse(fixture.Workflow.IsBusy);
        Assert.IsTrue(fixture.Workflow.CanExport);
        Assert.IsFalse(string.IsNullOrWhiteSpace(fixture.Workflow.FailureMessage));
        Assert.AreEqual(1, fixture.Store.PrepareCount);
        Assert.HasCount(1, fixture.Store.Prepared);
        if (File.Exists(path)) File.Delete(path);
    }

    [TestMethod]
    public async Task PendingPreparedBatchIsVisibleReadOnlyAndRetryableWithoutCreatingAnotherBatch()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        var batch = PreparedBatch(Guid.Parse("26000000-0000-0000-0000-000000000001"));
        fixture.Store.Prepared.Add(batch);

        await fixture.Workflow.RefreshHistoryAsync();

        Assert.HasCount(1, fixture.Workflow.PendingBatches);
        Assert.AreEqual(batch.Payload.Meta.BatchId, fixture.Workflow.SelectedPreparedBatch!.BatchId);
        Assert.IsTrue(fixture.Workflow.CanRetryPrepared);

        var path = Path.Combine(Path.GetTempPath(), $"sushi81-retry-{Guid.NewGuid():N}.xlsx");
        try
        {
            var result = await fixture.Workflow.RetryPreparedAsync(path);

            Assert.IsNotNull(result);
            Assert.AreEqual(batch.Payload.Meta.BatchId, result!.BatchId);
            Assert.AreEqual(0, fixture.Store.PrepareCount);
            Assert.AreEqual(1, fixture.Store.SuccessCount);
            Assert.IsEmpty(fixture.Store.Prepared);
            Assert.HasCount(1, fixture.Store.Successful);
            Assert.IsEmpty(fixture.Workflow.PendingBatches);
            Assert.IsFalse(fixture.Workflow.HasPreview && fixture.Workflow.Preview!.IsBlocked);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [TestMethod]
    public async Task LiveDatabaseReplacementClearsOldPreviewAndReloadsPendingState()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        var oldBatch = PreparedBatch(Guid.Parse("26000000-0000-0000-0000-000000000002"));
        var newBatch = PreparedBatch(Guid.Parse("26000000-0000-0000-0000-000000000003"));
        fixture.Store.Prepared.Add(oldBatch);

        await fixture.Workflow.PreviewAsync();
        await fixture.Workflow.RefreshHistoryAsync();
        Assert.IsTrue(fixture.Workflow.HasPreview);
        Assert.AreEqual(oldBatch.Payload.Meta.BatchId, fixture.Workflow.SelectedPreparedBatch!.BatchId);

        fixture.Store.Prepared.Clear();
        fixture.Store.Prepared.Add(newBatch);
        await fixture.Workflow.RefreshAfterLiveDatabaseReplacementAsync();

        Assert.IsFalse(fixture.Workflow.HasPreview);
        Assert.IsEmpty(fixture.Workflow.History);
        Assert.HasCount(1, fixture.Workflow.PendingBatches);
        Assert.AreEqual(newBatch.Payload.Meta.BatchId, fixture.Workflow.SelectedPreparedBatch!.BatchId);
    }

    [TestMethod]
    public async Task FailedPreparedRetryLeavesTheOriginalBatchAvailableForRetry()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative, new ThrowingWorkbookGateway());
        fixture.Store.Prepared.Add(PreparedBatch(Guid.Parse("26000000-0000-0000-0000-000000000004")));
        await fixture.Workflow.RefreshHistoryAsync();
        var batchId = fixture.Workflow.SelectedPreparedBatch!.BatchId;

        var path = Path.Combine(Path.GetTempPath(), $"sushi81-retry-failure-{Guid.NewGuid():N}.xlsx");
        var result = await fixture.Workflow.RetryPreparedAsync(path);

        Assert.IsNull(result);
        Assert.AreEqual(0, fixture.Store.PrepareCount);
        Assert.AreEqual(0, fixture.Store.SuccessCount);
        Assert.HasCount(1, fixture.Store.Prepared);
        Assert.AreEqual(batchId, fixture.Workflow.SelectedPreparedBatch!.BatchId);
        Assert.IsTrue(fixture.Workflow.CanRetryPrepared);
        StringAssert.Contains(fixture.Workflow.StatusMessage, "remains available");
        if (File.Exists(path)) File.Delete(path);
    }

    [TestMethod]
    public void AuthorityTextRaisesOnLocalizationRefresh()
    {
        using var fixture = CreateFixture(WriteAuthorityState.NonAuthoritativeReadOnly);
        var changed = new List<string?>();
        fixture.Workflow.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        fixture.Workflow.ApplyLocalization(new Dictionary<string, string>
        {
            ["GestionExportReadOnly"] = "只读设备：无法生成新的导出。"
        });

        StringAssert.Contains(fixture.Workflow.AuthorityStatusText, "只读");
        CollectionAssert.Contains(changed, nameof(fixture.Workflow.AuthorityStatusText));
    }

    [TestMethod]
    public async Task BlockingSettlementDiagnosticUsesLocalizedDesktopTextInFrenchAndChinese()
    {
        using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
        fixture.Store.Sources.Add(BlockingSource());

        await fixture.Workflow.PreviewAsync();
        StringAssert.Contains(fixture.Workflow.Diagnostics.Single().Message, "Settlement date");

        fixture.Workflow.ApplyLocalization(new Dictionary<string, string>
        {
            ["GestionExportDiagnosticSettlementDateUnavailable"] = "无法根据已记录的付款确定结算日期。",
            ["GestionExportBlockedAtExport"] = "导出被阻止：{0}"
        });
        StringAssert.Contains(fixture.Workflow.Diagnostics.Single().Message, "结算日期");

        var path = Path.Combine(Path.GetTempPath(), $"sushi81-localized-blocked-{Guid.NewGuid():N}.xlsx");
        try
        {
            await fixture.Workflow.ExportAsync(path);
            StringAssert.Contains(fixture.Workflow.StatusMessage, "结算日期");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [TestMethod]
    public void GestionExportSurfaceConstructsAndBindsOnSta()
    {
        RunOnSta(() =>
        {
            using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
            var dialogs = new CancelFileDialogs();
            var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(),
                startupSucceeded: true,
                authorityGuard: fixture.Guard,
                gestionExportWorkflow: fixture.Workflow);
            var window = new MainWindow(shell, fileDialogs: dialogs)
            {
                ShowInTaskbar = false,
                Width = 980,
                Height = 700
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                var gestion = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["GestionExport"]);
                gestion.IsSelected = true;
                window.UpdateLayout();

                var datePickers = VisualDescendants<DatePicker>(window)
                    .Where(picker => picker.GetBindingExpression(DatePicker.SelectedDateProperty)?.ParentBinding.Path.Path is "StartDate" or "EndDate")
                    .ToArray();
                Assert.HasCount(2, datePickers);
                Assert.AreEqual("StartDate", datePickers[0].GetBindingExpression(DatePicker.SelectedDateProperty)!.ParentBinding.Path.Path);
                Assert.AreEqual("EndDate", datePickers[1].GetBindingExpression(DatePicker.SelectedDateProperty)!.ParentBinding.Path.Path);
                var grids = VisualDescendants<DataGrid>(window)
                    .Where(grid => grid.GetBindingExpression(ItemsControl.ItemsSourceProperty)?.ParentBinding.Path.Path is "History" or "PendingBatches")
                    .ToArray();
                Assert.HasCount(2, grids);
                Assert.AreEqual("History", grids[0].GetBindingExpression(ItemsControl.ItemsSourceProperty)!.ParentBinding.Path.Path);
                Assert.AreEqual("PendingBatches", grids[1].GetBindingExpression(ItemsControl.ItemsSourceProperty)!.ParentBinding.Path.Path);

                var export = VisualDescendants<Button>(window).Single(button => button.Content?.ToString() == shell.Localized["GestionExportExport"]);
                Assert.IsTrue(export.IsEnabled);
                export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(1, dialogs.SaveCalls);
                Assert.AreEqual(0, fixture.Store.PrepareCount);
                Assert.AreEqual(0, fixture.Store.SuccessCount);
            }
            finally
            {
                window.Close();
                shell.Dispose();
            }
        });
    }

    [TestMethod]
    public void GestionExportDatePickersFollowFrenchAndChineseUiCultureInSession()
    {
        RunOnSta(() =>
        {
            using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
            using var shell = new ShellViewModel(
                new InMemorySelectedCultureStore(),
                startupSucceeded: true,
                authorityGuard: fixture.Guard,
                gestionExportWorkflow: fixture.Workflow);
            var window = new MainWindow(shell) { ShowInTaskbar = false, Width = 980, Height = 700 };

            try
            {
                window.Show();
                window.UpdateLayout();
                var gestion = VisualDescendants<TabItem>(window).Single(item => item.Header?.ToString() == shell.Localized["GestionExport"]);
                gestion.IsSelected = true;
                window.UpdateLayout();
                var datePickers = VisualDescendants<DatePicker>(window)
                    .Where(picker => picker.GetBindingExpression(DatePicker.SelectedDateProperty)?.ParentBinding.Path.Path is "StartDate" or "EndDate")
                    .ToArray();
                Assert.HasCount(2, datePickers);
                Assert.IsTrue(datePickers.All(picker => string.Equals(picker.Language.IetfLanguageTag, "fr-FR", StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual("Gestion export", shell.Localized["GestionExport"]);

                var languageSelector = VisualDescendants<ComboBox>(window).Single(combo => combo.SelectedValuePath == "CultureName");
                languageSelector.SelectedValue = "zh-CN";
                window.Dispatcher.Invoke(() => { });
                window.UpdateLayout();
                Assert.IsTrue(datePickers.All(picker => string.Equals(picker.Language.IetfLanguageTag, "zh-CN", StringComparison.OrdinalIgnoreCase)));
                Assert.AreEqual("销售数据导出", shell.Localized["GestionExport"]);
                Assert.AreEqual("销售数据导出", shell.Localized["GestionExportSection"]);

                languageSelector.SelectedValue = "fr-FR";
                window.Dispatcher.Invoke(() => { });
                window.UpdateLayout();
                Assert.IsTrue(datePickers.All(picker => string.Equals(picker.Language.IetfLanguageTag, "fr-FR", StringComparison.OrdinalIgnoreCase)));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void NonAuthoritativeStateBlocksNewExportButKeepsHistorySurfaceReadable()
    {
        using var fixture = CreateFixture(WriteAuthorityState.NonAuthoritativeReadOnly);

        Assert.IsFalse(fixture.Workflow.IsAuthoritative);
        Assert.IsFalse(fixture.Workflow.CanExport);
        Assert.IsTrue(fixture.Workflow.CanPreview);
        StringAssert.Contains(fixture.Workflow.AuthorityStatusText, "Read-only");
    }

    [TestMethod]
    public void DesktopCompositionAndSurfaceContainOnlyTheAuthorizedWp3Workflow()
    {
        var xaml = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
        var composition = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "CompositionRoot.cs"));
        var codeBehind = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "MainWindow.xaml.cs"));

        StringAssert.Contains(xaml, "GestionExportWorkflow");
        StringAssert.Contains(xaml, "GestionExportPreview");
        StringAssert.Contains(xaml, "GestionExportRegenerate");
        StringAssert.Contains(xaml, "ActionSummary");
        Assert.IsFalse(xaml.Contains("ProductCode", StringComparison.Ordinal));
        StringAssert.Contains(composition, "SqliteGestionExportStore");
        StringAssert.Contains(composition, "GestionExportWorkbookService");
        StringAssert.Contains(codeBehind, "OnPreviewGestionExport");
        StringAssert.Contains(codeBehind, "OnRegenerateGestionExport");
    }

    [TestMethod]
    public async Task FrenchAndChineseExportConceptsAreCompleteAndSwitchable()
    {
        var french = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        var chinese = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        await chinese.ChangeLanguageAsync(chinese.Languages.Single(language => language.CultureName == "zh-CN"));

        foreach (var key in new[]
        {
            "GestionExport", "GestionExportInclusiveHint", "GestionExportPreview", "GestionExportExport",
            "GestionExportHistory", "GestionExportRegenerate", "GestionExportAuthority", "GestionExportReadOnly",
            "GestionExportBusy", "GestionExportRetry", "GestionExportScope", "GestionExportPending",
            "GestionExportRetryPrepared", "GestionExportInvalidRange",
            "GestionExportNoPending", "GestionExportBlockedAtExport", "GestionExportDiagnosticSettlementDateUnavailable"
        })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(french.Localized[key]), $"French resource missing: {key}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(chinese.Localized[key]), $"Chinese resource missing: {key}");
            Assert.AreNotEqual(french.Localized[key], chinese.Localized[key], $"Language resources unexpectedly match: {key}");
        }
    }

    private static Fixture CreateFixture(WriteAuthorityState state, IExportWorkbookGateway? workbookGateway = null)
    {
        var guard = new WriteAuthorityGuard(state);
        var store = new FakeStore();
        var clock = new FixedClock();
        var applicationService = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), new FixedIdGenerator());
        var workbookService = new GestionExportWorkbookService(applicationService, store, workbookGateway ?? new EmptyWorkbookGateway(), clock, guard);
        var workflow = new GestionExportWorkflowViewModel(workbookService, store, guard, "test", new Dictionary<string, string>
        {
            ["GestionExportAllDates"] = "All applicable dates",
            ["GestionExportSelectionSummary"] = "{0} action(s): {1} CREATE, {2} UPDATE, {3} CANCEL.",
            ["GestionExportBlocking"] = "Blocking",
            ["GestionExportDiagnosticSettlementDateUnavailable"] = "Settlement date is unavailable for this order.",
            ["GestionExportCreate"] = "CREATE",
            ["GestionExportUpdate"] = "UPDATE",
            ["GestionExportCancel"] = "CANCEL",
            ["GestionExportInvalidRange"] = "The start date cannot be after the end date.",
            ["GestionExportReadOnly"] = "Read-only authority: new export is unavailable; successful history can still be regenerated safely.",
            ["GestionExportBlockedAtExport"] = "Export blocked: {0}",
            ["GestionExportPreparedRetryBusy"] = "Retrying the pending export batch…",
            ["GestionExportPreparedRetrySucceeded"] = "Pending batch completed: {0}.",
            ["GestionExportPreparedRetryFailure"] = "The pending batch could not be completed; it remains available for retry."
        }, preparedBatchReader: store);
        return new Fixture(guard, store, workflow);
    }

    private static string LocateRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sushi81.Pos.sln")))
            directory = directory.Parent;
        Assert.IsNotNull(directory);
        return Path.Combine([directory!.FullName, .. parts]);
    }

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        var children = 0;
        try { children = VisualTreeHelper.GetChildrenCount(root); }
        catch (InvalidOperationException) { yield break; }
        for (var index = 0; index < children; index++)
            foreach (var descendant in VisualDescendants<T>(VisualTreeHelper.GetChild(root, index))) yield return descendant;
    }

    private sealed record Fixture(WriteAuthorityGuard Guard, FakeStore Store, GestionExportWorkflowViewModel Workflow) : IDisposable
    {
        public void Dispose() => Guard.Dispose();
    }

    private sealed class CancelFileDialogs : ICatalogueWorkbookFileDialogs
    {
        public int SaveCalls { get; private set; }

        public string? ShowSave(object owner, string suggestedFileName, string? filter = null)
        {
            SaveCalls++;
            return null;
        }

        public string? ShowOpen(object owner, string? filter = null) => null;
    }

    private sealed class FakeStore : IExportOrderSourceReader, IExportLedgerStore, IExportBatchHistoryReader, IExportPreparedBatchReader
    {
        public int SelectionReadCount { get; private set; }
        public int PrepareCount { get; private set; }
        public int SuccessCount { get; private set; }
        public List<ExportEmissionRecord> LatestEmissions { get; } = [];
        public List<ExportOrderSourceRecord> Sources { get; } = [];
        public List<ExportBatchRecord> Prepared { get; } = [];
        public List<ExportBatchRecord> Successful { get; } = [];

        public Task<IReadOnlyList<ExportOrderSourceRecord>> ListExportOrderSourcesAsync(CancellationToken cancellationToken = default)
        {
            SelectionReadCount++;
            return Task.FromResult<IReadOnlyList<ExportOrderSourceRecord>>(Sources);
        }

        public Task<IReadOnlyList<ExportEmissionRecord>> ListLatestSuccessfulEmissionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportEmissionRecord>>(LatestEmissions);

        public Task<ExportBatchRecord?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExportBatchRecord?>(Prepared.Concat(Successful).SingleOrDefault(batch => batch.Payload.Meta.BatchId == batchId));

        public Task PrepareBatchAsync(ExportBatchRecord batch, CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            Prepared.Add(batch);
            return Task.CompletedTask;
        }

        public Task MarkBatchSucceededAsync(Guid batchId, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
        {
            SuccessCount++;
            var batch = Prepared.Single(batch => batch.Payload.Meta.BatchId == batchId);
            Prepared.Remove(batch);
            Successful.Add(batch with { Status = ExportBatchStatus.Success, CompletedAtUtc = completedAtUtc });
            LatestEmissions.AddRange(batch.Payload.Orders.Select(order =>
            {
                var positive = order with { Action = ExportAction.Create };
                return new ExportEmissionRecord(
                    batchId,
                    order.OrderId,
                    order.Action,
                    ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(positive)),
                    positive,
                    order.FulfilmentDate,
                    order.SettlementDate,
                    completedAtUtc);
            }));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExportBatchRecord>> ListSuccessfulBatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportBatchRecord>>(Successful);

        public Task<IReadOnlyList<ExportBatchRecord>> ListPreparedBatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportBatchRecord>>(Prepared);
    }

    private sealed class EmptyWorkbookGateway : IExportWorkbookGateway
    {
        public Task WriteAsync(ExportBatchPayload payload, Stream destination, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ValidateAsync(Stream source, ExportBatchPayload expectedPayload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 20);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class FixedIdGenerator : IIdGenerator
    {
        public Guid NewId() => Guid.Parse("25000000-0000-0000-0000-000000000001");
    }

    private sealed class ThrowingWorkbookGateway : IExportWorkbookGateway
    {
        public Task WriteAsync(ExportBatchPayload payload, Stream destination, CancellationToken cancellationToken = default) =>
            throw new IOException("synthetic workbook generation failure");

        public Task ValidateAsync(Stream source, ExportBatchPayload expectedPayload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlockingWorkbookGateway : IExportWorkbookGateway
    {
        public TaskCompletionSource<object?> WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<object?> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task WriteAsync(ExportBatchPayload payload, Stream destination, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult(null);
            await Release.Task.WaitAsync(cancellationToken);
        }

        public Task ValidateAsync(Stream source, ExportBatchPayload expectedPayload, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static ExportBatchRecord PreparedBatch(Guid batchId)
    {
        var payload = new ExportBatchPayload(
            new ExportBatchMeta("1.0", batchId, new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero), "test", null, null, 0, 0, 0),
            []);
        return new ExportBatchRecord(
            payload,
            ExportBatchStatus.Prepared,
            ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializeBatch(payload)));
    }

    private static ExportOrderSourceRecord ValidSource(Guid id, DateOnly fulfilmentDate, string comment)
    {
        var template = BlockingSource();
        var snapshot = template.Snapshot with
        {
            Id = id,
            CreatedAt = new DateTimeOffset(fulfilmentDate.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero),
            ClosedAt = new DateTimeOffset(fulfilmentDate.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero),
            PlannedFulfilmentDate = fulfilmentDate,
            Comment = comment,
            Status = OrderStatus.Closed,
            CancelledAt = null
        };
        var adjustment = new PaymentAdjustment(
            Guid.NewGuid(),
            id,
            PaymentBucket.Card,
            Money.FromCents(1000),
            new DateTimeOffset(fulfilmentDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(fulfilmentDate.ToDateTime(new TimeOnly(12, 1)), TimeSpan.Zero));
        return new ExportOrderSourceRecord(snapshot, [adjustment]);
    }

    private static ExportEmissionRecord EmissionFor(ExportOrderSourceRecord source, ExportAction action)
    {
        Assert.IsTrue(ExportSelectionRules.TryBuildPositivePayload(source, TimeZoneInfo.Utc, out var positive, out var diagnostic), diagnostic?.Message);
        return new ExportEmissionRecord(
            Guid.Parse("27400000-0000-0000-0000-000000000001"),
            source.Snapshot.Id,
            action,
            ExportPayloadSerializer.ComputeSha256(ExportPayloadSerializer.SerializePositive(positive)),
            positive,
            positive.FulfilmentDate,
            positive.SettlementDate,
            new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero));
    }

    private static ExportOrderSourceRecord BlockingSource()
    {
        var id = Guid.Parse("27000000-0000-0000-0000-000000000001");
        var now = new DateTimeOffset(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);
        var snapshot = new OrderSnapshot(
            id,
            OrderSourceType.Pos,
            OrderStatus.Closed,
            now,
            now,
            now,
            null,
            FulfilmentMode.Retrait,
            new DateOnly(2026, 9, 20),
            new TimeOnly(12, 0),
            false,
            null,
            null,
            "blocked",
            Money.FromCents(1000),
            false,
            false,
            null,
            Money.Zero,
            [new(Guid.Parse("27000000-0000-0000-0000-000000000002"), 0, Guid.NewGuid(), "P-1", "Produit", "Plats", Money.FromCents(1000), 10m, true, 1, Money.FromCents(1000), Money.FromCents(1000), [])],
            [new(10m, Money.FromCents(1000), Money.Zero, Guid.Parse("27000000-0000-0000-0000-000000000003"))])
        {
            CardPaymentTtc = Money.FromCents(1000),
            CashPaymentTtc = Money.Zero
        };
        return new ExportOrderSourceRecord(snapshot, []);
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
}
