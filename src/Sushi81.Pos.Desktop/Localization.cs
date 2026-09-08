using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Foundation.Configuration;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.GitHubTransport;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.Configuration;

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

    public void ReplaceConfiguration(LocalConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }
}

public sealed record LanguageOption(string CultureName, string DisplayName);

public sealed class ShellViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly ResourceManager ResourceManager = new("Sushi81.Pos.Desktop.Properties.Resources", typeof(ShellViewModel).Assembly);
    private readonly ISelectedCultureStore _cultureStore;
    private CultureInfo _culture;
    private LanguageOption _selectedLanguage;
    private bool _isLanguageChangeInProgress;
    private bool _m07OperationInProgress;
    private string? _m07OperationStatusKey;
    private LocalConfiguration _configuration;
    private readonly M07ConfigurationSetupService? _m07Setup;
    private AuthorityPhase? _authorityPhase;

    public ShellViewModel(ISelectedCultureStore cultureStore, bool startupSucceeded, CatalogueService? catalogueService = null, BusinessSettingsService? settingsService = null, OrderEntryService? orderEntryService = null, OrderLifecycleService? orderLifecycleService = null, IWriteAuthorityGuard? authorityGuard = null, WriteAuthorityState authorityState = WriteAuthorityState.Authoritative, M07RuntimeServices? m07Runtime = null, LocalConfiguration? configuration = null, M07ConfigurationSetupService? m07Setup = null, AuthorityPhase? authorityPhase = null)
    {
        _cultureStore = cultureStore ?? throw new ArgumentNullException(nameof(cultureStore));
        _configuration = configuration ?? new LocalConfiguration();
        _m07Setup = m07Setup;
        _culture = Normalize(_cultureStore.Load());
        StartupSucceeded = startupSucceeded;
        AuthorityState = authorityGuard?.State ?? authorityState;
        M07Runtime = m07Runtime;
        _authorityPhase = authorityPhase;
        Languages = new ObservableCollection<LanguageOption>();
        RecoveryCandidates = new ObservableCollection<RecoveryCandidate>();
        RefreshResources();
        _selectedLanguage = Languages.Single(option => option.CultureName == _culture.Name);
        Admin = startupSucceeded && catalogueService is not null && settingsService is not null ? new M03ShellViewModel(catalogueService, settingsService, authorityGuard) : null;
        Entry = startupSucceeded && orderEntryService is not null ? new OrderEntryShellViewModel(orderEntryService, authorityGuard) : null;
        Lifecycle = startupSucceeded && orderLifecycleService is not null ? new OrderLifecycleShellViewModel(orderLifecycleService, authorityGuard) : null;
        Admin?.ApplyLocalization(Localized["All"], Localized["Active"], Localized["Inactive"], Localized["Activate"], Localized["Deactivate"]);
        Entry?.ApplyLocalization(Localized["All"], Localized["FulfilmentUnselected"], Localized["Retrait"], Localized["Livraison"], Localized["ManualTotalActive"], Localized["NewOrder"], Localized["Quantity"], Localized);
        Lifecycle?.ApplyLocalization(Localized);
        if (Admin is not null) Admin.SettingsSaved += OnSettingsSaved;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<LanguageOption> Languages { get; }

    public bool StartupSucceeded { get; }

    public WriteAuthorityState AuthorityState { get; private set; }

    public M07RuntimeServices? M07Runtime { get; }

    public ObservableCollection<RecoveryCandidate> RecoveryCandidates { get; }

    public RecoveryCandidate? RecommendedRecoveryCandidate { get; private set; }

    public LocalConfiguration Configuration => _configuration;

    public bool CanWrite => StartupSucceeded && AuthorityState == WriteAuthorityState.Authoritative;

    public bool IsAuthorityWarningVisible => !CanWrite;

    public bool CanJoinExistingLineage => M07Runtime is not null
        && M07Runtime.CanSelfJoin;

    public bool CanAcquireTransferredAuthority => M07Runtime is not null
        && !CanWrite
        && M07Runtime.CurrentPhase is not (AuthorityPhase.TransferPreparing or AuthorityPhase.RelinquishedPendingGrant
            or AuthorityPhase.DisasterRecoveryPreparing or AuthorityPhase.DisasterRecoveryPending
            or AuthorityPhase.StaleGeneration)
        && !_m07OperationInProgress;

    public bool CanResumePendingTransfer => M07Runtime?.NormalHandoff is not null
        && M07Runtime.CurrentPhase is AuthorityPhase.TransferPreparing or AuthorityPhase.RelinquishedPendingGrant
        && !_m07OperationInProgress;

    public bool CanTestGitHubConnection => M07Runtime is not null && !_m07OperationInProgress;

    public bool CanConfigureM07 => _m07Setup is not null
        && !_m07OperationInProgress
        && !IsConfigurationLocked(CurrentAuthorityPhase);

    public bool CanStartDisasterRecovery => M07Runtime?.DisasterRecovery is not null
        && !CanWrite
        && !_m07OperationInProgress
        && CurrentAuthorityPhase is AuthorityPhase.PairedUninitializedReadOnly
            or AuthorityPhase.NonAuthoritativeReadOnly
            or AuthorityPhase.ReleasedNonAuthoritative
            or AuthorityPhase.RelinquishedPendingGrant;

    public bool CanRetryDisasterRecovery => M07Runtime?.DisasterRecovery is not null
        && !_m07OperationInProgress
        && CurrentAuthorityPhase is AuthorityPhase.DisasterRecoveryPreparing or AuthorityPhase.DisasterRecoveryPending;

    public bool CanReinitializeStaleDevice => M07Runtime?.DisasterRecovery is not null
        && !_m07OperationInProgress
        && CurrentAuthorityPhase == AuthorityPhase.StaleGeneration;

    public M03ShellViewModel? Admin { get; }

    public OrderEntryShellViewModel? Entry { get; }

    public OrderLifecycleShellViewModel? Lifecycle { get; }

    public bool IsM03Available => Admin is not null;

    public bool IsM04Available => Entry is not null;

    public bool IsM05Available => Lifecycle is not null;

    public string Title { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public string AuthorityStatus { get; private set; } = string.Empty;

    public string M07OperationStatus { get; private set; } = string.Empty;

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
            Entry?.ApplyLocalization(Localized["All"], Localized["FulfilmentUnselected"], Localized["Retrait"], Localized["Livraison"], Localized["ManualTotalActive"], Localized["NewOrder"], Localized["Quantity"], Localized);
            Lifecycle?.ApplyLocalization(Localized);
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

    public void Dispose()
    {
        if (Admin is not null) Admin.SettingsSaved -= OnSettingsSaved;
        Entry?.Dispose();
        Lifecycle?.Dispose();
    }

    private void OnSettingsSaved(object? sender, BusinessSettingsSavedEventArgs e)
    {
        // A committed/reloaded order is a historical snapshot. Later settings saves may
        // update the maintenance screen, but must never recompute that committed state.
        Entry?.ApplySettingsSaved(e.Previous, e.Current);
    }

    private void RefreshResources()
    {
        Title = Read("ShellTitle");
        Status = Read(StartupSucceeded ? "FoundationReady" : "StartupFailure");
        AuthorityStatus = Read(AuthorityState switch
        {
            WriteAuthorityState.NonAuthoritativeReadOnly => "AuthorityReadOnly",
            WriteAuthorityState.Transitioning => "AuthorityTransitioning",
            WriteAuthorityState.RecoveryRequired => "AuthorityRecoveryRequired",
            WriteAuthorityState.Uninitialized => "AuthorityRecoveryRequired",
            _ => "AuthorityReadOnly"
        });
        M07OperationStatus = _m07OperationStatusKey is not null
            ? Read(_m07OperationStatusKey)
            : M07Runtime?.CurrentPhase is AuthorityPhase.TransferPreparing or AuthorityPhase.RelinquishedPendingGrant
                ? Read("M07PendingTransfer")
                : M07Runtime?.CurrentPhase is AuthorityPhase.DisasterRecoveryPreparing or AuthorityPhase.DisasterRecoveryPending
                    ? Read("M07DisasterRecoveryPending")
                    : M07Runtime?.CurrentPhase == AuthorityPhase.StaleGeneration
                        ? Read("M07StaleGeneration")
                        : string.Empty;
        LanguageLabel = Read("LanguageLabel");
        LanguageSaveFailure = Read("LanguageSaveFailure");
        Languages.Clear();
        Languages.Add(new LanguageOption("fr-FR", Read("FrenchLanguage")));
        Languages.Add(new LanguageOption("zh-CN", Read("ChineseLanguage")));
        var keys = new[] { "ShellTitle", "Catalogue", "Settings", "Caisse", "Commandes", "Products", "Search", "OrderSearch", "Category", "All", "Active", "Inactive", "NewProduct", "Edit", "Save", "Cancel", "Add", "Confirm", "ReloadOrder", "Activate", "Deactivate", "BulkActivate", "BulkDeactivate", "BulkConfirm", "BulkNoChange", "BulkSuccess", "DeletePermanently", "ManageCategories", "CategoryShortCode", "CategoryShortCodeTooltip", "Code", "Name", "PriceTtc", "Vat", "DiscountEligible", "OptionsEnabled", "OptionGroups", "Options", "SelectionMode", "Required", "Optional", "Single", "Multi", "Minimum", "Maximum", "AdjustmentTtc", "MoveUp", "MoveDown", "PickupDiscount", "PickupMinimum", "DeliveryMinimum", "DeliveryFee", "DeliveryFeeEnabled", "Fulfilment", "FulfilmentUnselected", "Retrait", "Livraison", "PlannedDate", "PlannedTime", "TimeHour", "TimeMinute", "TimeUnset", "Telephone", "DeliveryAddress", "Comment", "PickupDiscountRequest", "Cart", "TotalTtc", "Quantity", "CustomAdjustments", "AddAdjustment", "AdjustmentLabel", "AdjustmentAmount", "NewOrder", "ManualTotalActive", "ReloadOrderTooltip", "ReloadedOrder", "OrderBrowser", "BrowseDate", "Browse", "OrderBrowserTime", "OrderBrowserMode", "OrderBrowserStatus", "OrderBrowserTotal", "OrderBrowserTelephone", "OrderBrowserEmptyTelephone", "OrderId", "OrderStatus", "OrderStatusOpen", "OrderStatusClosed", "OrderStatusCancelled", "OrderLines", "Unit", "PickupDiscountApplied", "TaxSnapshot", "InvalidOrderId", "OrderNotFound", "OrderSaved", "OrderSavedOutputFailed", "ProductInactive", "InvalidPlannedTime", "InvalidManualTotal", "ValidationFulfilmentRequired", "ValidationPlannedDateRequired", "ValidationPlannedDatePast", "ValidationPlannedTimeRequired", "ValidationPlannedTimeInvalid", "ValidationCartRequired", "ValidationDeliveryMinimum", "ValidationPickupDiscount", "InvalidQuantity", "InvalidOptions", "InvalidAdjustment", "EmptyCatalogue", "DeleteConfirm", "M03StartupFailure", "DeliveryFeeVatFixed", "CreateCategory", "CreateCategoryFirst", "RenameCategory", "Close", "EnterValidValues", "Saved", "ValidationGeneric", "ValidationAuthorityBlocked", "ValidationRequired", "ValidationCategoryDuplicate", "ValidationCategoryShortCodeDuplicate", "ValidationCategoryShortCodeTooLong", "ValidationCategoryMissing", "ValidationProductMissing", "ValidationProductDuplicateCode", "ValidationPriceNegative", "ValidationVatRange", "ValidationRequiredChoices", "ValidationSettingsRange", "ValidationInvalidNumber", "ValidationBusy", "ValidationGroupStructure", "ValidationOptionStructure", "ValidationConflict", "ValidationField", "CategoryEdit", "CategoryCreateSave", "CategoryRenameSave", "CategoryEditCancel", "DirtyEditorClose", "Discard", "KeepEditing", "OptionName", "OptionActive", "OrderReference", "OrderCard", "OrderCash", "OrderPaid", "OrderDifference", "OrderSearchLive", "OrderModify", "OrderAbandon", "OrderCancel", "OrderNewFromDetails", "OrderClose", "OrderEffectiveDate", "OrderEffectiveDateEdit", "OrderEffectiveDateHint", "OrderBrowseByDate", "OrderSave", "OrderReadOnly", "OrderEdit", "OrderAdvance", "OrderSearchHint", "OrderNoSelection", "DashboardTurnover", "DashboardReceived", "DashboardReceivedCard", "DashboardReceivedCash", "DashboardFuture", "DashboardDueToday", "DashboardOverdue", "DashboardRefresh", "AuthorityReadOnly", "AuthorityTransitioning", "AuthorityRecoveryRequired" };
        keys = keys.Append("ValidationPaymentNegative").Append("OrderCloseEligible")
            .Append("AuthorityCloseTitle").Append("AuthorityClosePrompt").Append("AuthorityTargetLabel")
            .Append("AuthorityCloseRetain").Append("AuthorityTransferClose").Append("AuthorityCloseCancel")
            .Append("AuthorityTargetRequired").Append("AuthorityTransferFailed")
            .Append("JoinExistingLineage").Append("JoinDisplayName").Append("JoinPrompt")
            .Append("JoinConfirm").Append("JoinSucceeded")
            .Append("M07AcquireAuthority").Append("M07AcquireValidating").Append("M07AcquireAcquired")
            .Append("M07AcquireUnavailable").Append("M07AcquireFailed")
            .Append("M07ResumeTransfer").Append("M07ResumeValidating").Append("M07ResumeSucceeded")
            .Append("M07ResumeFailed").Append("M07PendingTransfer")
            .Append("M07ConnectionTest").Append("M07ConnectionChecking").Append("M07ConnectionSuccess")
            .Append("M07ConnectionNotConfigured").Append("M07ConnectionCredentialMissing")
            .Append("M07ConnectionUnauthorized").Append("M07ConnectionForbidden")
             .Append("M07ConnectionNotFound").Append("M07ConnectionFailed")
             .Append("AuthorityTargetTitle").Append("AuthorityTargetPrompt").Append("AuthorityTargetConfirm")
             .Append("AuthorityTransferUnavailable")
             .Append("M07Setup").Append("M07SetupTitle").Append("M07SetupPrompt")
             .Append("M07SetupOneDriveRoot").Append("M07SetupBrowse")
             .Append("M07SetupGitHubOwner").Append("M07SetupGitHubRepository")
             .Append("M07SetupGitHubReleaseTag").Append("M07SetupGitHubReleaseName")
             .Append("M07SetupGitHubCredentialTarget").Append("M07SetupGitHubHelp")
             .Append("M07SetupSave").Append("M07SetupRestartRequired")
             .Append("M07SetupRootRequired").Append("M07SetupRootAbsolute")
             .Append("M07SetupRootUnavailable").Append("M07SetupLineageUnavailable")
             .Append("M07SetupLineageInvalid").Append("M07SetupLineageRequired")
             .Append("M07SetupPhaseLocked").Append("M07SetupAuthorityStateUnavailable")
             .Append("M07SetupPersistenceFailed")
             .Append("M07DisasterRecovery").Append("M07DisasterRecoveryPending")
             .Append("M07DisasterRecoveryNoCandidate").Append("M07DisasterRecoveryFailed")
             .Append("M07DisasterRecoverySucceeded").Append("M07StaleGeneration")
             .Append("M07ReinitializeStale").Append("M07ReinitializeNoSeed")
             .Append("M07ReinitializeSucceeded").Append("M07CandidateTypeGitHub")
             .Append("M07CandidateTypeOneDrive").Append("M07CandidateDataLossWarning")
             .Append("M07QuarantineWarning").Append("M07QuarantineConfirm").Append("M07ConfirmRecovery")
             .Append("M07CandidateRevision").Append("M07CandidateHandoffVersion").Append("M07CandidateSource")
             .Append("M07CandidateTimestamp").ToArray();
        Localized = keys.ToDictionary(key => key, Read, StringComparer.Ordinal);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(AuthorityStatus));
        OnPropertyChanged(nameof(CanWrite));
        OnPropertyChanged(nameof(IsAuthorityWarningVisible));
        OnPropertyChanged(nameof(CanJoinExistingLineage));
        OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
        OnPropertyChanged(nameof(CanResumePendingTransfer));
        OnPropertyChanged(nameof(CanTestGitHubConnection));
        OnPropertyChanged(nameof(CanConfigureM07));
        OnPropertyChanged(nameof(CanStartDisasterRecovery));
        OnPropertyChanged(nameof(CanRetryDisasterRecovery));
        OnPropertyChanged(nameof(CanReinitializeStaleDevice));
        OnPropertyChanged(nameof(M07OperationStatus));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(LanguageSaveFailure));
        OnPropertyChanged(nameof(Localized));
    }

    public async Task<DeviceSelfJoinResult> JoinExistingLineageAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (M07Runtime is null)
            throw new InvalidOperationException("M07 pairing services are not configured.");

        var result = await M07Runtime.SelfJoin.JoinAsync(displayName, cancellationToken);
        AuthorityState = M07Runtime.AuthorityGuard.State;
        await M07Runtime.RefreshAuthorityStateAsync(cancellationToken);
        RefreshChildAuthorityCommands();
        RefreshResources();
        return result;
    }

    public async Task<M07ConfigurationSetupResult?> ConfigureM07Async(
        M07ConfigurationSetupInput input,
        CancellationToken cancellationToken = default)
    {
        if (_m07Setup is null)
            return null;

        if (_m07OperationInProgress)
            return M07ConfigurationSetupResult.Failure(_configuration, M07ConfigurationSetupFailureKind.PersistenceFailed, "Another M07 operation is already in progress.");

        if (!CanConfigureM07)
        {
            var blocked = M07ConfigurationSetupResult.Failure(
                _configuration,
                M07ConfigurationSetupFailureKind.AuthorityPhaseUnsafe,
                "Technical configuration is unavailable during the current authority phase.");
            SetM07Operation("M07SetupPhaseLocked");
            RefreshResources();
            return blocked;
        }

        _m07OperationInProgress = true;
        OnPropertyChanged(nameof(CanConfigureM07));
        try
        {
            var result = await _m07Setup.ValidateAndPersistAsync(_configuration, input, cancellationToken);
            if (result.Succeeded)
            {
                _configuration = result.Configuration;
                if (_cultureStore is ConfigurationSelectedCultureStore configurationStore)
                    configurationStore.ReplaceConfiguration(result.Configuration);
                SetM07Operation("M07SetupRestartRequired");
            }
            else
            {
                SetM07Operation(result.FailureKind switch
                {
                    M07ConfigurationSetupFailureKind.RootRequired => "M07SetupRootRequired",
                    M07ConfigurationSetupFailureKind.RootNotAbsolute => "M07SetupRootAbsolute",
                    M07ConfigurationSetupFailureKind.RootUnavailable => "M07SetupRootUnavailable",
                    M07ConfigurationSetupFailureKind.LineageUnavailable => "M07SetupLineageUnavailable",
                    M07ConfigurationSetupFailureKind.LineageInvalid => "M07SetupLineageInvalid",
                    M07ConfigurationSetupFailureKind.LineageRequired => "M07SetupLineageRequired",
                    M07ConfigurationSetupFailureKind.AuthorityPhaseUnsafe => "M07SetupPhaseLocked",
                    M07ConfigurationSetupFailureKind.AuthorityStateUnavailable => "M07SetupAuthorityStateUnavailable",
                    M07ConfigurationSetupFailureKind.PersistenceFailed => "M07SetupPersistenceFailed",
                    _ => "M07SetupPersistenceFailed"
                });
            }

            RefreshResources();
            return result;
        }
        finally
        {
            _m07OperationInProgress = false;
            OnPropertyChanged(nameof(CanConfigureM07));
            OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
            OnPropertyChanged(nameof(CanResumePendingTransfer));
            OnPropertyChanged(nameof(CanTestGitHubConnection));
        }
    }

    public async Task<TargetAcquisitionResult?> AcquireTransferredAuthorityAsync(CancellationToken cancellationToken = default)
    {
        if (M07Runtime is null)
            return null;

        SetM07Operation("M07AcquireValidating");
        _m07OperationInProgress = true;
        OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
        OnPropertyChanged(nameof(CanTestGitHubConnection));
        OnPropertyChanged(nameof(CanConfigureM07));
        try
        {
            if (M07Runtime.TargetAcquisition is not { } acquisition)
            {
                SetM07Operation("M07AcquireUnavailable");
                return TargetAcquisitionResult.Failure(Guid.Empty, new InvalidOperationException("Target acquisition is not configured."));
            }

            var result = await acquisition.AcquireAsync(cancellationToken);
            AuthorityState = M07Runtime.AuthorityGuard.State;
            await M07Runtime.RefreshAuthorityStateAsync(cancellationToken);
            SetM07Operation(result.Succeeded ? "M07AcquireAcquired" : "M07AcquireFailed");
            RefreshChildAuthorityCommands();
            RefreshResources();
            return result;
        }
        finally
        {
            _m07OperationInProgress = false;
            OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
            OnPropertyChanged(nameof(CanResumePendingTransfer));
            OnPropertyChanged(nameof(CanTestGitHubConnection));
            OnPropertyChanged(nameof(CanConfigureM07));
        }
    }

    public async Task<NormalHandoffResult?> ResumePendingTransferAsync(CancellationToken cancellationToken = default)
    {
        if (M07Runtime?.NormalHandoff is not { } handoff)
            return null;

        SetM07Operation("M07ResumeValidating");
        _m07OperationInProgress = true;
        OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
        OnPropertyChanged(nameof(CanResumePendingTransfer));
        OnPropertyChanged(nameof(CanTestGitHubConnection));
        try
        {
            var result = await handoff.ResumePendingTransferAsync(cancellationToken);
            AuthorityState = M07Runtime.AuthorityGuard.State;
            await M07Runtime.RefreshAuthorityStateAsync(cancellationToken);
            SetM07Operation(result.Succeeded ? "M07ResumeSucceeded" : "M07ResumeFailed");
            RefreshChildAuthorityCommands();
            RefreshResources();
            return result;
        }
        finally
        {
            _m07OperationInProgress = false;
            OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
            OnPropertyChanged(nameof(CanResumePendingTransfer));
            OnPropertyChanged(nameof(CanTestGitHubConnection));
        }
    }

    public async Task RefreshAuthorityStateAsync(CancellationToken cancellationToken = default)
    {
        if (M07Runtime is null) return;
        await M07Runtime.RefreshAuthorityStateAsync(cancellationToken);
        AuthorityState = M07Runtime.AuthorityGuard.State;
        _authorityPhase = M07Runtime.CurrentPhase;
        RefreshChildAuthorityCommands();
        RefreshResources();
    }

    public async Task<RecoveryCandidateDiscoveryResult?> DiscoverRecoveryCandidatesAsync(CancellationToken cancellationToken = default)
    {
        if (M07Runtime?.DisasterRecovery is not { } recovery) return null;
        SetM07Operation("M07DisasterRecovery");
        _m07OperationInProgress = true;
        RefreshResources();
        try
        {
            var result = await recovery.DiscoverCandidatesAsync(cancellationToken);
            RecoveryCandidates.Clear();
            foreach (var candidate in result.Candidates) RecoveryCandidates.Add(candidate);
            RecommendedRecoveryCandidate = result.Recommended;
            SetM07Operation(result.Recommended is null ? "M07DisasterRecoveryNoCandidate" : "M07DisasterRecovery");
            return result;
        }
        finally
        {
            _m07OperationInProgress = false;
            RefreshResources();
        }
    }

    public async Task<DisasterRecoveryResult?> StartDisasterRecoveryAsync(
        string candidateId,
        bool quarantineConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (M07Runtime?.DisasterRecovery is not { } recovery) return null;
        _m07OperationInProgress = true;
        SetM07Operation("M07DisasterRecovery");
        RefreshResources();
        try
        {
            var result = await recovery.StartOrResumeAsync(candidateId, quarantineConfirmed, cancellationToken: cancellationToken);
            await RefreshAuthorityStateAsync(cancellationToken);
            SetM07Operation(result.Succeeded ? "M07DisasterRecoverySucceeded" : "M07DisasterRecoveryFailed");
            return result;
        }
        finally
        {
            _m07OperationInProgress = false;
            RefreshResources();
        }
    }

    public async Task<DisasterRecoveryResult?> RetryDisasterRecoveryAsync(bool quarantineConfirmed, CancellationToken cancellationToken = default)
    {
        if (M07Runtime?.DisasterRecovery is not { } recovery) return null;
        _m07OperationInProgress = true;
        SetM07Operation("M07DisasterRecoveryPending");
        RefreshResources();
        try
        {
            var result = await recovery.RetryAsync(quarantineConfirmed, cancellationToken);
            await RefreshAuthorityStateAsync(cancellationToken);
            SetM07Operation(result.Succeeded ? "M07DisasterRecoverySucceeded" : "M07DisasterRecoveryFailed");
            return result;
        }
        finally
        {
            _m07OperationInProgress = false;
            RefreshResources();
        }
    }

    public async Task<DisasterRecoveryResult?> ReinitializeStaleDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (M07Runtime?.DisasterRecovery is not { } recovery) return null;
        _m07OperationInProgress = true;
        SetM07Operation("M07ReinitializeStale");
        RefreshResources();
        try
        {
            var result = await recovery.ReinitializeStaleDeviceAsync(cancellationToken);
            await RefreshAuthorityStateAsync(cancellationToken);
            SetM07Operation(result.Succeeded ? "M07ReinitializeSucceeded" : "M07ReinitializeNoSeed");
            return result;
        }
        finally
        {
            _m07OperationInProgress = false;
            RefreshResources();
        }
    }

    public async Task<GitHubConnectionTestResult?> TestGitHubConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (M07Runtime is null)
            return null;

        SetM07Operation("M07ConnectionChecking");
        _m07OperationInProgress = true;
        OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
        OnPropertyChanged(nameof(CanTestGitHubConnection));
        try
        {
            if (M07Runtime.ConnectionSetup == GitHubConnectionSetupState.RepositoryNotConfigured)
            {
                var result = new GitHubConnectionTestResult(false, null, null, GitHubConnectionFailureKind.NotConfigured);
                SetM07Operation("M07ConnectionNotConfigured");
                return result;
            }

            if (M07Runtime.ConnectionSetup == GitHubConnectionSetupState.CredentialNotConfigured || M07Runtime.ConnectionTester is null)
            {
                var result = new GitHubConnectionTestResult(false, null, null, GitHubConnectionFailureKind.CredentialMissing);
                SetM07Operation("M07ConnectionCredentialMissing");
                return result;
            }

            var tested = await M07Runtime.ConnectionTester.TestAsync(cancellationToken);
            SetM07Operation(tested.Succeeded ? "M07ConnectionSuccess" : tested.FailureKind switch
            {
                GitHubConnectionFailureKind.CredentialMissing => "M07ConnectionCredentialMissing",
                GitHubConnectionFailureKind.Unauthorized => "M07ConnectionUnauthorized",
                GitHubConnectionFailureKind.Forbidden => "M07ConnectionForbidden",
                GitHubConnectionFailureKind.NotFound => "M07ConnectionNotFound",
                _ => "M07ConnectionFailed"
            });
            return tested;
        }
        finally
        {
            _m07OperationInProgress = false;
            OnPropertyChanged(nameof(CanAcquireTransferredAuthority));
            OnPropertyChanged(nameof(CanTestGitHubConnection));
            OnPropertyChanged(nameof(CanConfigureM07));
        }
    }

    private void SetM07Operation(string key)
    {
        _m07OperationStatusKey = key;
        M07OperationStatus = Read(key);
        OnPropertyChanged(nameof(M07OperationStatus));
    }

    private void RefreshChildAuthorityCommands()
    {
        Admin?.RefreshAuthorityState();
        Entry?.RefreshAuthorityState();
        Lifecycle?.RefreshAuthorityState();
    }

    private AuthorityPhase? CurrentAuthorityPhase => M07Runtime?.CurrentPhase ?? _authorityPhase;

    private static bool IsConfigurationLocked(AuthorityPhase? phase) => phase is
        AuthorityPhase.TransferPreparing
        or AuthorityPhase.RelinquishedPendingGrant
        or AuthorityPhase.TargetAcquisitionPending
        or AuthorityPhase.DisasterRecoveryPreparing
        or AuthorityPhase.DisasterRecoveryPending
        or AuthorityPhase.RecoveryRequired
        or AuthorityPhase.StaleGeneration;

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
