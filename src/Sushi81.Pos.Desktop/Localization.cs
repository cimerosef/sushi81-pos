using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Foundation.Configuration;

namespace Sushi81.Pos.Desktop;

public interface ISelectedCultureStore
{
    CultureInfo Load();

    void Save(CultureInfo culture);
}

public sealed class InMemorySelectedCultureStore : ISelectedCultureStore
{
    private CultureInfo _culture = CultureInfo.GetCultureInfo("fr-FR");

    public CultureInfo Load() => _culture;

    public void Save(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _culture = culture;
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

    public void Save(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        _configuration = _configuration with { UiCulture = culture.Name };
        _configurationService.SaveAsync(_configuration).GetAwaiter().GetResult();
    }
}

public sealed record LanguageOption(string CultureName, string DisplayName);

public sealed class ShellViewModel : INotifyPropertyChanged
{
    private static readonly ResourceManager ResourceManager = new("Sushi81.Pos.Desktop.Properties.Resources", typeof(ShellViewModel).Assembly);
    private readonly ISelectedCultureStore _cultureStore;
    private CultureInfo _culture;
    private LanguageOption _selectedLanguage;

    public ShellViewModel(ISelectedCultureStore cultureStore, bool startupSucceeded)
    {
        _cultureStore = cultureStore ?? throw new ArgumentNullException(nameof(cultureStore));
        _culture = Normalize(_cultureStore.Load());
        StartupSucceeded = startupSucceeded;
        Languages = new ObservableCollection<LanguageOption>();
        RefreshResources();
        _selectedLanguage = Languages.Single(option => option.CultureName == _culture.Name);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LanguageOption> Languages { get; }

    public bool StartupSucceeded { get; }

    public string Title { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string LanguageLabel { get; private set; } = string.Empty;

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_selectedLanguage == value)
            {
                return;
            }

            _culture = Normalize(CultureInfo.GetCultureInfo(value.CultureName));
            _cultureStore.Save(_culture);
            RefreshResources();
            _selectedLanguage = Languages.Single(option => option.CultureName == _culture.Name);
            OnPropertyChanged();
        }
    }

    private void RefreshResources()
    {
        Title = Read("ShellTitle");
        Status = Read(StartupSucceeded ? "FoundationReady" : "StartupFailure");
        LanguageLabel = Read("LanguageLabel");
        Languages.Clear();
        Languages.Add(new LanguageOption("fr-FR", Read("FrenchLanguage")));
        Languages.Add(new LanguageOption("zh-CN", Read("ChineseLanguage")));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(LanguageLabel));
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
