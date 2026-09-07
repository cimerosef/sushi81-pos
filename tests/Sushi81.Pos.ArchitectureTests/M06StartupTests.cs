using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class M06StartupTests
{
    [TestMethod]
    public async Task ProductionStartupWithExistingRecoveryShowsMainWindowAndClosesOnSta()
    {
        var paths = new TestPaths();
        paths.EnsureInitialized();
        var clock = new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Utc);
        var connections = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(connections, ProductionMigrations.All, clock).InitializeAsync();
        var snapshots = new SqliteLocalRecoverySnapshotService(paths, connections, clock);
        await snapshots.CreateAsync(new DurableChange(41, clock.UtcNow));

        Exception? failure = null;
        bool realWindow = false, visible = false, startupSucceeded = false, canWrite = false;
        var thread = new Thread(() =>
        {
            var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Startup += async (_, _) =>
            {
                try
                {
                    await CompositionRoot.StartAsync(app, paths);
                    realWindow = app.MainWindow is MainWindow;
                    visible = app.MainWindow.IsVisible;
                    var shell = (ShellViewModel)app.MainWindow.DataContext;
                    startupSucceeded = shell.StartupSucceeded;
                    canWrite = shell.CanWrite;
                    app.MainWindow.Close();
                }
                catch (Exception exception)
                {
                    failure = exception;
                    app.Shutdown();
                }
            };
            try { app.Run(); }
            catch (Exception exception) { failure = exception; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Production WPF startup/close deadlocked with existing Recovery. Bounded background thread prevents hanging CI.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
        Assert.IsTrue(realWindow);
        Assert.IsTrue(visible, "The production startup must show the real main window.");
        Assert.IsTrue(startupSucceeded);
        Assert.IsTrue(canWrite, "A supported existing installation must bootstrap normally.");
        Assert.AreEqual(41L, await SqliteLocalRecoverySnapshotService.GetHighestValidatedSequenceAsync(paths));
        Directory.Delete(paths.RootDirectory, recursive: true);
    }

    private sealed class TestPaths : IAppPaths
    {
        public string RootDirectory { get; } = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.Tests", "startup-" + Guid.NewGuid().ToString("N"));
        public string DataDirectory => Path.Combine(RootDirectory, "Data");
        public string RecoveryDirectory => Path.Combine(RootDirectory, "Recovery");
        public string CacheDirectory => Path.Combine(RootDirectory, "Cache");
        public string LogsDirectory => Path.Combine(RootDirectory, "Logs");
        public string ConfigDirectory => Path.Combine(RootDirectory, "Config");
        public string TempDirectory => Path.Combine(RootDirectory, "Temp");
        public string LiveDatabasePath => Path.Combine(DataDirectory, "live.db");
        public void EnsureInitialized()
        {
            foreach (var directory in new[] { DataDirectory, RecoveryDirectory, CacheDirectory, LogsDirectory, ConfigDirectory, TempDirectory })
                Directory.CreateDirectory(directory);
        }
    }
}
