using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Logging;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;
using Sushi81.Pos.Infrastructure.Ids;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
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
        CatalogueService? catalogueService = null;
        BusinessSettingsService? settingsService = null;
        OrderEntryService? orderEntryService = null;
        OrderLifecycleService? orderLifecycleService = null;
        var authorityGuard = new WriteAuthorityGuard();
        AuthorityResolution authorityResolution = new(WriteAuthorityState.RecoveryRequired, null);
        DebouncedRecoveryScheduler? recoveryScheduler = null;
        DurableChangeNotifier? durableChangeNotifier = null;

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
                ProductionMigrations.All,
                clock,
                snapshotService);
            await migrations.InitializeAsync();
            var authorityStateStore = new JsonAuthorityStateStore(paths);
            authorityResolution = await new AuthorityStateCoordinator(authorityStateStore, authorityGuard, clock, logger).InitializeAsync();
            recoveryScheduler = new DebouncedRecoveryScheduler(snapshotService, TimeProvider.System, logger);
            durableChangeNotifier = new DurableChangeNotifier(paths, clock, recoveryScheduler, logger);
            var transactionRunner = new SqliteTransactionRunner(connectionFactory);
            var idGenerator = new GuidV7IdGenerator(TimeProvider.System);
            var catalogueStore = new SqliteCatalogueStore(connectionFactory, transactionRunner, idGenerator, clock);
            var settingsStore = new SqliteBusinessSettingsStore(connectionFactory, transactionRunner, clock);
            catalogueService = new CatalogueService(catalogueStore, authorityGuard, durableChangeNotifier);
            settingsService = new BusinessSettingsService(settingsStore, authorityGuard, durableChangeNotifier);
            var orderStore = new SqliteOrderStore(connectionFactory, transactionRunner, null, idGenerator, clock);
            var orderCatalogueQueries = new OrderEntryCatalogueService(catalogueStore);
            orderLifecycleService = new OrderLifecycleService(orderStore, idGenerator, clock, orderCatalogueQueries, settingsStore, authorityGuard, durableChangeNotifier);
            orderEntryService = new OrderEntryService(
                orderCatalogueQueries, settingsStore, orderStore, new NoOpOrderPrintDispatcher(), idGenerator, clock, authorityGuard, durableChangeNotifier);
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

        var viewModel = new ShellViewModel(cultureStore, startupSucceeded, catalogueService, settingsService, orderEntryService, orderLifecycleService, authorityGuard, authorityResolution.State);
        var window = new MainWindow(viewModel);
        application.MainWindow = window;
        application.Exit += (_, _) =>
        {
            if (recoveryScheduler is not null)
            {
                try { recoveryScheduler.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                catch { /* shutdown must not prevent the application from exiting */ }
            }
            durableChangeNotifier?.Dispose();
            loggerProvider?.Dispose();
        };
        window.Show();
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Foundation startup completed.")]
    private static partial void LogFoundationStartupSucceeded(ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Foundation startup failed.")]
    private static partial void LogFoundationStartupFailed(ILogger logger, Exception exception);

}
