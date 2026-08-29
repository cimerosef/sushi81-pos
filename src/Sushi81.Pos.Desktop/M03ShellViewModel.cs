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
    private string searchText = string.Empty;
    private string activeFilter = "All";
    private bool isBusy;
    private BusinessSettings? loadedSettings;

    public M03ShellViewModel(CatalogueService catalogue, BusinessSettingsService settings)
    {
        this.catalogue = catalogue ?? throw new ArgumentNullException(nameof(catalogue));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Categories = new ObservableCollection<CategorySummary>();
        Products = new ObservableCollection<ProductSummary>();
        CategoryFilters = new ObservableCollection<CategorySummary>();
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

    public ProductSummary? SelectedProduct { get => selectedProduct; set { selectedProduct = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditProduct)); } }
    public CategorySummary? SelectedCategory { get => selectedCategory; set { selectedCategory = value; OnPropertyChanged(); } }
    public string SearchText { get => searchText; set { searchText = value ?? string.Empty; OnPropertyChanged(); } }
    public string ActiveFilter { get => activeFilter; set { activeFilter = value ?? "All"; OnPropertyChanged(); } }
    public bool CanEditProduct => SelectedProduct is not null && !IsBusy;
    public bool HasProducts => Products.Count > 0;
    public bool IsBusy { get => isBusy; private set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditProduct)); } }

    public string PickupDiscountRateText { get; set; } = "10";
    public string PickupDiscountMinText { get; set; } = "15.00";
    public string DeliveryMinText { get; set; } = "30.00";
    public bool DeliveryFeeEnabled { get; set; }
    public string DeliveryFeeAmountText { get; set; } = "0.00";

    public void ApplyLocalization(string all, string active, string inactive)
    {
        AllCategoryLabel = string.IsNullOrWhiteSpace(all) ? "All" : all;
        StatusFilters.Clear();
        StatusFilters.Add(new("All", string.IsNullOrWhiteSpace(all) ? "All" : all));
        StatusFilters.Add(new("Active", string.IsNullOrWhiteSpace(active) ? "Active" : active));
        StatusFilters.Add(new("Inactive", string.IsNullOrWhiteSpace(inactive) ? "Inactive" : inactive));
        OnPropertyChanged(nameof(AllCategoryLabel));
        OnPropertyChanged(nameof(StatusFilters));
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var categories = await catalogue.ListCategoriesAsync(cancellationToken);
            Categories.Clear();
            CategoryFilters.Clear();
            CategoryFilters.Add(new CategorySummary(Guid.Empty, AllCategoryLabel));
            foreach (var category in categories) { Categories.Add(category); CategoryFilters.Add(category); }
            var active = ActiveFilter switch { "Active" => true, "Inactive" => false, _ => (bool?)null };
            Guid? categoryId = SelectedCategory is null || SelectedCategory.Id == Guid.Empty ? null : SelectedCategory.Id;
            var products = await catalogue.ListProductsAsync(SearchText, categoryId, active, cancellationToken);
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
        if (!decimal.TryParse(PickupDiscountRateText, NumberStyles.Number, CultureInfo.InvariantCulture, out var percent)
            || !decimal.TryParse(PickupDiscountMinText, NumberStyles.Number, CultureInfo.InvariantCulture, out var pickupMin)
            || !decimal.TryParse(DeliveryMinText, NumberStyles.Number, CultureInfo.InvariantCulture, out var deliveryMin)
            || !decimal.TryParse(DeliveryFeeAmountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var fee))
            return OperationResult.Failure(new ValidationIssue("settings", "Enter valid numeric settings."));
        var current = loadedSettings ?? await settings.GetAsync(cancellationToken);
        var updated = current with { PickupDiscountRate = percent / 100m, PickupDiscountMinTotalTtc = Money.FromEuros(pickupMin), DeliveryMinMerchandiseTotalTtc = Money.FromEuros(deliveryMin), DeliveryFeeEnabled = DeliveryFeeEnabled, DeliveryFeeAmountTtc = Money.FromEuros(fee) };
        var result = await settings.UpdateAsync(updated, cancellationToken);
        if (result.Succeeded) loadedSettings = updated;
        return result;
    }

    public Task<ProductDraft?> LoadProductAsync(Guid id, CancellationToken cancellationToken = default) => catalogue.GetProductForEditAsync(id, cancellationToken);
    public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => catalogue.CreateProductAsync(draft, cancellationToken);
    public Task<OperationResult> UpdateProductAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken = default) => catalogue.UpdateProductAsync(id, draft, cancellationToken);
    public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => catalogue.CreateCategoryAsync(name, cancellationToken);
    public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) => catalogue.RenameCategoryAsync(id, name, cancellationToken);
    public Task<OperationResult> SetProductActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default) => catalogue.SetProductActiveAsync(id, active, cancellationToken);
    public Task<OperationResult> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => catalogue.DeleteProductAsync(id, cancellationToken);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
