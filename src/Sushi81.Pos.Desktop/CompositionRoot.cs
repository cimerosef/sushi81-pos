using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Logging;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Sushi81.Pos.Desktop;

public static partial class CompositionRoot
{
    private static RollingFileLoggerProvider? loggerProvider;

    public static async void Start(System.Windows.Application application)
    {
        ArgumentNullException.ThrowIfNull(application);

        var paths = new WindowsAppPaths();
        ISelectedCultureStore cultureStore = new InMemorySelectedCultureStore();
        var startupSucceeded = false;

        try
        {
            paths.EnsureInitialized();
            loggerProvider = new RollingFileLoggerProvider(paths, TimeProvider.System);
            var logger = loggerProvider.CreateLogger(typeof(CompositionRoot).FullName!);
            var configurationService = new JsonLocalConfigurationService(paths);
            var configuration = await configurationService.LoadAsync();
            _ = CultureInfo.GetCultureInfo(configuration.UiCulture);
            cultureStore = new ConfigurationSelectedCultureStore(configuration, configurationService);

            var clock = new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Local);
            var connectionFactory = new SqliteConnectionFactory(paths);
            var snapshotService = new SqliteLocalRecoverySnapshotService(paths, connectionFactory, clock);
            var migrations = new SqliteMigrationRunner(
                connectionFactory,
                M01Migrations.All,
                clock,
                snapshotService);
            await migrations.InitializeAsync();
            LogFoundationStartupSucceeded(logger);
            startupSucceeded = true;
        }
        catch (Exception exception)
        {
            // The localized shell deliberately remains visible so startup failure is never hidden.
            if (loggerProvider is { } provider)
            {
                LogFoundationStartupFailed(provider.CreateLogger(typeof(CompositionRoot).FullName!), exception);
            }
        }

        var viewModel = new ShellViewModel(cultureStore, startupSucceeded);
        var window = new MainWindow(viewModel);
        application.MainWindow = window;
        application.Exit += (_, _) => loggerProvider?.Dispose();
        window.Show();
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Foundation startup completed.")]
    private static partial void LogFoundationStartupSucceeded(ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Foundation startup failed.")]
    private static partial void LogFoundationStartupFailed(ILogger logger, Exception exception);
}
