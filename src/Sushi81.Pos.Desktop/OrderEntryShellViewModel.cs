using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

/// <summary>Presentation state for the M04 Caisse vertical slice.</summary>
public sealed class OrderEntryShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly OrderEntryService service;
    private ProductSummary? selectedProduct;
    private OrderEntryCartLineViewModel? selectedCartLine;
    private Guid selectedCategoryId;
    private string searchText = string.Empty;
    private FulfilmentMode? selectedFulfilment;
    private DateTime? plannedDate;
    private TimeSpan? plannedTime;
    private string plannedTimeText = string.Empty;
    private string telephone = string.Empty;
    private string deliveryAddress = string.Empty;
    private string comment = string.Empty;
    private bool pickupDiscountRequested;
    private bool isBusy;
    private bool isCommitted;
    private string totalText = "0.00";
    private Money? manualTotalOverride;
    private OrderPricingResult? pricing;
    private string validationMessage = string.Empty;
    private string committedMessage = string.Empty;
    private string reloadOrderIdText = string.Empty;
    private OrderSnapshot? reloadedOrder;
    private readonly object refreshLock = new();
    private CancellationTokenSource? refreshCancellation;
    private long refreshVersion;
    private readonly object priceLock = new();
    private CancellationTokenSource? priceCancellation;
    private long priceVersion;
    private bool disposed;
    private string allCategoriesLabel = "Toutes";

    public OrderEntryShellViewModel(OrderEntryService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Categories = new ObservableCollection<CategorySummary>();
        Products = new ObservableCollection<ProductSummary>();
        Cart = new ObservableCollection<OrderEntryCartLineViewModel>();
        FulfilmentChoices = new ObservableCollection<FulfilmentMode?> { null, FulfilmentMode.Retrait, FulfilmentMode.Livraison };
        plannedDate = service.BusinessDate.ToDateTime(TimeOnly.MinValue);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CategorySummary> Categories { get; }
    public ObservableCollection<ProductSummary> Products { get; }
    public ObservableCollection<OrderEntryCartLineViewModel> Cart { get; }
    public ObservableCollection<FulfilmentMode?> FulfilmentChoices { get; }
    public string AllCategoriesLabel => allCategoriesLabel;

    public void ApplyLocalization(string allCategories)
    {
        allCategoriesLabel = string.IsNullOrWhiteSpace(allCategories) ? "Toutes" : allCategories;
        if (Categories.Count > 0) Categories[0] = new CategorySummary(Guid.Empty, allCategoriesLabel);
        OnPropertyChanged(nameof(AllCategoriesLabel));
    }

    public ProductSummary? SelectedProduct { get => selectedProduct; set { selectedProduct = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAddSelectedProduct)); } }
    public OrderEntryCartLineViewModel? SelectedCartLine { get => selectedCartLine; set { selectedCartLine = value; OnPropertyChanged(); } }
    public Guid SelectedCategoryId { get => selectedCategoryId; set { if (selectedCategoryId == value) return; selectedCategoryId = value; OnPropertyChanged(); _ = RefreshProductsAsync(); } }
    public string SearchText { get => searchText; set { if (string.Equals(searchText, value, StringComparison.Ordinal)) return; searchText = value ?? string.Empty; OnPropertyChanged(); _ = RefreshProductsAsync(); } }
    public FulfilmentMode? SelectedFulfilment { get => selectedFulfilment; set { if (selectedFulfilment == value) return; selectedFulfilment = value; if (value != FulfilmentMode.Retrait) pickupDiscountRequested = false; OnPropertyChanged(); OnPropertyChanged(nameof(PickupDiscountRequested)); _ = RepriceAsync(clearManualOverride: true); } }
    public DateTime? PlannedDate { get => plannedDate; set { plannedDate = value; OnPropertyChanged(); _ = RepriceAsync(clearManualOverride: false); } }
    public TimeSpan? PlannedTime { get => plannedTime; private set { plannedTime = value; OnPropertyChanged(); } }
    public string PlannedTimeText
    {
        get => plannedTimeText;
        set
        {
            plannedTimeText = value ?? string.Empty;
            if (TimeSpan.TryParseExact(plannedTimeText.Trim(), ["hh\\:mm", "h\\:mm"], CultureInfo.InvariantCulture, out var parsed) && parsed >= TimeSpan.Zero && parsed < TimeSpan.FromDays(1)) PlannedTime = parsed;
            else if (string.IsNullOrWhiteSpace(plannedTimeText)) PlannedTime = null;
            OnPropertyChanged();
        }
    }
    public string Telephone { get => telephone; set { telephone = value ?? string.Empty; OnPropertyChanged(); } }
    public string DeliveryAddress { get => deliveryAddress; set { deliveryAddress = value ?? string.Empty; OnPropertyChanged(); } }
    public string Comment { get => comment; set { comment = value ?? string.Empty; OnPropertyChanged(); } }
    public bool PickupDiscountRequested { get => pickupDiscountRequested; set { if (pickupDiscountRequested == value) return; pickupDiscountRequested = value; OnPropertyChanged(); _ = RepriceAsync(clearManualOverride: true); } }
    public bool IsBusy { get => isBusy; private set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(CanAddSelectedProduct)); } }
    public bool IsCommitted { get => isCommitted; private set { isCommitted = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConfirm)); } }
    public bool CanAddSelectedProduct => !IsBusy && !IsCommitted && SelectedProduct is not null;
    public bool CanConfirm => !IsBusy && !IsCommitted && pricing?.IsValid == true;
    public string TotalText { get => totalText; private set { totalText = value; OnPropertyChanged(); } }
    public string ValidationMessage { get => validationMessage; private set { validationMessage = value; OnPropertyChanged(); } }
    public string CommittedMessage { get => committedMessage; private set { committedMessage = value; OnPropertyChanged(); } }
    public string ReloadOrderIdText { get => reloadOrderIdText; set { reloadOrderIdText = value ?? string.Empty; OnPropertyChanged(); } }
    public OrderSnapshot? ReloadedOrder { get => reloadedOrder; private set { reloadedOrder = value; OnPropertyChanged(); } }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var request = BeginRefresh(cancellationToken);
        try
        {
            var categories = await service.ListCategoriesAsync(request.Cancellation.Token);
            var products = await service.ListActiveProductsAsync(request.SearchText, request.CategoryId == Guid.Empty ? null : request.CategoryId, request.Cancellation.Token);
            if (!IsCurrent(request)) return;
            Categories.Clear();
            Categories.Add(new CategorySummary(Guid.Empty, AllCategoriesLabel));
            foreach (var category in categories) Categories.Add(category);
            if (selectedCategoryId != Guid.Empty && !categories.Any(category => category.Id == selectedCategoryId))
            {
                selectedCategoryId = Guid.Empty;
                OnPropertyChanged(nameof(SelectedCategoryId));
            }
            Products.Clear();
            foreach (var product in products) Products.Add(product);
            if (SelectedProduct is not null) SelectedProduct = Products.FirstOrDefault(product => product.Id == SelectedProduct.Id);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsCurrent(request)) { ValidationMessage = exception.Message; }
        finally { EndRefresh(request); }
    }

    public async Task AddSelectedProductAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProduct is not { } selected || IsCommitted) return;
        var product = await service.GetActiveProductAsync(selected.Id, cancellationToken);
        if (product is null) { ValidationMessage = "Le produit n'est plus actif."; return; }
        PendingProduct = product;
        OnPropertyChanged(nameof(PendingProduct));
    }

    public OrderEntryProduct? PendingProduct { get; private set; }

    public void AddConfiguredLine(OrderEntryProduct product, IReadOnlyList<Guid> selectedOptionIds, IReadOnlyList<OrderLineAdjustmentDraft> customAdjustments, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (IsCommitted) return;
        Cart.Add(new OrderEntryCartLineViewModel(OrderLineDraft.Create(product.Aggregate, quantity) with
        {
            SelectedOptionIds = selectedOptionIds.ToArray(),
            CustomAdjustments = customAdjustments.ToArray(),
            CategoryName = product.CategoryName
        }));
        PendingProduct = null;
        OnPropertyChanged(nameof(PendingProduct));
        _ = RepriceAsync(clearManualOverride: true);
    }

    public Task<OrderEntryProduct?> GetActiveProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => service.GetActiveProductAsync(productId, cancellationToken);

    public void UpdateConfiguredLine(OrderEntryCartLineViewModel line, IReadOnlyList<Guid> selectedOptionIds, IReadOnlyList<OrderLineAdjustmentDraft> customAdjustments, int? quantity = null)
    {
        ArgumentNullException.ThrowIfNull(line);
        line.Replace(line.Draft with { SelectedOptionIds = selectedOptionIds.ToArray(), CustomAdjustments = customAdjustments.ToArray(), Quantity = quantity ?? line.Quantity });
        _ = RepriceAsync(clearManualOverride: true);
    }

    public void RemoveLine(OrderEntryCartLineViewModel line)
    {
        if (IsCommitted) return;
        if (Cart.Remove(line)) _ = RepriceAsync(clearManualOverride: true);
    }

    public void ChangeQuantity(OrderEntryCartLineViewModel line, int quantity)
    {
        if (IsCommitted || quantity <= 0) return;
        line.Replace(line.Draft with { Quantity = quantity });
        _ = RepriceAsync(clearManualOverride: true);
    }

    public void SetManualTotal(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var euros))
        {
            ValidationMessage = "Le total TTC saisi est invalide.";
            return;
        }
        manualTotalOverride = Money.FromEuros(euros);
        _ = RepriceAsync(clearManualOverride: false);
    }

    public async Task RepriceAsync(bool clearManualOverride, CancellationToken cancellationToken = default)
    {
        if (clearManualOverride) manualTotalOverride = null;
        var request = BeginPrice(cancellationToken);
        try
        {
            var result = await service.PriceAsync(BuildDraft(), request.Cancellation.Token);
            if (!IsCurrent(request)) return;
            pricing = result;
            TotalText = pricing.TotalTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
            for (var index = 0; index < Math.Min(Cart.Count, pricing.Lines.Count); index++) Cart[index].SetLineTotal(pricing.Lines[index].CalculatedLineTotalTtc);
            ValidationMessage = string.Join(Environment.NewLine, pricing.ValidationErrors.Concat(pricing.DiscountNotAppliedReason is { } reason && PickupDiscountRequested ? [reason] : []));
            OnPropertyChanged(nameof(CanConfirm));
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsCurrent(request)) { ValidationMessage = exception.Message; OnPropertyChanged(nameof(CanConfirm)); }
        finally { EndPrice(request); }
    }

    public async Task<ConfirmOrderResult?> ConfirmAsync(CancellationToken cancellationToken = default)
    {
        if (IsCommitted) return null;
        IsBusy = true;
        try
        {
            var result = await service.ConfirmNewOrderAsync(BuildDraft(), cancellationToken);
            if (!result.Succeeded)
            {
                ValidationMessage = string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message));
                return result;
            }
            IsCommitted = true;
            var committedId = result.CommittedOrder?.Id ?? result.PersistedOrderId;
            CommittedMessage = result.HasOutputFailure
                ? $"Commande {committedId} enregistrée ; l'envoi de sortie a échoué."
                : $"Commande {committedId} enregistrée.";
            ValidationMessage = result.HasOutputFailure ? string.Join(Environment.NewLine, result.Issues.Select(issue => issue.Message)) : string.Empty;
            return result;
        }
        finally { IsBusy = false; }
    }

    public async Task<bool> ReloadOrderAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(ReloadOrderIdText.Trim(), out var id)) { ValidationMessage = "Identifiant de commande invalide."; return false; }
        ReloadedOrder = await service.GetOrderByIdAsync(id, cancellationToken);
        return ReloadedOrder is not null;
    }

    private NewOrderDraft BuildDraft() => new(
        Cart.Select(line => line.Draft).ToArray(), SelectedFulfilment,
        PlannedDate is { } date ? DateOnly.FromDateTime(date.Date) : null,
        PlannedTime is { } time ? TimeOnly.FromTimeSpan(time) : null,
        Telephone, DeliveryAddress, Comment, PickupDiscountRequested, manualTotalOverride);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lock (refreshLock) refreshCancellation?.Cancel();
        lock (priceLock) priceCancellation?.Cancel();
        service.Dispose();
    }

    private RefreshRequest BeginRefresh(CancellationToken externalCancellationToken)
    {
        lock (refreshLock)
        {
            refreshCancellation?.Cancel();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            refreshCancellation = cancellation;
            return new(++refreshVersion, searchText, selectedCategoryId, cancellation);
        }
    }

    private bool IsCurrent(RefreshRequest request)
    {
        lock (refreshLock) return request.Version == refreshVersion && ReferenceEquals(request.Cancellation, refreshCancellation);
    }

    private void EndRefresh(RefreshRequest request)
    {
        lock (refreshLock)
        {
            if (ReferenceEquals(request.Cancellation, refreshCancellation)) refreshCancellation = null;
        }
        request.Cancellation.Dispose();
    }

    private PriceRequest BeginPrice(CancellationToken externalCancellationToken)
    {
        lock (priceLock)
        {
            priceCancellation?.Cancel();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            priceCancellation = cancellation;
            return new(++priceVersion, cancellation);
        }
    }

    private bool IsCurrent(PriceRequest request)
    {
        lock (priceLock) return request.Version == priceVersion && ReferenceEquals(request.Cancellation, priceCancellation);
    }

    private void EndPrice(PriceRequest request)
    {
        lock (priceLock)
        {
            if (ReferenceEquals(request.Cancellation, priceCancellation)) priceCancellation = null;
        }
        request.Cancellation.Dispose();
    }

    private readonly record struct RefreshRequest(long Version, string SearchText, Guid CategoryId, CancellationTokenSource Cancellation);
    private readonly record struct PriceRequest(long Version, CancellationTokenSource Cancellation);
    private async Task RefreshProductsAsync()
    {
        try { await RefreshAsync(); } catch { }
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class OrderEntryCartLineViewModel : INotifyPropertyChanged
{
    private OrderLineDraft draft;
    private string lineTotalText = "0.00";
    public OrderEntryCartLineViewModel(OrderLineDraft draft) => this.draft = draft;
    public event PropertyChangedEventHandler? PropertyChanged;
    public OrderLineDraft Draft => draft;
    public string DisplayName => $"{draft.Product.Product.Code} — {draft.Product.Product.Name}";
    public string Configuration => string.Join(", ", draft.SelectedOptionIds.Select(id => draft.Product.OptionsByGroup.Values.SelectMany(options => options).FirstOrDefault(option => option.Id == id)?.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Concat(draft.CustomAdjustments.Select(adjustment => adjustment.Label)));
    public int Quantity => draft.Quantity;
    public string LineTotalText => lineTotalText;
    public void Replace(OrderLineDraft next) { draft = next; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); }
    public void SetLineTotal(Money total) { lineTotalText = total.Euros.ToString("0.00", CultureInfo.CurrentCulture); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalText))); }
}
