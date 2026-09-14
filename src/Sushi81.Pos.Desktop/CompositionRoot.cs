using Sushi81.Pos.Infrastructure.Configuration;
using Sushi81.Pos.Infrastructure.Logging;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Paths;
using Sushi81.Pos.Infrastructure.Recovery;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Time;
using Sushi81.Pos.Infrastructure.Ids;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;
using Sushi81.Pos.Infrastructure.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Recovery;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.IO;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Infrastructure.Printing;

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
        IRecoveryScheduler? recoveryScheduler = null;
        IAsyncDisposable? recoverySchedulerDisposable = null;
        DurableChangeNotifier? durableChangeNotifier = null;
        M07RuntimeServices? m07Runtime = null;
        JsonLocalConfigurationService? configurationService = null;
        LocalConfiguration configuration = new();
        M07ConfigurationSetupService? m07Setup = null;
        JsonAuthorityStateStore? authorityStateStore = null;
        AuthorityPhase? authorityPhase = null;
        IOrderPrintApplicationService? printService = null;
        PrinterSetupViewModel? printerSetup = null;
        HiboutikImportOrchestrator? hiboutikImportOrchestrator = null;

        try
        {
            paths.EnsureInitialized();
            loggerProvider = new RollingFileLoggerProvider(paths, TimeProvider.System);
            var logger = loggerProvider.CreateLogger(typeof(CompositionRoot).FullName!);
            startupLogger = logger;
            configurationService = new JsonLocalConfigurationService(paths);
            configuration = await configurationService.LoadAsync();
            authorityStateStore = new JsonAuthorityStateStore(paths);
            m07Setup = new M07ConfigurationSetupService(configurationService, authorityStateStore);
            _ = CultureInfo.GetCultureInfo(configuration.UiCulture);
            cultureStore = new ConfigurationSelectedCultureStore(configuration, configurationService);

            var clock = new TimeProviderBusinessClock(TimeProvider.System, TimeZoneInfo.Local);
            var connectionFactory = new SqliteConnectionFactory(paths);
            var snapshotService = new SqliteLocalRecoverySnapshotService(paths, connectionFactory, clock);
            var businessRevisionReader = new SqliteBusinessRevisionStore(paths, connectionFactory);
            JsonSystemMetadataStore? systemMetadata = null;
            if (!string.IsNullOrWhiteSpace(configuration.OneDriveRoot) && Path.IsPathFullyQualified(configuration.OneDriveRoot))
                systemMetadata = new JsonSystemMetadataStore(configuration.OneDriveRoot!, TimeProvider.System);
            var authorityStartupEvidence = await AuthorityStartupPreflight.CaptureAsync(paths, authorityStateStore);
            var legacyBootstrapEvidence = await authorityStateStore.HasLegacyBootstrapEvidenceAsync();
            var migrations = new SqliteMigrationRunner(
                connectionFactory,
                ProductionMigrations.All,
                clock,
                snapshotService);
            await migrations.InitializeAsync();
            authorityResolution = await new AuthorityStateCoordinator(authorityStateStore, authorityGuard, clock, logger, systemMetadata)
                .InitializeAsync(legacyBootstrapEvidence, authorityStartupEvidence.HasPreExistingLiveDatabase);
            authorityPhase = (await authorityStateStore.LoadAsync())?.Protocol?.Phase;
            var localRecoveryScheduler = new DebouncedRecoveryScheduler(snapshotService, TimeProvider.System, logger);
            recoveryScheduler = localRecoveryScheduler;
            recoverySchedulerDisposable = localRecoveryScheduler;
            OneDriveRecoveryCheckpointScheduler? cloudCheckpointScheduler = null;
            if (systemMetadata is not null)
            {
                var checkpointPublisher = new OneDriveRecoveryCheckpointPublisher(
                    configuration.OneDriveRoot!, authorityStateStore, authorityGuard, snapshotService, clock, businessRevisionReader);
                cloudCheckpointScheduler = new OneDriveRecoveryCheckpointScheduler(
                    checkpointPublisher,
                    Path.Combine(paths.ConfigDirectory, "onedrive-checkpoint-watermark.json"),
                    clock);
                recoveryScheduler = new CompositeRecoveryScheduler(localRecoveryScheduler, cloudCheckpointScheduler);
                recoverySchedulerDisposable = (IAsyncDisposable)recoveryScheduler;
            }
            durableChangeNotifier = await DurableChangeNotifier.CreateAsync(paths, clock, recoveryScheduler, logger, businessRevisionReader);
            var transactionRunner = new SqliteTransactionRunner(connectionFactory);
            var idGenerator = new GuidV7IdGenerator(TimeProvider.System);
            var catalogueStore = new SqliteCatalogueStore(connectionFactory, transactionRunner, idGenerator, clock);
            var settingsStore = new SqliteBusinessSettingsStore(connectionFactory, transactionRunner, clock);
            catalogueService = new CatalogueService(catalogueStore, authorityGuard, durableChangeNotifier);
            settingsService = new BusinessSettingsService(settingsStore, authorityGuard, durableChangeNotifier);
            var orderStore = new SqliteOrderStore(connectionFactory, transactionRunner, null, idGenerator, clock);
            var orderCatalogueQueries = new OrderEntryCatalogueService(catalogueStore);
            hiboutikImportOrchestrator = new HiboutikImportOrchestrator(orderCatalogueQueries, settingsStore);
            orderLifecycleService = new OrderLifecycleService(orderStore, idGenerator, clock, authorityGuard, durableChangeNotifier, orderCatalogueQueries, settingsStore);
            var printDispatcher = new WindowsOrderPrintDispatcher(
                settingsStore,
                configurationService,
                new WindowsPrintDocumentSubmitter(),
                clock);
            printService = new OrderPrintApplicationService(orderStore, printDispatcher);
            printerSetup = new PrinterSetupViewModel(configuration, configurationService, new WindowsPrintQueueCatalog());
            orderEntryService = new OrderEntryService(
                orderCatalogueQueries, settingsStore, orderStore, printDispatcher, idGenerator, clock, authorityGuard, durableChangeNotifier);
            if (systemMetadata is not null)
            {
                var selfJoin = new SelfJoinService(authorityStateStore, authorityGuard, systemMetadata, clock);
                NormalHandoffService? normalHandoff = null;
                TargetAcquisitionService? targetAcquisition = null;
                GitHubHandoffConnectionTester? connectionTester = null;
                DisasterRecoveryService? disasterRecovery = null;
                IRecoveryCandidateDiscovery? recoveryCandidates = null;
                var connectionSetup = string.IsNullOrWhiteSpace(configuration.GitHubOwner)
                    || string.IsNullOrWhiteSpace(configuration.GitHubRepository)
                    ? GitHubConnectionSetupState.RepositoryNotConfigured
                    : string.IsNullOrWhiteSpace(configuration.GitHubCredentialTarget)
                        ? GitHubConnectionSetupState.CredentialNotConfigured
                        : GitHubConnectionSetupState.Ready;
                if (connectionSetup == GitHubConnectionSetupState.Ready)
                {
                    var options = new GitHubHandoffRepositoryOptions(
                        configuration.GitHubOwner!,
                        configuration.GitHubRepository!,
                        configuration.GitHubReleaseTag,
                        configuration.GitHubReleaseName);
                    var credentialProvider = new WindowsCredentialManagerGitHubCredentialProvider(configuration.GitHubCredentialTarget!);
                    var transport = new GitHubReleaseAssetTransport(options, credentialProvider);
                    var snapshotFactory = new LocalRecoveryTransferSnapshotFactory(snapshotService, clock, businessRevisionReader);
                    var faultProbe = new EnvironmentNormalHandoffFaultProbe();
                    normalHandoff = new NormalHandoffService(
                        authorityStateStore, authorityGuard, systemMetadata, snapshotFactory, transport, clock, businessRevisionReader, faultProbe);
                    targetAcquisition = new TargetAcquisitionService(
                        authorityStateStore, authorityGuard, systemMetadata, transport, new SqliteTransferSnapshotInstaller(paths), clock);
                    connectionTester = new GitHubHandoffConnectionTester(transport);
                    recoveryCandidates = new RecoveryCandidateDiscovery(paths, transport, configuration.OneDriveRoot);
                    disasterRecovery = new DisasterRecoveryService(
                        authorityStateStore,
                        authorityGuard,
                        systemMetadata,
                        recoveryCandidates,
                        new RecoveryActivationService(transport),
                        transport,
                        snapshotService,
                        paths,
                        clock);
                }
                m07Runtime = new M07RuntimeServices(
                    authorityGuard,
                    authorityStateStore,
                    systemMetadata,
                    selfJoin,
                    normalHandoff,
                    targetAcquisition,
                    connectionTester,
                    connectionSetup,
                    disasterRecovery,
                    recoveryCandidates);
                await m07Runtime.RefreshAuthorityStateAsync();
            }
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

        var viewModel = new ShellViewModel(
            cultureStore,
            startupSucceeded,
            catalogueService,
            settingsService,
            orderEntryService,
            orderLifecycleService,
            authorityGuard,
            authorityResolution.State,
            m07Runtime,
            configuration,
            m07Setup,
            authorityPhase,
            printService,
            printerSetup,
            hiboutikImportOrchestrator);
        var window = new MainWindow(
            viewModel,
            recoverySchedulerDisposable,
            exception =>
            {
                if (startupLogger is not null) LogFoundationShutdownFailed(startupLogger, exception);
            });
        application.MainWindow = window;
        application.Exit += (_, _) =>
        {
            durableChangeNotifier?.Dispose();
            if (m07Runtime is not null) _ = DisposeM07RuntimeAsync(m07Runtime);
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

    private static async Task DisposeM07RuntimeAsync(M07RuntimeServices runtime) => await runtime.DisposeAsync();

}
