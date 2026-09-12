using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Printing;

namespace Sushi81.Pos.Infrastructure.IntegrationTests;

[TestClass]
public sealed class M08PrintingIntegrationTests
{
    [TestMethod]
    public async Task ReceiptIdentityMigrationSeedsAndPersistsTheApprovedBusinessValues()
    {
        using var paths = new TempPaths();
        var clock = new FixedClock();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();

        await using (var connection = await factory.OpenLiveConnectionAsync())
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT receipt_business_name, receipt_address_line_1, receipt_address_line_2, receipt_siret, receipt_vat_number, receipt_activity_code FROM business_settings;";
            await using var reader = await command.ExecuteReaderAsync();
            Assert.IsTrue(await reader.ReadAsync());
            Assert.AreEqual(ReceiptIdentity.Default.BusinessName, reader.GetString(0));
            Assert.AreEqual(ReceiptIdentity.Default.AddressLine1, reader.GetString(1));
            Assert.AreEqual(ReceiptIdentity.Default.AddressLine2, reader.GetString(2));
            Assert.AreEqual(ReceiptIdentity.Default.Siret, reader.GetString(3));
            Assert.AreEqual(ReceiptIdentity.Default.VatNumber, reader.GetString(4));
            Assert.AreEqual(ReceiptIdentity.Default.ActivityCode, reader.GetString(5));
        }

