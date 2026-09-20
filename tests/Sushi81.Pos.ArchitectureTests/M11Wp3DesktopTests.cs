using System.Globalization;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
public sealed class M11Wp3DesktopTests
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
            Assert.AreEqual(0, fixture.Store.PrepareCount);
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
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

        var path = Path.Combine(Path.GetTempPath(), $"sushi81-retry-failure-{Guid.NewGuid():N}.xlsx");
        var result = await fixture.Workflow.RetryPreparedAsync(path);

        Assert.IsNull(result);
        Assert.HasCount(1, fixture.Store.Prepared);
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
    public void GestionExportSurfaceConstructsAndBindsOnSta()
    {
        RunOnSta(() =>
        {
            using var fixture = CreateFixture(WriteAuthorityState.Authoritative);
            var xaml = File.ReadAllText(LocateRepositoryFile("src", "Sushi81.Pos.Desktop", "MainWindow.xaml"));
            var surface = new Grid();
            var history = new DataGrid();
            history.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(fixture.Workflow.History))
            {
                Source = fixture.Workflow
            });
            var dateFilter = new DatePicker();
            dateFilter.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(fixture.Workflow.StartDate))
            {
                Source = fixture.Workflow
            });
            surface.Children.Add(history);
            surface.Children.Add(dateFilter);

            Assert.AreEqual(2, surface.Children.Count);
            Assert.IsNotNull(history.GetBindingExpression(ItemsControl.ItemsSourceProperty));
            Assert.IsNotNull(dateFilter.GetBindingExpression(DatePicker.SelectedDateProperty));
            StringAssert.Contains(xaml, "GestionExportPending");
            StringAssert.Contains(xaml, "SelectedPreparedBatch");
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
            "GestionExportNoPending", "GestionExportBlockedAtExport"
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

    private sealed record Fixture(WriteAuthorityGuard Guard, FakeStore Store, GestionExportWorkflowViewModel Workflow) : IDisposable
    {
        public void Dispose() => Guard.Dispose();
    }

    private sealed class FakeStore : IExportOrderSourceReader, IExportLedgerStore, IExportBatchHistoryReader, IExportPreparedBatchReader
    {
        public int SelectionReadCount { get; private set; }
        public int PrepareCount { get; private set; }
        public int SuccessCount { get; private set; }
        public List<ExportOrderSourceRecord> Sources { get; } = [];
        public List<ExportBatchRecord> Prepared { get; } = [];
        public List<ExportBatchRecord> Successful { get; } = [];

        public Task<IReadOnlyList<ExportOrderSourceRecord>> ListExportOrderSourcesAsync(CancellationToken cancellationToken = default)
        {
            SelectionReadCount++;
            return Task.FromResult<IReadOnlyList<ExportOrderSourceRecord>>(Sources);
        }

        public Task<IReadOnlyList<ExportEmissionRecord>> ListLatestSuccessfulEmissionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportEmissionRecord>>([]);

        public Task<ExportBatchRecord?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ExportBatchRecord?>(Prepared.Concat(Successful).SingleOrDefault(batch => batch.Payload.Meta.BatchId == batchId));

        public Task PrepareBatchAsync(ExportBatchRecord batch, CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            return Task.CompletedTask;
        }

        public Task MarkBatchSucceededAsync(Guid batchId, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
        {
            SuccessCount++;
            var batch = Prepared.Single(batch => batch.Payload.Meta.BatchId == batchId);
            Prepared.Remove(batch);
            Successful.Add(batch with { Status = ExportBatchStatus.Success, CompletedAtUtc = completedAtUtc });
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
