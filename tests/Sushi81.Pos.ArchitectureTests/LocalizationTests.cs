using System.IO;
using Sushi81.Pos.Infrastructure.Sqlite;
using Sushi81.Pos.Infrastructure.Settings;
using Sushi81.Pos.Infrastructure.Order;
using Sushi81.Pos.Infrastructure.Migrations;
using Sushi81.Pos.Infrastructure.Catalogue;
using Sushi81.Pos.Domain;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Foundation.Ids;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Resources;
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

    [TestMethod]
    public void FrenchAndChineseResourcesHaveExactParityAndLoadThroughResourceManager()
    {
        var repositoryRoot = FindRepositoryRoot();
        var desktopRoot = Path.Combine(repositoryRoot, "src", "Sushi81.Pos.Desktop");
        var french = ReadResourceFile(Path.Combine(desktopRoot, "Properties", "Resources.resx"));
        var chinese = ReadResourceFile(Path.Combine(desktopRoot, "Properties", "Resources.zh-CN.resx"));

        CollectionAssert.AreEquivalent(french.Keys.ToArray(), chinese.Keys.ToArray(), "French and zh-CN resource key sets must match exactly.");
        Assert.IsGreaterThanOrEqualTo(400, french.Count, "The full V1 resource set should be present.");
        var resourceManager = new ResourceManager("Sushi81.Pos.Desktop.Properties.Resources", typeof(ShellViewModel).Assembly);
        var frenchCulture = CultureInfo.GetCultureInfo("fr-FR");
        var chineseCulture = CultureInfo.GetCultureInfo("zh-CN");
        var corruptionMarkers = new[] { "\uFFFD", "Ã", "Â", "â€", "ï¿½", "锟斤拷" };

        foreach (var key in french.Keys)
        {
            var frenchValue = french[key];
            var chineseValue = chinese[key];
            Assert.IsFalse(string.IsNullOrWhiteSpace(frenchValue), $"French resource {key} is empty.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(chineseValue), $"zh-CN resource {key} is empty.");
            Assert.AreEqual(NormalizeResourceLineEndings(frenchValue), NormalizeResourceLineEndings(resourceManager.GetString(key, frenchCulture)!), $"French ResourceManager lookup failed for {key}.");
            Assert.AreEqual(NormalizeResourceLineEndings(chineseValue), NormalizeResourceLineEndings(resourceManager.GetString(key, chineseCulture)!), $"zh-CN ResourceManager lookup failed for {key}.");

            foreach (var marker in corruptionMarkers)
            {
                Assert.IsFalse(frenchValue.Contains(marker, StringComparison.Ordinal), $"French resource {key} contains a text-corruption marker.");
                Assert.IsFalse(chineseValue.Contains(marker, StringComparison.Ordinal), $"zh-CN resource {key} contains a text-corruption marker.");
            }

            CollectionAssert.AreEqual(
                PlaceholderSignature(frenchValue, key, "fr-FR"),
                PlaceholderSignature(chineseValue, key, "zh-CN"),
                $"Composite-format placeholder indexes differ for {key}.");
        }
    }

    [TestMethod]
    public async Task RepresentativeV1AreasSwitchBothWaysAndKeepFormattingAndContractIds()
    {
        using var french = new ShellViewModel(new InMemorySelectedCultureStore(), startupSucceeded: true);
        var keys = new[]
        {
            "ShellTitle", "AuthorityReadOnly", "M03StartupFailure", "JoinPrompt", "M07AcquireAuthority",
            "M07DisasterRecoverySucceeded", "Catalogue", "CatalogueImportPreviewTitle", "CatalogueExport",
            "NewOrder", "OrderClose", "OrderSearch", "DashboardHiboutikCard", "OrderPrintCustomerFailure",
            "OrderReprintKitchen", "HiboutikPasteInstructions", "GestionExportHistory", "GestionExportRegenerate",
            "GestionExportDatePickerWatermark", "DataTools", "ArchiveAccess", "ArchiveNoArchives", "ValidationGeneric", "OperationFailed"
        };

        Assert.AreEqual("Données", french.Localized["DataTools"]);

        foreach (var key in keys)
            Assert.IsFalse(string.IsNullOrWhiteSpace(french.Localized[key]), $"French representative resource {key} did not resolve.");

        var frenchCatalogue = french.Localized["Catalogue"];
        var frenchAmount = string.Format(CultureInfo.GetCultureInfo("fr-FR"), french.Localized["HiboutikSourceTotalFormat"], 1234.5m);
        var frenchDate = new DateTime(2026, 9, 25).ToString("d", CultureInfo.GetCultureInfo("fr-FR"));
        Assert.AreEqual("25/09/2026", frenchDate);
        StringAssert.Contains(frenchAmount, "1234,50");

        foreach (var key in new[] { "GestionExportCreate", "GestionExportUpdate", "GestionExportCancel" })
            Assert.AreEqual(key["GestionExport".Length..].ToUpperInvariant(), french.Localized[key], $"The fixed {key} export identifier changed.");

        await french.ChangeLanguageAsync(french.Languages.Single(option => option.CultureName == "zh-CN"));

        foreach (var key in keys)
            Assert.IsFalse(string.IsNullOrWhiteSpace(french.Localized[key]), $"zh-CN representative resource {key} did not resolve.");
        Assert.AreNotEqual(frenchCatalogue, french.Localized["Catalogue"]);
        Assert.AreEqual("数据", french.Localized["DataTools"]);
        Assert.AreEqual("2026/9/25", new DateTime(2026, 9, 25).ToString("d", CultureInfo.GetCultureInfo("zh-CN")));
        StringAssert.Contains(
            string.Format(CultureInfo.GetCultureInfo("zh-CN"), french.Localized["HiboutikSourceTotalFormat"], 1234.5m),
            "1234.50");
        foreach (var key in new[] { "GestionExportCreate", "GestionExportUpdate", "GestionExportCancel" })
            Assert.AreEqual(key["GestionExport".Length..].ToUpperInvariant(), french.Localized[key], $"The fixed {key} export identifier changed in zh-CN.");

        await french.ChangeLanguageAsync(french.Languages.Single(option => option.CultureName == "fr-FR"));
        Assert.AreEqual(frenchCatalogue, french.Localized["Catalogue"]);
    }

    [TestMethod]
    public void DesktopLocalizationAuditHasOnlyDocumentedXamlLiteralsAndNoRawExceptionSinks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var desktopRoot = Path.Combine(repositoryRoot, "src", "Sushi81.Pos.Desktop");
        var french = ReadResourceFile(Path.Combine(desktopRoot, "Properties", "Resources.resx"));
        var chinese = ReadResourceFile(Path.Combine(desktopRoot, "Properties", "Resources.zh-CN.resx"));
        var allowedXamlLiterals = new HashSet<string>(StringComparer.Ordinal)
        {
            // Punctuation and action glyphs plus the fixed external export identifier column name.
            "↻", ":", "(", ")", "BatchId", "−", "+", "✎", "×"
        };
        var visibleAttributeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Content", "Text", "Header", "Title", "ToolTip", "Watermark"
        };
        var unexplained = new List<string>();

        foreach (var path in Directory.EnumerateFiles(desktopRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path);
            foreach (var element in document.Root!.DescendantsAndSelf())
            foreach (var attribute in element.Attributes())
            {
                if (!visibleAttributeNames.Contains(attribute.Name.LocalName)) continue;
                var value = attribute.Value.Trim();
                if (value.Length == 0 || value.StartsWith('{')) continue;
                if (!allowedXamlLiterals.Contains(value)) unexplained.Add($"{Path.GetRelativePath(repositoryRoot, path)}: {attribute.Name.LocalName}={value}");
            }
        }
        Assert.IsEmpty(unexplained, "Unlocalized XAML literals require a documented allowlist entry: " + string.Join("; ", unexplained));

        var localizedKeys = new HashSet<string>(StringComparer.Ordinal);
        var patterns = new[]
        {
            new Regex(@"\bLocalizedText\s*\(\s*[^,]+,\s*""(?<key>[^""]+)""", RegexOptions.CultureInvariant),
            new Regex(@"\b(?:Localized|Label|Text)\s*\(\s*""(?<key>[^""]+)""", RegexOptions.CultureInvariant)
        };
        var conditionalPattern = new Regex(@"\bLocalizedText\s*\(\s*[^,]+,\s*[^,?]+\?\s*""(?<first>[^""]+)""\s*:\s*""(?<second>[^""]+)""", RegexOptions.CultureInvariant);
        foreach (var path in Directory.EnumerateFiles(desktopRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            foreach (var pattern in patterns)
            foreach (Match match in pattern.Matches(source))
                localizedKeys.Add(match.Groups["key"].Value);
            foreach (Match match in conditionalPattern.Matches(source))
            {
                localizedKeys.Add(match.Groups["first"].Value);
                localizedKeys.Add(match.Groups["second"].Value);
            }

            Assert.IsFalse(Regex.IsMatch(source, @"MessageBox\s*\.\s*Show\s*\([^;\r\n]*\bexception\.Message", RegexOptions.CultureInvariant), $"Raw exception text is shown in {Path.GetRelativePath(repositoryRoot, path)}.");
            Assert.IsFalse(Regex.IsMatch(source, @"(?:ValidationMessage\s*=\s*|validation\.Text\s*=\s*)exception\.Message", RegexOptions.CultureInvariant), $"Raw exception text is assigned to a user-facing validation surface in {Path.GetRelativePath(repositoryRoot, path)}.");
        }

        Assert.IsGreaterThanOrEqualTo(100, localizedKeys.Count, "The audit must inspect the Desktop localization call sites.");
        foreach (var key in localizedKeys)
        {
            Assert.IsTrue(french.TryGetValue(key, out var frenchValue) && !string.IsNullOrWhiteSpace(frenchValue), $"Missing French localization for call-site key {key}.");
            Assert.IsTrue(chinese.TryGetValue(key, out var chineseValue) && !string.IsNullOrWhiteSpace(chineseValue), $"Missing zh-CN localization for call-site key {key}.");
        }
    }

    [TestMethod]
    public async Task SwitchingCultureLeavesPersistedCatalogueAndOrderTextUnchanged()
    {
        using var paths = new TemporaryAppPaths();
        var clock = new FixedBusinessClock();
        var ids = new DeterministicIds();
        var factory = new SqliteConnectionFactory(paths);
        await new SqliteMigrationRunner(factory, ProductionMigrations.All, clock).InitializeAsync();
        var runner = new SqliteTransactionRunner(factory);
        var catalogueStore = new SqliteCatalogueStore(factory, runner, ids, clock);
        var catalogue = new CatalogueService(catalogueStore);
        var categoryResult = await catalogue.CreateCategoryAsync("Plats épicés 中文");
        Assert.IsTrue(categoryResult.Succeeded, string.Join("; ", categoryResult.Issues.Select(issue => issue.Message)));
        var category = categoryResult.Value!;
        const string productName = "Sushi épicé 生鱼";
        var productResult = await catalogue.CreateProductAsync(new ProductDraft(
            Guid.Empty, "L10N-001", productName, category.Id, Money.FromCents(10000), 10m, true, true, false, []));
        Assert.IsTrue(productResult.Succeeded, string.Join("; ", productResult.Issues.Select(issue => issue.Message)));
        var productId = productResult.Value!;
        var orderStore = new SqliteOrderStore(factory, runner);
        using var orderEntryService = new OrderEntryService(
            new OrderEntryCatalogueService(catalogueStore),
            new SqliteBusinessSettingsStore(factory, runner, clock),
            orderStore,
            new NoOpOrderPrintDispatcher(),
            ids,
            clock);
        using var configurationService = new JsonLocalConfigurationService(paths);
        var configuration = await configurationService.LoadAsync();
        using var shell = new ShellViewModel(
            new ConfigurationSelectedCultureStore(configuration, configurationService),
            startupSucceeded: true,
            catalogueService: catalogue,
            orderEntryService: orderEntryService);

        var selected = await new OrderEntryCatalogueService(catalogueStore).GetActiveProductAsync(productId);
        Assert.IsNotNull(selected);
        const string telephone = "0612345678";
        const string address = "12 rue des Fleurs 中文";
        const string comment = "Déposer devant la porte 请放门口";
        var result = await orderEntryService.ConfirmNewOrderAsync(new NewOrderDraft(
            [new OrderLineDraft(Guid.Empty, selected.Aggregate, [], [], 1, selected.CategoryName)],
            FulfilmentMode.Livraison,
            clock.BusinessDate,
            new TimeOnly(12, 5),
            telephone,
            address,
            comment,
            false));
        Assert.IsTrue(result.Succeeded, string.Join("; ", result.Issues.Select(issue => issue.Message)));
        var savedOrderId = (await orderStore.ListByPlannedDateAsync(clock.BusinessDate)).Single().Id;
        var beforeOrder = await orderStore.GetByIdAsync(savedOrderId);
        var beforeProduct = await catalogue.GetProductForEditAsync(productId);
        var beforeCategory = (await catalogue.ListCategoriesAsync()).Single(item => item.Id == category.Id);

        await shell.ChangeLanguageAsync(shell.Languages.Single(option => option.CultureName == "zh-CN"));
        Assert.AreEqual("语言", shell.LanguageLabel);
        await shell.ChangeLanguageAsync(shell.Languages.Single(option => option.CultureName == "fr-FR"));
        Assert.AreEqual("Langue", shell.LanguageLabel);

        var afterOrder = await orderStore.GetByIdAsync(savedOrderId);
        var afterProduct = await catalogue.GetProductForEditAsync(productId);
        var afterCategory = (await catalogue.ListCategoriesAsync()).Single(item => item.Id == category.Id);
        Assert.IsNotNull(beforeOrder);
        Assert.IsNotNull(afterOrder);
        Assert.IsNotNull(beforeProduct);
        Assert.IsNotNull(afterProduct);
        Assert.AreEqual(beforeProduct.Name, afterProduct.Name);
        Assert.AreEqual(beforeProduct.Code, afterProduct.Code);
        Assert.AreEqual(beforeCategory.Name, afterCategory.Name);
        Assert.AreEqual(beforeOrder.Telephone, afterOrder.Telephone);
        Assert.AreEqual(beforeOrder.DeliveryAddress, afterOrder.DeliveryAddress);
        Assert.AreEqual(beforeOrder.Comment, afterOrder.Comment);
        Assert.AreEqual(beforeOrder.Items.Single().ProductName, afterOrder.Items.Single().ProductName);
        Assert.AreEqual(beforeOrder.Items.Single().CategoryName, afterOrder.Items.Single().CategoryName);
    }

    private static Dictionary<string, string> ReadResourceFile(string path) =>
        XDocument.Load(path).Root!.Elements("data").ToDictionary(
            element => element.Attribute("name")?.Value ?? throw new AssertFailedException($"Resource without a key in {path}."),
            element => element.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);

    private static string NormalizeResourceLineEndings(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
    private static int[] PlaceholderSignature(string value, string key, string culture)
    {
        var format = CompositeFormat.Parse(value);
        var matches = Regex.Matches(value, @"(?<!\{)\{(?<index>\d+)(?:\s*,\s*[+-]?\d+)?(?:\s*:[^{}]*)?\}(?!\})");
        var indexes = matches.Select(match => int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture)).Order().ToArray();
        var minimumArgumentCount = indexes.Length == 0 ? 0 : indexes[^1] + 1;
        Assert.AreEqual(format.MinimumArgumentCount, minimumArgumentCount, $"Placeholder scan and CompositeFormat disagree for {culture} resource {key}.");
        return indexes;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Sushi81.Pos.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new AssertFailedException("Could not locate the Sushi81 POS repository root.");
    }

    private sealed class FixedBusinessClock : IBusinessClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        public DateOnly BusinessDate => new(2026, 9, 25);
        public TimeZoneInfo BusinessTimeZone => TimeZoneInfo.Utc;
    }

    private sealed class DeterministicIds : IIdGenerator
    {
        private int counter;
        public Guid NewId() => Guid.Parse($"10000000-0000-0000-0000-{Interlocked.Increment(ref counter):D12}");
    }

    private sealed class NoOpOrderPrintDispatcher : IOrderPrintDispatcher
    {
        public Task DispatchAsync(OrderSnapshot committedOrder, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
