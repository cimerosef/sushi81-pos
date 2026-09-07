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
using Sushi81.Pos.Application.Foundation.Paths;

namespace Sushi81.Pos.Desktop;

public static partial class CompositionRoot
{
    private static RollingFileLoggerProvider? loggerProvider;

    public static Task StartAsync(System.Windows.Application application) => StartAsync(application, new WindowsAppPaths());

    internal static async Task StartAsync(System.Windows.Application application, IAppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(application);

        ArgumentNullException.ThrowIfNull(paths);
        ISelectedCultureStore cultureStore = new InMemorySelectedCultureStore();
        var startupSucceeded = false;
        ILogger? startupLogger = null;
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
            startupLogger = logger;
            var configurationService = new JsonLocalConfigurationService(paths);
            var configuration = await configurationService.LoadAsync();
            _ = CultureInfo.GetCultureInfo(configuration.UiCulture);
            cultureStore = new ConfigurationSelectedCultureStore(configuration, configurationService);

            var clock = new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Local);
            var connectionFactory = new SqliteConnectionFactory(paths);
            var snapshotService = new SqliteLocalRecoverySnapshotService(paths, connectionFactory, clock);
            var businessRevisionReader = new SqliteBusinessRevisionStore(paths, connectionFactory);
            var authorityStateStore = new JsonAuthorityStateStore(paths);
            var authorityStartupEvidence = await AuthorityStartupPreflight.CaptureAsync(paths, authorityStateStore);
            var legacyBootstrapEvidence = await authorityStateStore.HasLegacyBootstrapEvidenceAsync();
            var migrations = new SqliteMigrationRunner(
                connectionFactory,
                ProductionMigrations.All,
                clock,
                snapshotService);
            await migrations.InitializeAsync();
            authorityResolution = await new AuthorityStateCoordinator(authorityStateStore, authorityGuard, clock, logger)
                .InitializeAsync(legacyBootstrapEvidence, authorityStartupEvidence.HasPreExistingLiveDatabase);
            recoveryScheduler = new DebouncedRecoveryScheduler(snapshotService, TimeProvider.System, logger);
            durableChangeNotifier = await DurableChangeNotifier.CreateAsync(paths, clock, recoveryScheduler, logger, businessRevisionReader);
            var transactionRunner = new SqliteTransactionRunner(connectionFactory);
            var idGenerator = new GuidV7IdGenerator(TimeProvider.System);
            var catalogueStore = new SqliteCatalogueStore(connectionFactory, transactionRunner, idGenerator, clock);
            var settingsStore = new SqliteBusinessSettingsStore(connectionFactory, transactionRunner, clock);
            catalogueService = new CatalogueService(catalogueStore, authorityGuard, durableChangeNotifier);
            settingsService = new BusinessSettingsService(settingsStore, authorityGuard, durableChangeNotifier);
            var orderStore = new SqliteOrderStore(connectionFactory, transactionRunner, null, idGenerator, clock);
            var orderCatalogueQueries = new OrderEntryCatalogueService(catalogueStore);
            orderLifecycleService = new OrderLifecycleService(orderStore, idGenerator, clock, authorityGuard, durableChangeNotifier, orderCatalogueQueries, settingsStore);
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
        if (recoveryScheduler is not null)
        {
            var closeCoordinator = new AsyncCloseCoordinator(
                recoveryScheduler.DisposeAsync,
                () => application.Dispatcher.BeginInvoke(new Action(application.Shutdown)),
                exception =>
                {
                    if (startupLogger is not null) LogFoundationShutdownFailed(startupLogger, exception);
                });
            window.Closing += (_, closing) => _ = closeCoordinator.HandleClosingAsync(closing);
        }
        application.Exit += (_, _) =>
        {
            durableChangeNotifier?.Dispose();
            loggerProvider?.Dispose();
        };
        window.Show();
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information, Message = "Foundation startup completed.")]
    private static partial void LogFoundationStartupSucceeded(ILogger logger);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Foundation startup failed.")]
    private static partial void LogFoundationStartupFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Error, Message = "Recovery flush failed during orderly application shutdown.")]
    private static partial void LogFoundationShutdownFailed(ILogger logger, Exception exception);

}
