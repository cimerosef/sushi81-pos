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
    private bool mutationBusy;
    private BusinessSettings? loadedSettings;
    private string activateLabel = "Activate";
    private string deactivateLabel = "Deactivate";
    private string settingsValidationMessage = string.Empty;
    private readonly object filterRefreshLock = new();
    private CancellationTokenSource? filterRefreshCancellation;
    private Task filterRefreshTask = Task.CompletedTask;
    private long filterRefreshVersion;
    private int filterRefreshSuppression;

    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

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
        // Keep the three semantic status options for the lifetime of the view-model. WPF
        // can process collection replacement asynchronously; stable option identity lets
        // localization update labels without creating a selection lifecycle race.
        StatusFilters = new ObservableCollection<FilterOption>
        {
            new("All", "All"),
            new("Active", "Active"),
            new("Inactive", "Inactive")
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? FilterRefreshFailed;

    public ObservableCollection<CategorySummary> Categories { get; }
    public ObservableCollection<CategorySummary> CategoryFilters { get; }
    public ObservableCollection<ProductSummary> Products { get; }
    public ObservableCollection<FilterOption> StatusFilters { get; }

    public string AllCategoryLabel { get; private set; } = "All";

    public sealed class FilterOption : INotifyPropertyChanged
    {
        private string label;

        public FilterOption(string key, string label)
        {
            Key = key;
            this.label = label;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Key { get; }
        public string Label => label;

        public void SetLabel(string value)
        {
            var next = string.IsNullOrWhiteSpace(value) ? Key : value;
            if (string.Equals(label, next, StringComparison.Ordinal)) return;
            label = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
        }
    }

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
            if (next is not null && selectedCategory?.Id == next.Id) return;
            selectedCategory = next;
            if (next is not null) selectedCategoryId = next.Id;
            OnPropertyChanged(nameof(SelectedCategoryId));
            OnPropertyChanged();
            ScheduleFilterRefresh(debounce: false);
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
            var nextId = next?.Id ?? requestedId;
            if (selectedCategoryId == nextId) return;
            selectedCategory = next;
            selectedCategoryId = nextId;
            OnPropertyChanged(nameof(SelectedCategory));
            OnPropertyChanged();
            ScheduleFilterRefresh(debounce: false);
        }
    }
    public string SearchText
    {
        get => searchText;
        set
        {
            var next = value ?? string.Empty;
            if (string.Equals(searchText, next, StringComparison.Ordinal)) return;
            searchText = next;
            OnPropertyChanged();
            ScheduleFilterRefresh(debounce: true);
        }
    }
    public string ActiveFilter
    {
        get => activeFilter;
        set => SetActiveFilter(value, schedule: true);
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
            SetActiveFilter(value, schedule: true);
        }
    }

    /// <summary>Completion task for the latest automatic filter/search refresh.</summary>
    public Task FilterRefreshTask
    {
        get { lock (filterRefreshLock) return filterRefreshTask; }
    }
    public bool CanEditProduct => SelectedProduct is not null && !IsBusy;
    public bool CanDeleteProduct => SelectedProduct is not null && !IsBusy;
    public bool CanToggleProduct => SelectedProduct is not null && !IsBusy;
    public bool CanBulkActivate => !IsBusy && Products.Any(product => !product.IsActive);
    public bool CanBulkDeactivate => !IsBusy && Products.Any(product => product.IsActive);
    public int FilteredProductCount => Products.Count;
    public int FilteredProductsToActivateCount => Products.Count(product => !product.IsActive);
    public int FilteredProductsToDeactivateCount => Products.Count(product => product.IsActive);
    public string ToggleProductActionLabel => SelectedProduct?.IsActive == true ? deactivateLabel : activateLabel;
    public bool HasProducts => Products.Count > 0;
    public bool IsBusy { get => isBusy; private set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanEditProduct)); OnPropertyChanged(nameof(CanDeleteProduct)); OnPropertyChanged(nameof(CanToggleProduct)); OnPropertyChanged(nameof(CanBulkActivate)); OnPropertyChanged(nameof(CanBulkDeactivate)); OnPropertyChanged(nameof(ToggleProductActionLabel)); } }

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
        StatusFilters.Single(option => option.Key == "All").SetLabel(all);
        StatusFilters.Single(option => option.Key == "Active").SetLabel(active);
        StatusFilters.Single(option => option.Key == "Inactive").SetLabel(inactive);
        SetActiveFilter(statusKey, schedule: false);
        SuppressFilterRefresh();
        try
        {
            if (CategoryFilters.Count > 0)
            {
                CategoryFilters[0] = new CategorySummary(Guid.Empty, AllCategoryLabel);
                RestoreCategorySelection(categoryId);
            }
        }
        finally
        {
            ResumeFilterRefresh();
        }
        OnPropertyChanged(nameof(AllCategoryLabel));
        OnPropertyChanged(nameof(ToggleProductActionLabel));
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var request = BeginRefreshRequest(isFullRefresh: true, cancellationToken);
        IsBusy = true;
        var task = ExecuteRefreshAsync(request, cancellationToken);
        lock (filterRefreshLock)
        {
            if (request.Version == filterRefreshVersion && ReferenceEquals(filterRefreshCancellation, request.Cancellation))
                filterRefreshTask = task;
        }
        return task;
    }

    private async Task ExecuteRefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var categories = await catalogue.ListCategoriesAsync(request.Cancellation.Token);
            request.Cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentRequest(request)) return;

            SuppressFilterRefresh();
            try
            {
                Categories.Clear();
                CategoryFilters.Clear();
                CategoryFilters.Add(new CategorySummary(Guid.Empty, AllCategoryLabel));
                foreach (var category in categories) { Categories.Add(category); CategoryFilters.Add(category); }
                RestoreCategorySelection(request.Snapshot.CategoryId);
                SetActiveFilter(request.Snapshot.StatusKey, schedule: false);
            }
            finally
            {
                ResumeFilterRefresh();
            }

            // Category refresh can deterministically fall back to All when the previously
            // selected category no longer exists. Use that effective key for the product
            // query while retaining the request's search/status snapshot.
            Guid? effectiveCategoryId = selectedCategoryId == Guid.Empty ? null : selectedCategoryId;
            var products = await catalogue.ListProductsAsync(request.Snapshot.SearchText, effectiveCategoryId, request.Snapshot.ActiveValue, request.Cancellation.Token);
            request.Cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrentRequest(request)) return;
            Products.Clear(); foreach (var product in products) Products.Add(product);
            NotifyProductFilterProperties();
            if (SelectedProduct is not null) SelectedProduct = Products.FirstOrDefault(product => product.Id == SelectedProduct.Id);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // A newer manual or automatic request superseded this one. Its task is complete
            // without committing stale categories or products.
        }
        finally
        {
            if (EndRefreshRequest(request)) IsBusy = false;
        }
    }

    private void ScheduleFilterRefresh(bool debounce)
    {
        if (filterRefreshSuppression > 0) return;

        var request = BeginRefreshRequest(isFullRefresh: false, CancellationToken.None);
        lock (filterRefreshLock) filterRefreshTask = RunFilterRefreshAsync(request, debounce);
    }

    private async Task RunFilterRefreshAsync(RefreshRequest request, bool debounce)
    {
        try
        {
            if (debounce) await Task.Delay(SearchDebounce, request.Cancellation.Token);
            await RefreshProductsAsync(request);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested)
        {
            // A newer filter request superseded this one. It must not report an error.
        }
        catch
        {
            if (IsCurrentRequest(request))
            {
                // Do not leave an older result actionable after a failed filter query.
                Products.Clear();
                NotifyProductFilterProperties();
                FilterRefreshFailed?.Invoke(this, EventArgs.Empty);
            }
        }
        finally
        {
            EndRefreshRequest(request);
        }
    }

    private async Task RefreshProductsAsync(RefreshRequest request)
    {
        var products = await catalogue.ListProductsAsync(request.Snapshot.SearchText, request.Snapshot.CategoryIdValue, request.Snapshot.ActiveValue, request.Cancellation.Token);
        request.Cancellation.Token.ThrowIfCancellationRequested();
        if (!IsCurrentRequest(request)) return;
        Products.Clear();
        foreach (var product in products) Products.Add(product);
        NotifyProductFilterProperties();
        if (SelectedProduct is not null) SelectedProduct = Products.FirstOrDefault(product => product.Id == SelectedProduct.Id);
    }

    private RefreshRequest BeginRefreshRequest(bool isFullRefresh, CancellationToken externalCancellationToken)
    {
        CancellationTokenSource cancellation;
        RefreshRequest request;
        lock (filterRefreshLock)
        {
            filterRefreshCancellation?.Cancel();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            filterRefreshCancellation = cancellation;
            request = new(++filterRefreshVersion, CaptureFilterSnapshot(), cancellation, isFullRefresh);
        }
        if (!isFullRefresh && IsBusy && !mutationBusy) IsBusy = false;
        return request;
    }

    private bool EndRefreshRequest(RefreshRequest request)
    {
        bool isCurrent;
        lock (filterRefreshLock)
        {
            isCurrent = request.Version == filterRefreshVersion && ReferenceEquals(filterRefreshCancellation, request.Cancellation);
            if (isCurrent)
            {
                filterRefreshCancellation = null;
            }
        }
        request.Cancellation.Dispose();
        return isCurrent && request.IsFullRefresh;
    }

    private bool IsCurrentRequest(RefreshRequest request)
    {
        lock (filterRefreshLock)
        {
            return request.Version == filterRefreshVersion && ReferenceEquals(filterRefreshCancellation, request.Cancellation);
        }
    }

    private FilterSnapshot CaptureFilterSnapshot() => new(searchText, selectedCategoryId, activeFilter);

    private void SuppressFilterRefresh() => filterRefreshSuppression++;

    private void ResumeFilterRefresh() => filterRefreshSuppression = Math.Max(0, filterRefreshSuppression - 1);

    private readonly record struct FilterSnapshot(string SearchText, Guid CategoryId, string StatusKey)
    {
        public Guid? CategoryIdValue => CategoryId == Guid.Empty ? null : CategoryId;
        public bool? ActiveValue => StatusKey switch { "Active" => true, "Inactive" => false, _ => null };
    }

    private readonly record struct RefreshRequest(long Version, FilterSnapshot Snapshot, CancellationTokenSource Cancellation, bool IsFullRefresh);

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
        mutationBusy = true;
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
        finally { mutationBusy = false; IsBusy = false; }
    }

    public Task<ProductDraft?> LoadProductAsync(Guid id, CancellationToken cancellationToken = default) => catalogue.GetProductForEditAsync(id, cancellationToken);
    public Task<OperationResult<Guid>> CreateProductAsync(ProductDraft draft, CancellationToken cancellationToken = default) => catalogue.CreateProductAsync(draft, cancellationToken);
    public Task<OperationResult> UpdateProductAsync(Guid id, ProductDraft draft, CancellationToken cancellationToken = default) => catalogue.UpdateProductAsync(id, draft, cancellationToken);
    public Task<OperationResult<CategorySummary>> CreateCategoryAsync(string name, CancellationToken cancellationToken = default) => catalogue.CreateCategoryAsync(name, cancellationToken);
    public Task<OperationResult<CategorySummary>> RenameCategoryAsync(Guid id, string name, CancellationToken cancellationToken = default) => catalogue.RenameCategoryAsync(id, name, cancellationToken);
    public Task<OperationResult> SetProductActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default) => catalogue.SetProductActiveAsync(id, active, cancellationToken);
    public async Task<BulkProductActiveStateRequest> CaptureBulkProductActiveStateAsync(bool targetIsActive, CancellationToken cancellationToken = default)
    {
        await WaitForLatestFilterRefreshAsync(cancellationToken);
        // ProductSummary is immutable; copying the IDs and expected states creates the
        // confirmation snapshot that cannot be retargeted by later filter changes.
        var items = Products.Select(product => new BulkProductActiveStateItem(product.Id, product.IsActive)).ToArray();
        return new BulkProductActiveStateRequest(targetIsActive, items);
    }
    public async Task<OperationResult<BulkProductActiveStateResult>> BulkSetProductsActiveAsync(BulkProductActiveStateRequest request, CancellationToken cancellationToken = default)
    {
        if (IsBusy) return OperationResult<BulkProductActiveStateResult>.Failure(new ValidationIssue("products", "A catalogue operation is already in progress.", ValidationCodes.Busy));
        mutationBusy = true;
        IsBusy = true;
        try { return await catalogue.BulkSetProductsActiveAsync(request, cancellationToken); }
        finally { mutationBusy = false; IsBusy = false; }
    }
    public Task<OperationResult> DeleteProductAsync(Guid id, CancellationToken cancellationToken = default) => catalogue.DeleteProductAsync(id, cancellationToken);

    private async Task WaitForLatestFilterRefreshAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task task;
            long version;
            lock (filterRefreshLock)
            {
                task = filterRefreshTask;
                version = filterRefreshVersion;
            }
            await task.WaitAsync(cancellationToken);
            lock (filterRefreshLock)
            {
                if (version == filterRefreshVersion && ReferenceEquals(task, filterRefreshTask)) return;
            }
        }
    }

    private void NotifyProductFilterProperties()
    {
        OnPropertyChanged(nameof(HasProducts));
        OnPropertyChanged(nameof(FilteredProductCount));
        OnPropertyChanged(nameof(FilteredProductsToActivateCount));
        OnPropertyChanged(nameof(FilteredProductsToDeactivateCount));
        OnPropertyChanged(nameof(CanBulkActivate));
        OnPropertyChanged(nameof(CanBulkDeactivate));
    }

    private void RestoreCategorySelection(Guid categoryId)
    {
        var next = CategoryFilters.FirstOrDefault(category => category.Id == categoryId)
            ?? CategoryFilters.FirstOrDefault(category => category.Id == Guid.Empty);
        selectedCategory = next;
        selectedCategoryId = next?.Id ?? Guid.Empty;
        OnPropertyChanged(nameof(SelectedCategory));
        OnPropertyChanged(nameof(SelectedCategoryId));
    }

    private void SetActiveFilter(string? value, bool schedule)
    {
        var next = value is "Active" or "Inactive" ? value : "All";
        if (string.Equals(activeFilter, next, StringComparison.Ordinal)) return;
        activeFilter = next;
        OnPropertyChanged(nameof(ActiveFilter));
        OnPropertyChanged(nameof(SelectedStatusKey));
        if (schedule) ScheduleFilterRefresh(debounce: false);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
