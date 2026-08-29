using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;

namespace Sushi81.Pos.Desktop;

public interface ISelectedCultureStore
{
    CultureInfo Load();

    Task SaveAsync(CultureInfo culture, CancellationToken cancellationToken = default);
}

public sealed class InMemorySelectedCultureStore : ISelectedCultureStore
{
    private CultureInfo _culture = CultureInfo.GetCultureInfo("fr-FR");

    public CultureInfo Load() => _culture;

    public Task SaveAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        cancellationToken.ThrowIfCancellationRequested();
        _culture = culture;
        return Task.CompletedTask;
    }
}

public sealed class ConfigurationSelectedCultureStore : ISelectedCultureStore
{
    private readonly ILocalConfigurationService _configurationService;
    private LocalConfiguration _configuration;

    public ConfigurationSelectedCultureStore(LocalConfiguration configuration, ILocalConfigurationService configurationService)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    public CultureInfo Load() => CultureInfo.GetCultureInfo(_configuration.UiCulture);

    public async Task SaveAsync(CultureInfo culture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var updatedConfiguration = _configuration with { UiCulture = culture.Name };
        await _configurationService.SaveAsync(updatedConfiguration, cancellationToken);
        _configuration = updatedConfiguration;
    }
}

public sealed record LanguageOption(string CultureName, string DisplayName);

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private static readonly ResourceManager ResourceManager = new("Sushi81.Pos.Desktop.Properties.Resources", typeof(ShellViewModel).Assembly);
    private readonly ISelectedCultureStore _cultureStore;
    private CultureInfo _culture;
    private LanguageOption _selectedLanguage;
    private bool _isLanguageChangeInProgress;

    public ShellViewModel(ISelectedCultureStore cultureStore, bool startupSucceeded, CatalogueService? catalogueService = null, BusinessSettingsService? settingsService = null)
    {
        _cultureStore = cultureStore ?? throw new ArgumentNullException(nameof(cultureStore));
        _culture = Normalize(_cultureStore.Load());
        StartupSucceeded = startupSucceeded;
        Languages = new ObservableCollection<LanguageOption>();
        RefreshResources();
        _selectedLanguage = Languages.Single(option => option.CultureName == _culture.Name);
        Admin = startupSucceeded && catalogueService is not null && settingsService is not null ? new M03ShellViewModel(catalogueService, settingsService) : null;
        Admin?.ApplyLocalization(Localized["All"], Localized["Active"], Localized["Inactive"], Localized["Activate"], Localized["Deactivate"]);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LanguageOption> Languages { get; }

    public bool StartupSucceeded { get; }

    public M03ShellViewModel? Admin { get; }

    public bool IsM03Available => Admin is not null;

    public string Title { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string LanguageLabel { get; private set; } = string.Empty;

    public string LanguageSaveFailure { get; private set; } = string.Empty;

    public IReadOnlyDictionary<string, string> Localized { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
    }

    public bool CanChangeLanguage => !_isLanguageChangeInProgress;

    /// <summary>
    /// Persists the culture before refreshing localized resources, so a failed save leaves the
    /// visible and persisted selections unchanged.
    /// </summary>
    public async Task ChangeLanguageAsync(LanguageOption language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);
        var requestedCulture = Normalize(CultureInfo.GetCultureInfo(language.CultureName));
        if (requestedCulture.Name == _culture.Name)
        {
            return;
        }

        if (_isLanguageChangeInProgress)
        {
            return;
        }

        _isLanguageChangeInProgress = true;
        OnPropertyChanged(nameof(CanChangeLanguage));
        try
        {
            await _cultureStore.SaveAsync(requestedCulture, cancellationToken);

            _culture = requestedCulture;
            RefreshResources();
            Admin?.ApplyLocalization(Localized["All"], Localized["Active"], Localized["Inactive"], Localized["Activate"], Localized["Deactivate"]);
            _selectedLanguage = Languages.Single(option => option.CultureName == _culture.Name);
            OnPropertyChanged(nameof(SelectedLanguage));
        }
        catch
        {
            // Restore the ComboBox selection to the last successfully persisted culture.
            OnPropertyChanged(nameof(SelectedLanguage));
            throw;
        }
        finally
        {
            _isLanguageChangeInProgress = false;
            OnPropertyChanged(nameof(CanChangeLanguage));
        }
    }

    private void RefreshResources()
    {
        Title = Read("ShellTitle");
        Status = Read(StartupSucceeded ? "FoundationReady" : "StartupFailure");
        LanguageLabel = Read("LanguageLabel");
        LanguageSaveFailure = Read("LanguageSaveFailure");
        Languages.Clear();
        Languages.Add(new LanguageOption("fr-FR", Read("FrenchLanguage")));
        Languages.Add(new LanguageOption("zh-CN", Read("ChineseLanguage")));
        var keys = new[] { "Catalogue", "Settings", "Search", "Category", "All", "Active", "Inactive", "NewProduct", "Edit", "Save", "Cancel", "Activate", "Deactivate", "DeletePermanently", "ManageCategories", "Code", "Name", "PriceTtc", "Vat", "DiscountEligible", "OptionsEnabled", "OptionGroups", "Options", "Required", "Optional", "Single", "Multi", "Minimum", "Maximum", "AdjustmentTtc", "MoveUp", "MoveDown", "PickupDiscount", "PickupMinimum", "DeliveryMinimum", "DeliveryFee", "DeliveryFeeEnabled", "EmptyCatalogue", "DeleteConfirm", "M03StartupFailure", "DeliveryFeeVatFixed", "CreateCategory", "CreateCategoryFirst", "RenameCategory", "Close", "EnterValidValues", "Saved", "ValidationGeneric", "ValidationRequired", "ValidationCategoryDuplicate", "ValidationCategoryMissing", "ValidationProductMissing", "ValidationProductDuplicateCode", "ValidationPriceNegative", "ValidationVatRange", "ValidationRequiredChoices", "ValidationSettingsRange", "ValidationInvalidNumber", "ValidationBusy", "ValidationGroupStructure", "ValidationOptionStructure", "ValidationConflict", "ValidationField", "CategoryEdit", "CategoryCreateSave", "CategoryRenameSave", "CategoryEditCancel", "DirtyEditorClose", "Discard", "KeepEditing", "OptionName", "OptionActive" };
        Localized = keys.ToDictionary(key => key, Read, StringComparer.Ordinal);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(LanguageSaveFailure));
        OnPropertyChanged(nameof(Localized));
    }

    private string Read(string key) => ResourceManager.GetString(key, _culture) ?? throw new InvalidOperationException($"Missing required localization resource '{key}'.");

    private static CultureInfo Normalize(CultureInfo culture) => culture.Name switch
    {
        "fr-FR" => culture,
        "zh-CN" => culture,
        _ => CultureInfo.GetCultureInfo("fr-FR"),
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
