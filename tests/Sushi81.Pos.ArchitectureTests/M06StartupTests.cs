using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
[DoNotParallelize]
public sealed class M06StartupTests
{
    [TestMethod]
    public void AFreshDefaultShellShowsLocalizedTechnicalSetupAndKeepsSelfJoinUnavailable()
    {
        RunOnSta(() =>
        {
            var paths = new TestPaths();
            try
            {
                var configurationService = new JsonLocalConfigurationService(paths);
                var configuration = configurationService.LoadAsync().GetAwaiter().GetResult();
                using var shell = new ShellViewModel(
                    new InMemorySelectedCultureStore(),
                    startupSucceeded: false,
                    configuration: configuration,
                    m07Setup: new M07ConfigurationSetupService(configurationService));
                var window = new MainWindow(shell) { Width = 760, Height = 520, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();

                var frenchButtons = VisibleButtonLabels(window);
                CollectionAssert.Contains(frenchButtons, shell.Localized["M07Setup"]);
                CollectionAssert.DoesNotContain(frenchButtons, shell.Localized["JoinExistingLineage"]);
                Assert.IsTrue(shell.CanConfigureM07);
                Assert.IsFalse(shell.CanJoinExistingLineage);

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                window.UpdateLayout();
                CollectionAssert.Contains(VisibleButtonLabels(window), shell.Localized["M07Setup"]);
                CollectionAssert.DoesNotContain(VisibleButtonLabels(window), shell.Localized["JoinExistingLineage"]);
                Assert.AreEqual("配置 Sushi81 系统", shell.Localized["M07Setup"]);
                window.Close();
            }
            finally
            {
                if (Directory.Exists(paths.RootDirectory)) Directory.Delete(paths.RootDirectory, recursive: true);
            }
        });
    }

    [TestMethod]
    public void UnsafeAuthorityPhaseHidesTechnicalSetupInFrenchAndChinese()
    {
        RunOnSta(() =>
        {
            var paths = new TestPaths();
            try
            {
                var configurationService = new JsonLocalConfigurationService(paths);
                var configuration = configurationService.LoadAsync().GetAwaiter().GetResult();
                using var shell = new ShellViewModel(
                    new InMemorySelectedCultureStore(),
                    startupSucceeded: false,
                    configuration: configuration,
                    m07Setup: new M07ConfigurationSetupService(configurationService),
                    authorityPhase: AuthorityPhase.TransferPreparing);
                var window = new MainWindow(shell) { Width = 760, Height = 520, ShowInTaskbar = false };
                window.Show();
                window.UpdateLayout();

                Assert.IsFalse(shell.CanConfigureM07);
                CollectionAssert.DoesNotContain(VisibleButtonLabels(window), shell.Localized["M07Setup"]);

                shell.ChangeLanguageAsync(shell.Languages.Single(language => language.CultureName == "zh-CN")).GetAwaiter().GetResult();
                window.UpdateLayout();
                Assert.IsFalse(shell.CanConfigureM07);
                CollectionAssert.DoesNotContain(VisibleButtonLabels(window), shell.Localized["M07Setup"]);
                window.Close();
            }
            finally
            {
                if (Directory.Exists(paths.RootDirectory)) Directory.Delete(paths.RootDirectory, recursive: true);
            }
        });
    }

    [TestMethod]
    public async Task ProductionStartupWithExistingRecoveryShowsMainWindowAndClosesOnSta()
    {
        var paths = new TestPaths();
        var freshPaths = new TestPaths();
        var setupPaths = new TestPaths();
        var joinPaths = new TestPaths();
        try
        {
            paths.EnsureInitialized();
            var clock = new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Utc);
            var connections = new SqliteConnectionFactory(paths);
            await new SqliteMigrationRunner(connections, ProductionMigrations.All, clock).InitializeAsync();
            var snapshots = new SqliteLocalRecoverySnapshotService(paths, connections, clock);
            await snapshots.CreateAsync(new DurableChange(41, clock.UtcNow));

            var setupSharedRoot = Path.Combine(setupPaths.RootDirectory, "Shared OneDrive");
            var joinSharedRoot = Path.Combine(joinPaths.RootDirectory, "Shared OneDrive");
            Directory.CreateDirectory(setupSharedRoot);
            Directory.CreateDirectory(joinSharedRoot);
            var setupLineage = Guid.NewGuid();
            var joinLineage = Guid.NewGuid();
            await new JsonSystemMetadataStore(setupSharedRoot).EnsureCurrentLineageAsync(setupLineage, 1);
            await new JsonSystemMetadataStore(joinSharedRoot).EnsureCurrentLineageAsync(joinLineage, 1);
            await ConfigureSharedRootAsync(setupPaths, setupSharedRoot);
            await ConfigureSharedRootAsync(joinPaths, joinSharedRoot);

            Exception? failure = null;
            bool realWindow = false, visible = false, startupSucceeded = false, canWrite = false;
            var thread = new Thread(() =>
            {
                var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Startup += async (_, _) =>
                {
                    try
                    {
                        // Existing M06 recovery startup remains the production baseline.
                        await CompositionRoot.StartAsync(app, paths);
                        realWindow = app.MainWindow is MainWindow;
                        visible = app.MainWindow.IsVisible;
                        var shell = (ShellViewModel)app.MainWindow.DataContext;
                        startupSucceeded = shell.StartupSucceeded;
                        canWrite = shell.CanWrite;
                        app.MainWindow.Close();
                        await Task.Yield();

                        // R18-A: first production startup creates the DB but remains read-only;
                        // repeated full production restarts cannot bootstrap a new lineage.
                        for (var restart = 0; restart < 3; restart++)
                        {
                            await CompositionRoot.StartAsync(app, freshPaths);
                            var freshShell = (ShellViewModel)app.MainWindow.DataContext;
                            Require(freshShell.StartupSucceeded, "Fresh production startup did not succeed.");
                            Require(!freshShell.CanWrite, "Fresh production startup became writable.");
                            app.MainWindow.Close();
                            await Task.Yield();
                        }
                        var freshStore = new JsonAuthorityStateStore(freshPaths);
                        Require(File.Exists(Path.Combine(freshPaths.ConfigDirectory, "m07-fresh-install.marker")), "Fresh Config provenance is missing.");
                        Require(File.Exists(Path.Combine(freshPaths.DataDirectory, "m07-fresh-install.anchor")), "Fresh Data provenance is missing.");
                        Require(!File.Exists(Path.Combine(freshPaths.ConfigDirectory, "authority-state.json")), "Fresh startup created authority state.");
                        Require(!await freshStore.HasLegacyBootstrapEvidenceAsync(), "Fresh startup qualified as legacy bootstrap evidence.");

                        // R18-B/F: setup against existing shared metadata offers self-join while
                        // leaving business writes and no-grant acquisition unavailable.
                        await CompositionRoot.StartAsync(app, setupPaths);
                        var setupShell = (ShellViewModel)app.MainWindow.DataContext;
                        Require(!setupShell.CanWrite, "Fresh setup became writable.");
                        Require(setupShell.CanJoinExistingLineage, "Fresh setup did not expose self-join.");
                        var noGrant = await setupShell.AcquireTransferredAuthorityAsync();
                        Require(noGrant is not null, "No-grant acquisition returned no result.");
                        Require(!noGrant!.Succeeded, "No-grant acquisition unexpectedly succeeded.");
                        Require(!setupShell.CanWrite, "No-grant acquisition changed write authority.");
                        Require(!File.Exists(Path.Combine(setupPaths.ConfigDirectory, "authority-state.json")), "No-grant acquisition created authority state.");
                        app.MainWindow.Close();
                        await Task.Yield();

                        // R18-C: explicit self-join persists exact identity/lineage/generation
                        // and production restart reconstructs the same read-only state.
                        await CompositionRoot.StartAsync(app, joinPaths);
                        var joinShell = (ShellViewModel)app.MainWindow.DataContext;
                        Require(joinShell.CanJoinExistingLineage, "Production self-join was not exposed.");
                        var joined = await joinShell.JoinExistingLineageAsync("Fresh B");
                        Require(joined.Readiness == PairingReadiness.PairedUninitializedReadOnly, "Self-join did not remain paired/read-only.");
                        Require(!joinShell.CanWrite, "Self-join became writable.");
                        app.MainWindow.Close();
                        await Task.Yield();

                        var joinStore = new JsonAuthorityStateStore(joinPaths);
                        var joinedDocument = await joinStore.LoadAsync();
                        var joinedProtocol = joinedDocument?.Protocol;
                        Require(joinedProtocol is not null, "Self-join did not persist protocol state.");
                        Require(joinedProtocol!.LineageId == joinLineage, "Self-join persisted a different lineage.");
                        Require(joinedProtocol.Generation == 1, "Self-join persisted a different generation.");
                        var joinedDeviceId = joinedProtocol.DeviceId;

                        await CompositionRoot.StartAsync(app, joinPaths);
                        var restartedJoinShell = (ShellViewModel)app.MainWindow.DataContext;
                        var restartedDocument = await joinStore.LoadAsync();
                        Require(restartedDocument?.Protocol?.DeviceId == joinedDeviceId, "Self-join device identity changed after restart.");
                        Require(restartedDocument?.Protocol?.LineageId == joinLineage, "Self-join lineage changed after restart.");
                        Require(restartedDocument?.Protocol?.Generation == 1, "Self-join generation changed after restart.");
                        Require(!restartedJoinShell.CanWrite, "Self-join restart became writable.");
                        Require(!restartedJoinShell.CanJoinExistingLineage, "Paired self-join incorrectly remained joinable.");
                        app.MainWindow.Close();
                        await Task.Yield();
                        app.Shutdown();
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
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(45)), "Production M06/R18 startup sequence did not close on a bounded STA thread.");
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            Assert.IsTrue(realWindow);
            Assert.IsTrue(visible, "The production startup must show the real main window.");
            Assert.IsTrue(startupSucceeded);
            Assert.IsTrue(canWrite, "A supported existing installation must bootstrap normally.");
            Assert.AreEqual(41L, await SqliteLocalRecoverySnapshotService.GetHighestValidatedSequenceAsync(paths));
        }
        finally
        {
            foreach (var testPaths in new[] { paths, freshPaths, setupPaths, joinPaths })
            {
                if (Directory.Exists(testPaths.RootDirectory)) Directory.Delete(testPaths.RootDirectory, recursive: true);
            }
        }
    }

    private static async Task ConfigureSharedRootAsync(TestPaths paths, string sharedRoot)
    {
        var configurationService = new JsonLocalConfigurationService(paths);
        var result = await new M07ConfigurationSetupService(configurationService, new JsonAuthorityStateStore(paths))
            .ValidateAndPersistAsync(await configurationService.LoadAsync(), new(sharedRoot, null, null, null, null, null));
        Assert.IsTrue(result.Succeeded);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string[] VisibleButtonLabels(Window window) =>
        VisualDescendants<Button>(window)
            .Where(button => button.Visibility == Visibility.Visible)
            .Select(button => button.Content as string)
            .Where(content => content is not null)
            .Cast<string>()
            .ToArray();

    private static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
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
