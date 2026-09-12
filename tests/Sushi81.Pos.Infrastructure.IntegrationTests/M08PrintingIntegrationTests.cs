using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Time;
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
