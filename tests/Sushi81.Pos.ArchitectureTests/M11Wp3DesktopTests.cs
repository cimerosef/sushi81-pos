using System.Globalization;
using System.IO;
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
            "GestionExportBusy", "GestionExportRetry", "GestionExportInvalidRange", "GestionExportNoPending"
        })
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(french.Localized[key]), $"French resource missing: {key}");
            Assert.IsFalse(string.IsNullOrWhiteSpace(chinese.Localized[key]), $"Chinese resource missing: {key}");
            Assert.AreNotEqual(french.Localized[key], chinese.Localized[key], $"Language resources unexpectedly match: {key}");
        }
    }

    private static Fixture CreateFixture(WriteAuthorityState state)
    {
        var guard = new WriteAuthorityGuard(state);
        var store = new FakeStore();
        var clock = new FixedClock();
        var applicationService = new GestionExportService(store, store, clock, guard, new NoOpDurableChangeNotifier(), new FixedIdGenerator());
        var workbookService = new GestionExportWorkbookService(applicationService, store, new EmptyWorkbookGateway(), clock, guard);
        var workflow = new GestionExportWorkflowViewModel(workbookService, store, guard, "test", new Dictionary<string, string>
        {
            ["GestionExportAllDates"] = "All applicable dates",
            ["GestionExportSelectionSummary"] = "{0} action(s): {1} CREATE, {2} UPDATE, {3} CANCEL.",
            ["GestionExportBlocking"] = "Blocking",
            ["GestionExportCreate"] = "CREATE",
            ["GestionExportUpdate"] = "UPDATE",
            ["GestionExportCancel"] = "CANCEL",
            ["GestionExportInvalidRange"] = "The start date cannot be after the end date.",
            ["GestionExportReadOnly"] = "Read-only authority: new export is unavailable; successful history can still be regenerated safely."
        });
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

    private sealed class FakeStore : IExportOrderSourceReader, IExportLedgerStore, IExportBatchHistoryReader
    {
        public int SelectionReadCount { get; private set; }
        public int PrepareCount { get; private set; }
        public int SuccessCount { get; private set; }

        public Task<IReadOnlyList<ExportOrderSourceRecord>> ListExportOrderSourcesAsync(CancellationToken cancellationToken = default)
        {
            SelectionReadCount++;
            return Task.FromResult<IReadOnlyList<ExportOrderSourceRecord>>([]);
        }

        public Task<IReadOnlyList<ExportEmissionRecord>> ListLatestSuccessfulEmissionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportEmissionRecord>>([]);

        public Task<ExportBatchRecord?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default) => Task.FromResult<ExportBatchRecord?>(null);

        public Task PrepareBatchAsync(ExportBatchRecord batch, CancellationToken cancellationToken = default)
        {
            PrepareCount++;
            return Task.CompletedTask;
        }

        public Task MarkBatchSucceededAsync(Guid batchId, DateTimeOffset completedAtUtc, CancellationToken cancellationToken = default)
        {
            SuccessCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExportBatchRecord>> ListSuccessfulBatchesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ExportBatchRecord>>([]);
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
}
