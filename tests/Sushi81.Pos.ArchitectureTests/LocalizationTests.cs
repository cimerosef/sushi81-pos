using System.IO;
using System.Globalization;
using System.Text.Json;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Paths;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Desktop;
using Sushi81.Pos.Infrastructure.Configuration;

namespace Sushi81.Pos.ArchitectureTests;

[TestClass]
public sealed class LocalizationTests
{
    private static readonly string[] ChineseLanguageNames = ["法语", "简体中文"];
    private static readonly string[] FrenchLanguageNames = ["Français", "中文（简体）"];

    [TestMethod]
    public async Task ShellUsesFrenchByDefaultAndPersistsAChineseSwitch()
    {
        var store = new InMemorySelectedCultureStore();
        var viewModel = new ShellViewModel(store, startupSucceeded: true);

        Assert.AreEqual("Fondation prête.", viewModel.Status);
        Assert.AreEqual("Langue", viewModel.LanguageLabel);
        Assert.AreEqual("fr-FR", viewModel.SelectedLanguageCultureName);

        await viewModel.ChangeLanguageAsync(viewModel.Languages.Single(option => option.CultureName == "zh-CN"));

        Assert.AreEqual("基础已就绪。", viewModel.Status);
        Assert.AreEqual("语言", viewModel.LanguageLabel);
        Assert.AreEqual("zh-CN", store.Load().Name);
        Assert.AreEqual("zh-CN", viewModel.SelectedLanguageCultureName);
        CollectionAssert.AreEquivalent(ChineseLanguageNames, viewModel.Languages.Select(option => option.DisplayName).ToArray());

        await viewModel.ChangeLanguageAsync(viewModel.Languages.Single(option => option.CultureName == "fr-FR"));

        Assert.AreEqual("Fondation prête.", viewModel.Status);
        Assert.AreEqual("Langue", viewModel.LanguageLabel);
        CollectionAssert.AreEquivalent(FrenchLanguageNames, viewModel.Languages.Select(option => option.DisplayName).ToArray());
    }

    [TestMethod]
    public void UnsupportedPersistedCultureFallsBackToFrench()
    {
        var store = new FixedCultureStore(CultureInfo.GetCultureInfo("en-US"));
        var viewModel = new ShellViewModel(store, startupSucceeded: false);

        Assert.AreEqual("Le démarrage a échoué. Consultez les diagnostics.", viewModel.Status);
    }

    [TestMethod]
    public async Task LanguageSwitchPersistsWithTheProductionConfigurationServiceAndSurvivesRestart()
    {
        using var paths = new TemporaryAppPaths();
        var firstService = new JsonLocalConfigurationService(paths);
        var initialConfiguration = await firstService.LoadAsync();
        var firstViewModel = new ShellViewModel(
            new ConfigurationSelectedCultureStore(initialConfiguration, firstService),
            startupSucceeded: true);

        Assert.AreEqual("fr-FR", firstViewModel.SelectedLanguage.CultureName);

        await firstViewModel.ChangeLanguageAsync(firstViewModel.Languages.Single(option => option.CultureName == "zh-CN"));

        var configurationPath = Path.Combine(paths.ConfigDirectory, "local-settings.json");
        using var persistedJson = JsonDocument.Parse(await File.ReadAllTextAsync(configurationPath));
        Assert.AreEqual("zh-CN", persistedJson.RootElement.GetProperty("uiCulture").GetString());
        Assert.AreEqual("zh-CN", firstViewModel.SelectedLanguageCultureName);

        var restartedService = new JsonLocalConfigurationService(paths);
        var restartedConfiguration = await restartedService.LoadAsync();
        var restartedViewModel = new ShellViewModel(
            new ConfigurationSelectedCultureStore(restartedConfiguration, restartedService),
            startupSucceeded: true);

        Assert.AreEqual("zh-CN", restartedViewModel.SelectedLanguage.CultureName);
        await restartedViewModel.ChangeLanguageAsync(restartedViewModel.Languages.Single(option => option.CultureName == "fr-FR"));
        Assert.AreEqual("fr-FR", (await restartedService.LoadAsync()).UiCulture);
    }

    [TestMethod]
    public async Task ConcurrentLanguageAndPrinterWritersPreserveEachOthersLatestFields()
    {
        using var paths = new TemporaryAppPaths();
        using var configurationService = new JsonLocalConfigurationService(paths);
        var initial = await configurationService.LoadAsync();
        var cultureStore = new ConfigurationSelectedCultureStore(initial, configurationService);
        var printerSetup = new PrinterSetupViewModel(initial, configurationService, new FixedQueueCatalog());
        printerSetup.KitchenQueueId = "kitchen-queue";
        printerSetup.KitchenQueueName = "Kitchen";
        printerSetup.CustomerQueueId = "customer-queue";
        printerSetup.CustomerQueueName = "Customer";

        await Task.WhenAll(
            cultureStore.SaveAsync(CultureInfo.GetCultureInfo("zh-CN")),
            printerSetup.SaveAsync());

        var persisted = await configurationService.LoadAsync();
        Assert.AreEqual("zh-CN", persisted.UiCulture);
        Assert.AreEqual("kitchen-queue", persisted.KitchenPrinterQueueId);
        Assert.AreEqual("customer-queue", persisted.CustomerPrinterQueueId);
    }

