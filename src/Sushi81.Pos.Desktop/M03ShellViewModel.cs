using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Settings;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

/// <summary>Small testable presentation seam for M03 maintenance workflows.</summary>
public sealed class M03ShellViewModel : INotifyPropertyChanged
{
    private readonly CatalogueService catalogue;
    private readonly BusinessSettingsService settings;
    private ProductSummary? selectedProduct;
    private CategorySummary? selectedCategory;
    private Guid selectedCategoryId;
    private string searchText = string.Empty;
    private string activeFilter = "All";
    private bool isBusy;
    private BusinessSettings? loadedSettings;
    private string activateLabel = "Activate";
    private string deactivateLabel = "Deactivate";
    private string settingsValidationMessage = string.Empty;

    public M03ShellViewModel(CatalogueService catalogue, BusinessSettingsService settings)
    {
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Categories = new ObservableCollection<CategorySummary>();
        Products = new ObservableCollection<ProductSummary>();
        CategoryFilters = new ObservableCollection<CategorySummary>();
        CategoryFilters.Add(new CategorySummary(Guid.Empty, AllCategoryLabel));
        selectedCategory = CategoryFilters[0];
        selectedCategoryId = Guid.Empty;
        StatusFilters = new ObservableCollection<FilterOption>
        {
            new("All", "All"),
            new("Active", "Active"),
            new("Inactive", "Inactive")
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CategorySummary> Categories { get; }
    public ObservableCollection<CategorySummary> CategoryFilters { get; }
    public ObservableCollection<ProductSummary> Products { get; }
    public ObservableCollection<FilterOption> StatusFilters { get; }

    public string AllCategoryLabel { get; private set; } = "All";

    public sealed record FilterOption(string Key, string Label);

    public ProductSummary? SelectedProduct { get => selectedProduct; set { selectedProduct = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditProduct)); OnPropertyChanged(nameof(CanDeleteProduct)); OnPropertyChanged(nameof(CanToggleProduct)); OnPropertyChanged(nameof(ToggleProductActionLabel)); } }
    public CategorySummary? SelectedCategory
    {
        get => selectedCategory;
        set
        {
            // WPF can transiently write null while ItemsSource is rebuilt. Preserve the
            // semantic selection and restore it after the collection has been rebuilt.
            if (value is null && CategoryFilters.Count == 0) return;
            var next = value ?? CategoryFilters.FirstOrDefault(category => category.Id == Guid.Empty);
            selectedCategory = next;
            if (next is not null) selectedCategoryId = next.Id;
            OnPropertyChanged(nameof(SelectedCategoryId));
            OnPropertyChanged();
        }
    }
    /// <summary>
    /// Stable semantic key for the category filter ComboBox.
    ///
    /// The WPF view binds SelectedValue to this property with SelectedValuePath="Id".
    /// The selected category object is replaced whenever localization or refresh rebuilds
    /// the filter collection, so object-instance SelectedItem identity is not a reliable
    /// binding contract. Guid.Empty is the localized All item and is always the fallback.
    /// </summary>
    public Guid? SelectedCategoryId
    {
        get => selectedCategoryId;
        set
        {
            // WPF can transiently write null while ItemsSource is being replaced. Keep the
            // semantic key until the rebuilt collection is ready, then restore it from the
            // collection (or deterministically fall back to All).
            if (value is null && CategoryFilters.Count == 0) return;
            var requestedId = value ?? Guid.Empty;
            var next = CategoryFilters.FirstOrDefault(category => category.Id == requestedId)
                ?? CategoryFilters.FirstOrDefault(category => category.Id == Guid.Empty);
            selectedCategory = next;
            selectedCategoryId = next?.Id ?? requestedId;
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged();
        }
    }
    public string SearchText { get => searchText; set { searchText = value ?? string.Empty; OnPropertyChanged(); } }
    public string ActiveFilter
    {
        get => activeFilter;
        set => SetActiveFilter(value);
    }
    /// <summary>
    /// Stable semantic key for the status filter ComboBox.
    ///
    /// MainWindow binds SelectedValue to this property with SelectedValuePath="Key".
    /// The status options are replaced during localization, so the binding must preserve
    /// the key while WPF transiently clears selection during collection replacement.
    /// </summary>
    public string? SelectedStatusKey
    {
        get => activeFilter;
        set
        {
            // Keep the effective key while WPF is between StatusFilters.Clear() and the
            // rebuilt localized options. Once options exist, null deterministically means All.
            if (value is null && StatusFilters.Count == 0) return;
            SetActiveFilter(value);
        }
    }
    public bool CanEditProduct => SelectedProduct is not null && !IsBusy;
    public bool CanDeleteProduct => SelectedProduct is not null && !IsBusy;
    public bool CanToggleProduct => SelectedProduct is not null && !IsBusy;
    public string ToggleProductActionLabel => SelectedProduct?.IsActive == true ? deactivateLabel : activateLabel;
    public bool HasProducts => Products.Count > 0;
    public bool IsBusy { get => isBusy; private set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditProduct)); OnPropertyChanged(nameof(CanDeleteProduct)); OnPropertyChanged(nameof(CanToggleProduct)); OnPropertyChanged(nameof(ToggleProductActionLabel)); } }

    public string PickupDiscountRateText { get; set; } = "10";
    public string PickupDiscountMinText { get; set; } = "15.00";
    public string DeliveryMinText { get; set; } = "30.00";
    public bool DeliveryFeeEnabled { get; set; }
    public string DeliveryFeeAmountText { get; set; } = "0.00";
    public string SettingsValidationMessage { get => settingsValidationMessage; private set { settingsValidationMessage = value ?? string.Empty; OnPropertyChanged(); } }

    public void SetSettingsValidationMessage(string? message) => SettingsValidationMessage = message ?? string.Empty;

    public void ApplyLocalization(string all, string active, string inactive, string? activate = null, string? deactivate = null)
    {
        var categoryId = selectedCategoryId;
        var statusKey = ActiveFilter;
        activateLabel = string.IsNullOrWhiteSpace(activate) ? "Activate" : activate;
        deactivateLabel = string.IsNullOrWhiteSpace(deactivate) ? "Deactivate" : deactivate;
        AllCategoryLabel = string.IsNullOrWhiteSpace(all) ? "All" : all;
        StatusFilters.Clear();
        StatusFilters.Add(new("All", string.IsNullOrWhiteSpace(all) ? "All" : all));
        StatusFilters.Add(new("Active", string.IsNullOrWhiteSpace(active) ? "Active" : active));
        StatusFilters.Add(new("Inactive", string.IsNullOrWhiteSpace(inactive) ? "Inactive" : inactive));
        ActiveFilter = statusKey;
        if (CategoryFilters.Count > 0)
        {
            CategoryFilters[0] = new CategorySummary(Guid.Empty, AllCategoryLabel);
            RestoreCategorySelection(categoryId);
        }
        OnPropertyChanged(nameof(AllCategoryLabel));
        OnPropertyChanged(nameof(StatusFilters));
        OnPropertyChanged(nameof(ToggleProductActionLabel));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var categoryId = selectedCategoryId;
            var statusKey = ActiveFilter;
            var categories = await catalogue.ListCategoriesAsync(cancellationToken);
            Categories.Clear();
            CategoryFilters.Clear();
            CategoryFilters.Add(new CategorySummary(Guid.Empty, AllCategoryLabel));
            foreach (var category in categories) { Categories.Add(category); CategoryFilters.Add(category); }
            RestoreCategorySelection(categoryId);
            ActiveFilter = statusKey;
            var active = ActiveFilter switch { "Active" => true, "Inactive" => false, _ => (bool?)null };
            Guid? selectedId = SelectedCategory is null || SelectedCategory.Id == Guid.Empty ? null : SelectedCategory.Id;
            var products = await catalogue.ListProductsAsync(SearchText, selectedId, active, cancellationToken);
            Products.Clear(); foreach (var product in products) Products.Add(product);
            OnPropertyChanged(nameof(HasProducts));
            if (SelectedProduct is not null) SelectedProduct = Products.FirstOrDefault(product => product.Id == SelectedProduct.Id);
        }
        finally { IsBusy = false; }
    }

    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        loadedSettings = await settings.GetAsync(cancellationToken);
        PickupDiscountRateText = (loadedSettings.PickupDiscountRate * 100m).ToString("0.#############################", CultureInfo.InvariantCulture);
        PickupDiscountMinText = loadedSettings.PickupDiscountMinTotalTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture);
        DeliveryMinText = loadedSettings.DeliveryMinMerchandiseTotalTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture);
        DeliveryFeeEnabled = loadedSettings.DeliveryFeeEnabled;
        DeliveryFeeAmountText = loadedSettings.DeliveryFeeAmountTtc.Euros.ToString("0.00", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(PickupDiscountRateText)); OnPropertyChanged(nameof(PickupDiscountMinText)); OnPropertyChanged(nameof(DeliveryMinText)); OnPropertyChanged(nameof(DeliveryFeeEnabled)); OnPropertyChanged(nameof(DeliveryFeeAmountText));
    }

    public async Task<OperationResult> SaveSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return OperationResult.Failure(new ValidationIssue("settings", "A settings save is already in progress."));
        IsBusy = true;
        try
        {
            var issues = new List<ValidationIssue>();
            var percentOk = M03Presentation.TryParseDecimal(PickupDiscountRateText, "pickup-rate", out var percent, out var percentIssue);
            if (!percentOk && percentIssue is not null) issues.Add(percentIssue);
            var pickupOk = M03Presentation.TryParseMoney(PickupDiscountMinText, "pickup-minimum", out var pickupMoney, out var pickupIssue);
            if (!pickupOk && pickupIssue is not null) issues.Add(pickupIssue);
            var deliveryOk = M03Presentation.TryParseMoney(DeliveryMinText, "delivery-minimum", out var deliveryMoney, out var deliveryIssue);
            if (!deliveryOk && deliveryIssue is not null) issues.Add(deliveryIssue);
            var feeOk = M03Presentation.TryParseMoney(DeliveryFeeAmountText, "delivery-fee", out var feeMoney, out var feeIssue);
            if (!feeOk && feeIssue is not null) issues.Add(feeIssue);
            if (issues.Count > 0) return OperationResult.Failure(issues.ToArray());
            var current = loadedSettings ?? await settings.GetAsync(cancellationToken);
            var updated = current with { PickupDiscountRate = percent / 100m, PickupDiscountMinTotalTtc = pickupMoney, DeliveryMinMerchandiseTotalTtc = deliveryMoney, DeliveryFeeEnabled = DeliveryFeeEnabled, DeliveryFeeAmountTtc = feeMoney };
            var result = await settings.UpdateAsync(updated, cancellationToken);
            if (result.Succeeded) loadedSettings = updated;
            return result;
        }
        finally { IsBusy = false; }
    }

    public Task<ProductDraft?> LoadProductAsync(Guid id, CancellationToken cancellationToken = default) => catalogue.GetProductForEditAsync(id, cancellationToken);
    public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => catalogue.CreateProductAsync(draft, cancellationToken);
    public Task<OperationResult> UpdateProductAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken = default) => catalogue.UpdateProductAsync(id, draft, cancellationToken);
    public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => catalogue.CreateCategoryAsync(name, cancellationToken);
    public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) => catalogue.RenameCategoryAsync(id, name, cancellationToken);
    public Task<OperationResult> SetProductActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default) => catalogue.SetProductActiveAsync(id, active, cancellationToken);
    public Task<OperationResult> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => catalogue.DeleteProductAsync(id, cancellationToken);

    private void RestoreCategorySelection(Guid categoryId)
    {
        var next = CategoryFilters.FirstOrDefault(category => category.Id == categoryId)
            ?? CategoryFilters.FirstOrDefault(category => category.Id == Guid.Empty);
        selectedCategory = next;
        selectedCategoryId = next?.Id ?? Guid.Empty;
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(SelectedCategoryId));
    }

    private void SetActiveFilter(string? value)
    {
        activeFilter = value is "Active" or "Inactive" ? value : "All";
        OnPropertyChanged(nameof(ActiveFilter));
        OnPropertyChanged(nameof(SelectedStatusKey));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