        var store = new SqliteBusinessSettingsStore(factory, new SqliteTransactionRunner(factory), clock);
        var updatedIdentity = new ReceiptIdentity("Sushi 81 Test", "Adresse 1", "Adresse 2", "SIRET", "TVA", "APE");
        var updated = (await store.GetAsync()) with { ReceiptIdentity = updatedIdentity };
        Assert.IsTrue((await store.UpdateAsync(updated)).Succeeded);
        var reopened = await store.GetAsync();
        Assert.AreEqual(updatedIdentity, reopened.ReceiptIdentity);
    }

    [TestMethod]
    public async Task ThermalLayoutPaginatesToTheQueueImageableHeightOnAnStaThread()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var surface = new PrintImageableSurface(220, 90, 5, 5, 210, 30);
                var pages = ThermalPrintLayout.Paginate(string.Join(Environment.NewLine, Enumerable.Repeat("Ligne de test longue pour la pagination", 12)), surface);
                completion.TrySetResult(pages);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var pages = await completion.Task;
        Assert.IsGreaterThan(1, pages.Count);
        Assert.IsTrue(pages.All(page => !string.IsNullOrWhiteSpace(page)));
    }

    [TestMethod]
    public async Task SemanticReceiptRendererOwnsAlignmentAndKeepsAtomicHeaderTogether()
    {
        var content = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Heading, "*** CUISINE ***", AtomicGroup: "header"),
            new(PrintReceiptBlockKind.LabelValue, "Cmd", "20260912-001", AtomicGroup: "header"),
            new(PrintReceiptBlockKind.LabelValue, "Heure", "10:15", AtomicGroup: "header"),
            new(PrintReceiptBlockKind.Separator, "-"),
            new(PrintReceiptBlockKind.LabelValue, "Adresse", "12 rue très longue à Nemours qui doit être enroulée sans perdre de texte"),
            new(PrintReceiptBlockKind.Total, "TOTAL", "12.50 EUR", AtomicGroup: "total")
        ]);

        var pages = await StaPrintThread.RunAsync(
            () => ThermalPrintLayout.Paginate(content, new PrintImageableSurface(220, 120, 5, 5, 210, 60)),
            CancellationToken.None);

        Assert.IsGreaterThan(1, pages.Count);
        StringAssert.Contains(pages[0], "*** CUISINE ***");
        StringAssert.Contains(pages[0], "Cmd : 20260912-001");
        StringAssert.Contains(pages[0], "Heure : 10:15");
        Assert.IsTrue(pages.SelectMany(page => page.Split(Environment.NewLine)).Any(line => line.Contains("TOTAL : 12.50 EUR", StringComparison.Ordinal)));
        Assert.IsTrue(pages.Any(page => page.Contains("12 rue très longue", StringComparison.Ordinal)));
        Assert.IsFalse(pages.Any(page => page.Contains("Catalogue actuel", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task SemanticReceiptRendererUsesNarrowImageableWidthWithoutFixedWidthFragments()
    {
        var content = new PrintReceiptContent([
            new(PrintReceiptBlockKind.Identity, "Sushi 81", AtomicGroup: "identity"),
            new(PrintReceiptBlockKind.Identity, "SIRET", "90805211100014", AtomicGroup: "identity"),
            new(PrintReceiptBlockKind.Item, "1x P-001", "Un nom de produit volontairement très long pour tester le rendu thermique")
        ]);

        var pages = await StaPrintThread.RunAsync(
            () => ThermalPrintLayout.Paginate(content, new PrintImageableSurface(100, 240, 2, 2, 60, 180)),
            CancellationToken.None);

        var rendered = string.Join(Environment.NewLine, pages);
        StringAssert.Contains(rendered, "Sushi 81");
        StringAssert.Contains(rendered, "908052111000");
        StringAssert.Contains(rendered, "14");
        StringAssert.Contains(rendered, "nom de");
        StringAssert.Contains(rendered, "volontaireme");
        Assert.IsFalse(rendered.Contains(new string(' ', 20), StringComparison.Ordinal));
    }

    [TestMethod]
    public void ThermalLayoutUsesNormalAndNarrowDriverGeometryWithoutFallback()
    {
        var normal = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(5, 5, 210, 30));
        var narrow = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(1, 1, 50, 100));

        Assert.IsFalse(normal.UsedFallback);
        Assert.AreEqual(220d, normal.PageWidth);
        Assert.AreEqual(40d, normal.PageHeight);
        Assert.IsFalse(narrow.UsedFallback);
        Assert.AreEqual(52d, narrow.PageWidth);
        Assert.AreEqual(102d, narrow.PageHeight);
    }

    [TestMethod]
    public void ThermalLayoutUsesMediaOrBoundedFallbackForMissingAndInvalidImageableMetadata()
    {
        var mediaFallback = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(null, null, null, null, 320, 2400));
        var invalidFallback = ThermalPrintLayout.FromGeometry(new PrintImageableGeometry(-1, 0, double.NaN, double.PositiveInfinity, 9999, 99999));
        var defaultFallback = ThermalPrintLayout.FromGeometry(null);

        Assert.IsTrue(mediaFallback.UsedFallback);
        Assert.AreEqual(320d, mediaFallback.PageWidth);
        Assert.AreEqual(ThermalPrintLayout.MaximumFallbackPageHeight, mediaFallback.PageHeight);
        Assert.IsTrue(invalidFallback.UsedFallback);
        Assert.AreEqual(ThermalPrintLayout.MaximumFallbackPageWidth, invalidFallback.PageWidth);
        Assert.AreEqual(ThermalPrintLayout.MaximumFallbackPageHeight, invalidFallback.PageHeight);
        Assert.IsTrue(defaultFallback.UsedFallback);
        Assert.AreEqual(ThermalPrintLayout.FallbackPageWidth, defaultFallback.PageWidth);
        Assert.AreEqual(ThermalPrintLayout.FallbackPageHeight, defaultFallback.PageHeight);
        Assert.IsGreaterThan(0d, defaultFallback.ImageableWidth);
        Assert.IsGreaterThan(0d, defaultFallback.ImageableHeight);
    }

    [TestMethod]
    public async Task ProductionPrintThreadExecutesOnSta()
    {
        var apartmentState = await StaPrintThread.RunAsync(
            () => Thread.CurrentThread.GetApartmentState(),
            CancellationToken.None);

        Assert.AreEqual(ApartmentState.STA, apartmentState);
    }

    [TestMethod]
    public void PrintQueueSelectionNormalizesSortsAndResolvesStableIdentifiers()
    {
        var queues = PrintQueueSelection.Normalize([
            new PrintQueueInfo("z-id", "Zeta"),
            new PrintQueueInfo("a-id", "Alpha"),
            new PrintQueueInfo("A-ID", "Alpha alias"),
            new PrintQueueInfo("", "NameOnly"),
            new PrintQueueInfo("", "nameonly"),
            new PrintQueueInfo("", "")
        ]);

        Assert.HasCount(3, queues);
        Assert.AreEqual("Alpha", queues[0].Name);
        Assert.AreEqual("NameOnly", queues[1].Name);
        Assert.AreEqual("Zeta", queues[2].Name);
        Assert.IsTrue(PrintQueueSelection.Matches(queues[0], "a-id", null));
        Assert.IsTrue(PrintQueueSelection.Matches(queues[0], null, "Alpha"));
        Assert.IsTrue(PrintQueueSelection.Matches(queues[0], null, "a-id"));
        Assert.IsFalse(PrintQueueSelection.Matches(queues[0], "missing", "missing"));
    }

    [TestMethod]
    public void PrintQueueSelectionLeavesAnUnmatchedConfiguredQueueUnavailable()
    {
        var queues = PrintQueueSelection.Normalize([
            new PrintQueueInfo("pdf-id", "Microsoft Print to PDF"),
            new PrintQueueInfo("brother-id", "Brother HL-L2400DWE Printer")
        ]);

        Assert.IsFalse(queues.Any(queue => PrintQueueSelection.Matches(queue, "missing-id", "Missing queue")));
    }

    private sealed class FixedClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 12, 10, 15, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 12);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class TempPaths : IAppPaths, IDisposable
    {
        public TempPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.M08.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
            EnsureInitialized();
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath { get; }
        public void EnsureInitialized() { foreach (var path in new[] { RootDirectory, DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory }) Directory.CreateDirectory(path); }
        public void Dispose() { if (Directory.Exists(RootDirectory)) Directory.Delete(RootDirectory, true); }
    }
}