    [TestMethod]
    public async Task AmbiguousPrintOutcomeIsLocalizedInFrenchAndChinese()
    {
        var fr = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        var zh = new ShellViewModel(new InMemorySelectedCultureStore(), true);
        await zh.ChangeLanguageAsync(zh.Languages.Single(language => language.CultureName == "zh-CN"));
        var frIssue = new ValidationIssue("kitchen-print", "uncertain", ValidationCodes.PrintAmbiguous);
        var zhIssue = new ValidationIssue("customer-print", "uncertain", ValidationCodes.PrintAmbiguous);

        Assert.AreEqual(fr.Localized["OrderPrintAmbiguous"], M03Presentation.Message(frIssue, fr.Localized));
        Assert.AreEqual(zh.Localized["OrderPrintAmbiguous"], M03Presentation.Message(zhIssue, zh.Localized));
        Assert.AreNotEqual(fr.Localized["OrderPrintKitchenFailure"], M03Presentation.Message(frIssue, fr.Localized));
        Assert.AreNotEqual(zh.Localized["OrderPrintCustomerFailure"], M03Presentation.Message(zhIssue, zh.Localized));
    }

    [TestMethod]
    public void LanguageSwitchIsAwaitableOnASynchronizationContextWithoutBlockingIt()
    {
        var synchronizationContext = new QueuedSynchronizationContext();
        var originalSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);

        try
        {
            var viewModel = new ShellViewModel(new YieldingCultureStore(), startupSucceeded: true);
            var operation = viewModel.ChangeLanguageAsync(viewModel.Languages.Single(option => option.CultureName == "zh-CN"));

            Assert.IsFalse(operation.IsCompleted, "The operation must return control while asynchronous persistence is pending.");
            Assert.IsFalse(viewModel.CanChangeLanguage, "The UI must reject a re-entrant selection while persistence is in progress.");
            synchronizationContext.RunUntilCompleted(operation);

            Assert.IsTrue(operation.IsCompletedSuccessfully);
            Assert.IsTrue(viewModel.CanChangeLanguage);
            Assert.AreEqual("zh-CN", viewModel.SelectedLanguage.CultureName);
            Assert.AreEqual("基础已就绪。", viewModel.Status);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(originalSynchronizationContext);
        }
    }

    [TestMethod]
    public async Task FailedLanguageSaveLeavesTheVisibleSelectionUnchanged()
    {
        var viewModel = new ShellViewModel(new FailingCultureStore(), startupSucceeded: true);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => viewModel.ChangeLanguageAsync(viewModel.Languages.Single(option => option.CultureName == "zh-CN")));

        Assert.AreEqual("fr-FR", viewModel.SelectedLanguage.CultureName);
        Assert.AreEqual("Fondation prête.", viewModel.Status);
    }

    private sealed class FixedCultureStore(CultureInfo culture) : ISelectedCultureStore
    {
        public CultureInfo Load() => culture;

        public Task SaveAsync(CultureInfo selectedCulture, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("The test does not switch culture.");
    }

    private sealed class YieldingCultureStore : ISelectedCultureStore
    {
        private CultureInfo culture = CultureInfo.GetCultureInfo("fr-FR");

        public CultureInfo Load() => culture;

        public async Task SaveAsync(CultureInfo selectedCulture, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            culture = selectedCulture;
        }
    }

    private sealed class FailingCultureStore : ISelectedCultureStore
    {
        public CultureInfo Load() => CultureInfo.GetCultureInfo("fr-FR");

        public Task SaveAsync(CultureInfo selectedCulture, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Synthetic persistence failure."));
    }

    private sealed class FixedQueueCatalog : IPrintQueueCatalog
    {
        public Task<IReadOnlyList<PrintQueueInfo>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrintQueueInfo>>([
                new("kitchen-queue", "Kitchen"),
                new("customer-queue", "Customer")]);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();

        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));

        public void RunUntilCompleted(Task operation)
        {
            while (!operation.IsCompleted && callbacks.Count > 0)
            {
                var (callback, state) = callbacks.Dequeue();
                callback(state);
            }

            Assert.IsTrue(operation.IsCompleted, "The queued UI continuations should complete the asynchronous language switch.");
        }
    }

    private sealed class TemporaryAppPaths : IAppPaths, IDisposable
    {
        public TemporaryAppPaths()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Sushi81.Pos.Tests", Guid.NewGuid().ToString("N"));
            DataDirectory = Path.Combine(RootDirectory, "Data");
            RecoveryDirectory = Path.Combine(RootDirectory, "Recovery");
            CacheDirectory = Path.Combine(RootDirectory, "Cache");
            LogsDirectory = Path.Combine(RootDirectory, "Logs");
            ConfigDirectory = Path.Combine(RootDirectory, "Config");
            TempDirectory = Path.Combine(RootDirectory, "Temp");
            LiveDatabasePath = Path.Combine(DataDirectory, "live.db");
        }

        public string RootDirectory { get; }
        public string DataDirectory { get; }
        public string RecoveryDirectory { get; }
        public string CacheDirectory { get; }
        public string LogsDirectory { get; }
        public string ConfigDirectory { get; }
        public string TempDirectory { get; }
        public string LiveDatabasePath { get; }

        public void EnsureInitialized()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(RecoveryDirectory);
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(TempDirectory);
        }

        public void Dispose()
        {
            if (Directory.Exists(RootDirectory))
            {
                Directory.Delete(RootDirectory, recursive: true);
            }
        }
    }
}
