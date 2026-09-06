using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

public sealed record FulfilmentChoice(FulfilmentMode? Mode, string Label);

public sealed record TimeChoice(int? Value, string Label);

/// <summary>Localized read-only presentation of one persisted order-browser row.</summary>
public sealed class OrderBrowserRowViewModel(OrderBrowserRow row) : INotifyPropertyChanged
{
    private string retraitLabel = "Retrait";
    private string livraisonLabel = "Livraison";
    private string openLabel = "Ouverte";
    private string closedLabel = "Clôturée";
    private string cancelledLabel = "Annulée";
    private string emptyTelephoneLabel = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public OrderBrowserRow Row { get; } = row ?? throw new ArgumentNullException(nameof(row));
    public Guid Id => Row.Id;
    public string PlannedTimeText => OrderTimeFormatting.Format(Row.PlannedFulfilmentTime);
    public string FulfilmentText => Row.Fulfilment == FulfilmentMode.Retrait ? retraitLabel : livraisonLabel;
    public string StatusText => Row.Status switch
    {
        OrderStatus.Open => openLabel,
        OrderStatus.Closed => closedLabel,
        OrderStatus.Cancelled => cancelledLabel,
        _ => Row.Status.ToString()
    };
    public string TotalText => Row.TotalTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string TelephoneText => string.IsNullOrWhiteSpace(Row.Telephone) ? emptyTelephoneLabel : Row.Telephone;

    public void ApplyLocalization(IReadOnlyDictionary<string, string> labels)
    {
        retraitLabel = Read(labels, "Retrait", "Retrait");
        livraisonLabel = Read(labels, "Livraison", "Livraison");
        openLabel = Read(labels, "OrderStatusOpen", "Ouverte");
        closedLabel = Read(labels, "OrderStatusClosed", "Clôturée");
        cancelledLabel = Read(labels, "OrderStatusCancelled", "Annulée");
        emptyTelephoneLabel = Read(labels, "OrderBrowserEmptyTelephone", string.Empty);
        OnPropertyChanged(nameof(FulfilmentText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(TelephoneText));
    }

    private static string Read(IReadOnlyDictionary<string, string> labels, string key, string fallback) =>
        labels.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

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
    private int? selectedPlannedHour;
    private int? selectedPlannedMinute;
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
    private IReadOnlyList<ValidationIssue>? activeValidationIssues;
    private bool pricingValidationActive;
    private bool renderingValidationMessage;
    private string committedMessage = string.Empty;
    private string reloadOrderIdText = string.Empty;
    private OrderSnapshot? reloadedOrder;
    private DateTime? browseDate;
    private OrderBrowserRowViewModel? selectedBrowserOrder;
    private readonly object refreshLock = new();
    private CancellationTokenSource? refreshCancellation;
    private long refreshVersion;
    private readonly object priceLock = new();
    private CancellationTokenSource? priceCancellation;
    private long priceVersion;
    private readonly object browserLock = new();
    private CancellationTokenSource? browserCancellation;
    private long browserVersion;
    private CancellationTokenSource? browserSelectionCancellation;
    private long browserSelectionVersion;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private bool disposed;
    private string allCategoriesLabel = "Toutes";
    private string unselectedFulfilmentLabel = "—";
    private string retraitFulfilmentLabel = "Retrait";
    private string livraisonFulfilmentLabel = "Livraison";
    private string manualTotalStateText = "Total manuel";
    private string newOrderLabel = "Nouvelle commande";
    private string quantityLabel = "Quantité";
    private IReadOnlyDictionary<string, string> localized = new Dictionary<string, string>(StringComparer.Ordinal);

    public OrderEntryShellViewModel(OrderEntryService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        Categories = new ObservableCollection<CategorySummary>();
        Products = new ObservableCollection<ProductSummary>();
        Cart = new ObservableCollection<OrderEntryCartLineViewModel>();
        BrowserOrders = new ObservableCollection<OrderBrowserRowViewModel>();
        PlannedHourChoices = new ObservableCollection<TimeChoice>(BuildTimeChoices([11, 12, 13, 14, 18, 19, 20, 21, 22]));
        PlannedMinuteChoices = new ObservableCollection<TimeChoice>(BuildTimeChoices([0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55]));
        FulfilmentChoices = new ObservableCollection<FulfilmentChoice>
        {
            new(null, unselectedFulfilmentLabel),
            new(FulfilmentMode.Retrait, retraitFulfilmentLabel),
            new(FulfilmentMode.Livraison, livraisonFulfilmentLabel)
        };
        plannedDate = service.BusinessDate.ToDateTime(TimeOnly.MinValue);
        browseDate = plannedDate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<CategorySummary> Categories { get; }
    public ObservableCollection<ProductSummary> Products { get; }
    public ObservableCollection<OrderEntryCartLineViewModel> Cart { get; }
    public ObservableCollection<FulfilmentChoice> FulfilmentChoices { get; }
    public ObservableCollection<TimeChoice> PlannedHourChoices { get; }
    public ObservableCollection<TimeChoice> PlannedMinuteChoices { get; }
    public ObservableCollection<OrderBrowserRowViewModel> BrowserOrders { get; }
    public string AllCategoriesLabel => allCategoriesLabel;

    public void ApplyLocalization(
        string allCategories,
        string? unselectedFulfilment = null,
        string? retraitFulfilment = null,
        string? livraisonFulfilment = null,
        string? manualTotalState = null,
        string? newOrder = null,
        string? quantityLabel = null,
        IReadOnlyDictionary<string, string>? labels = null,
        string? browseOrders = null,
        string? browseDateLabel = null,
        string? browseRefresh = null,
        string? browseEmpty = null)
    {
        var previousFulfilment = selectedFulfilment;
        var previousHour = selectedPlannedHour;
        var previousMinute = selectedPlannedMinute;
        var previousPlannedTime = plannedTime;
        var previousPickupDiscount = pickupDiscountRequested;
        if (labels is not null) localized = labels;
        allCategoriesLabel = string.IsNullOrWhiteSpace(allCategories) ? "Toutes" : allCategories;
        unselectedFulfilmentLabel = string.IsNullOrWhiteSpace(unselectedFulfilment) ? "—" : unselectedFulfilment;
        retraitFulfilmentLabel = string.IsNullOrWhiteSpace(retraitFulfilment) ? "Retrait" : retraitFulfilment;
        livraisonFulfilmentLabel = string.IsNullOrWhiteSpace(livraisonFulfilment) ? "Livraison" : livraisonFulfilment;
        manualTotalStateText = string.IsNullOrWhiteSpace(manualTotalState) ? "Total manuel" : manualTotalState;
        newOrderLabel = string.IsNullOrWhiteSpace(newOrder) ? "Nouvelle commande" : newOrder;
        this.quantityLabel = string.IsNullOrWhiteSpace(quantityLabel) ? "Quantité" : quantityLabel;
        if (Categories.Count > 0 && !string.Equals(Categories[0].Name, allCategoriesLabel, StringComparison.Ordinal))
        {
            Categories[0] = new CategorySummary(Guid.Empty, allCategoriesLabel);
            OnPropertyChanged(nameof(SelectedCategoryId));
        }
        if (PlannedHourChoices.Count > 0) PlannedHourChoices[0] = new TimeChoice(null, Localized("TimeUnset", "—"));
        if (PlannedMinuteChoices.Count > 0) PlannedMinuteChoices[0] = new TimeChoice(null, Localized("TimeUnset", "—"));
        if (FulfilmentChoices.Count == 3)
        {
            FulfilmentChoices[0] = new FulfilmentChoice(null, unselectedFulfilmentLabel);
            FulfilmentChoices[1] = new FulfilmentChoice(FulfilmentMode.Retrait, retraitFulfilmentLabel);
            FulfilmentChoices[2] = new FulfilmentChoice(FulfilmentMode.Livraison, livraisonFulfilmentLabel);
        }
        selectedFulfilment = previousFulfilment;
        selectedPlannedHour = previousHour;
        selectedPlannedMinute = previousMinute;
        plannedTime = previousPlannedTime;
        pickupDiscountRequested = previousPickupDiscount;
        foreach (var line in Cart) line.SetQuantityLabel(this.quantityLabel);
        foreach (var row in BrowserOrders) row.ApplyLocalization(localized);
        if (pricingValidationActive) RenderPricingValidationMessage();
        else if (activeValidationIssues is { Count: > 0 }) RenderValidationIssues();
        OnPropertyChanged(nameof(AllCategoriesLabel));
        OnPropertyChanged(nameof(SelectedFulfilment));
        OnPropertyChanged(nameof(SelectedPlannedHour));
        OnPropertyChanged(nameof(SelectedPlannedMinute));
        OnPropertyChanged(nameof(PlannedTime));
        OnPropertyChanged(nameof(PlannedTimeValid));
        OnPropertyChanged(nameof(PickupDiscountRequested));
        OnPropertyChanged(nameof(IsPickupDiscountEnabled));
        OnPropertyChanged(nameof(ManualTotalStateText));
        OnPropertyChanged(nameof(NewOrderLabel));
        OnPropertyChanged(nameof(ReloadedOrderDisplay));
        OnPropertyChanged(nameof(HasReloadedOrder));
    }

    public ProductSummary? SelectedProduct { get => selectedProduct; set { selectedProduct = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanAddSelectedProduct)); } }
    public OrderEntryCartLineViewModel? SelectedCartLine { get => selectedCartLine; set { selectedCartLine = value; OnPropertyChanged(); } }
    public Guid SelectedCategoryId { get => selectedCategoryId; set { if (selectedCategoryId == value) return; selectedCategoryId = value; OnPropertyChanged(); _ = RefreshProductsAsync(); } }
    public string SearchText { get => searchText; set { if (string.Equals(searchText, value, StringComparison.Ordinal)) return; searchText = value ?? string.Empty; OnPropertyChanged(); _ = RefreshProductsAsync(); } }
    public FulfilmentMode? SelectedFulfilment
    {
        get => selectedFulfilment;
        set
        {
            if (selectedFulfilment == value) return;
            selectedFulfilment = value;
            if (value != FulfilmentMode.Retrait) pickupDiscountRequested = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PickupDiscountRequested));
            OnPropertyChanged(nameof(IsPickupDiscountEnabled));
            _ = RepriceAsync(clearManualOverride: true);
        }
    }
    public DateTime? PlannedDate
    {
        get => plannedDate;
        set
        {
            plannedDate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlannedDateValid));
            OnPropertyChanged(nameof(CanConfirm));
            _ = RepriceAsync(clearManualOverride: false);
        }
    }
    public DateTime MinimumPlannedDate => service.BusinessDate.ToDateTime(TimeOnly.MinValue);
    public bool PlannedDateValid => PlannedDate is { } value && value.Date >= MinimumPlannedDate.Date;
    public TimeSpan? PlannedTime { get => plannedTime; private set { plannedTime = value; OnPropertyChanged(); OnPropertyChanged(nameof(PlannedTimeText)); } }
    public string PlannedTimeText => PlannedTime is { } time ? OrderTimeFormatting.Format(TimeOnly.FromTimeSpan(time)) : string.Empty;
    public DateTime? BrowseDate
    {
        get => browseDate;
        set
        {
            var next = (value ?? service.BusinessDate.ToDateTime(TimeOnly.MinValue)).Date;
            if (browseDate?.Date == next)
            {
                if (value is null) OnPropertyChanged();
                return;
            }
            browseDate = next;
            OnPropertyChanged();
            _ = RefreshOrderBrowserAsync();
        }
    }
    public OrderBrowserRowViewModel? SelectedBrowserOrder
    {
        get => selectedBrowserOrder;
        set { if (ReferenceEquals(selectedBrowserOrder, value)) return; selectedBrowserOrder = value; OnPropertyChanged(); }
    }
    public bool HasBrowserOrders => BrowserOrders.Count > 0;
    public int? SelectedPlannedHour
    {
        get => selectedPlannedHour;
        set
        {
            if (selectedPlannedHour == value) return;
            selectedPlannedHour = value;
            if (value is null) selectedPlannedMinute = null;
            UpdatePlannedTime();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPlannedMinute));
            OnPropertyChanged(nameof(CanConfirm));
            _ = RepriceAsync(clearManualOverride: false);
        }
    }
    public int? SelectedPlannedMinute
    {
        get => selectedPlannedMinute;
        set
        {
            if (selectedPlannedMinute == value) return;
            selectedPlannedMinute = value;
            if (value is null) selectedPlannedHour = null;
            UpdatePlannedTime();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPlannedHour));
            OnPropertyChanged(nameof(CanConfirm));
            _ = RepriceAsync(clearManualOverride: false);
        }
    }
    public string Telephone { get => telephone; set { telephone = value ?? string.Empty; OnPropertyChanged(); } }
    public string DeliveryAddress { get => deliveryAddress; set { deliveryAddress = value ?? string.Empty; OnPropertyChanged(); } }
    public string Comment { get => comment; set { comment = value ?? string.Empty; OnPropertyChanged(); } }
    public bool PickupDiscountRequested
    {
        get => pickupDiscountRequested;
        set
        {
            var next = value && selectedFulfilment == FulfilmentMode.Retrait;
            if (pickupDiscountRequested == next) return;
            pickupDiscountRequested = next;
            OnPropertyChanged();
            _ = RepriceAsync(clearManualOverride: true);
        }
    }
    public bool IsBusy { get => isBusy; private set { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(CanAddSelectedProduct)); OnPropertyChanged(nameof(CanStartNewOrder)); OnPropertyChanged(nameof(IsPickupDiscountEnabled)); } }
    public bool IsCommitted { get => isCommitted; private set { isCommitted = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanConfirm)); OnPropertyChanged(nameof(CanStartNewOrder)); OnPropertyChanged(nameof(IsPickupDiscountEnabled)); } }
    public bool CanAddSelectedProduct => !IsBusy && !IsCommitted && SelectedProduct is not null;
    public bool CanConfirm => !IsBusy && !IsCommitted && PlannedDateValid && PlannedTimeValid && pricing?.IsValid == true;
    public bool CanStartNewOrder => IsCommitted && !IsBusy;

    public bool HasUncommittedDraft => !IsCommitted && (Cart.Count > 0 || SelectedFulfilment is not null || !string.IsNullOrWhiteSpace(Telephone) || !string.IsNullOrWhiteSpace(DeliveryAddress) || !string.IsNullOrWhiteSpace(Comment));

    public void ApplyCustomerDetailsFromOrder(OrderSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (HasUncommittedDraft) throw new InvalidOperationException("The current Caisse draft is not empty.");
        StartNewOrderFromCustomer(source);
    }

    public void StartNewOrderFromCustomer(OrderSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Cart.Clear(); SelectedCartLine = null; selectedFulfilment = null; plannedDate = service.BusinessDate.ToDateTime(TimeOnly.MinValue); plannedTime = null; selectedPlannedHour = null; selectedPlannedMinute = null;
        telephone = source.Telephone ?? string.Empty; deliveryAddress = source.DeliveryAddress ?? string.Empty; comment = source.Comment ?? string.Empty; pickupDiscountRequested = false; manualTotalOverride = null; pricing = null; totalText = "0.00"; isCommitted = false; reloadOrderIdText = string.Empty; reloadedOrder = null;
        activeValidationIssues = null; pricingValidationActive = false;
        OnPropertyChanged(string.Empty); _ = RepriceAsync(clearManualOverride: true);
    }
    public bool PlannedTimeValid => (SelectedPlannedHour is null && SelectedPlannedMinute is null)
        || (SelectedPlannedHour is { } && SelectedPlannedMinute is { } && PlannedTime is { } time && IsApprovedPlannedTime(time));
    public bool IsPickupDiscountEnabled => !IsBusy && !IsCommitted && SelectedFulfilment == FulfilmentMode.Retrait;
    public string TotalText { get => totalText; private set { totalText = value; OnPropertyChanged(); } }
    public string ValidationMessage
    {
        get => validationMessage;
        private set
        {
            validationMessage = value;
            if (!renderingValidationMessage)
            {
                activeValidationIssues = null;
                pricingValidationActive = false;
            }
            OnPropertyChanged();
        }
    }
    public string CommittedMessage { get => committedMessage; private set { committedMessage = value; OnPropertyChanged(); } }
    public string ReloadOrderIdText { get => reloadOrderIdText; set { reloadOrderIdText = value ?? string.Empty; OnPropertyChanged(); } }
    public OrderSnapshot? ReloadedOrder
    {
        get => reloadedOrder;
        private set
        {
            reloadedOrder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReloadedOrderDisplay));
            OnPropertyChanged(nameof(HasReloadedOrder));
        }
    }
    public string ManualTotalStateText => pricing?.ManualTotalOverrideActive == true ? manualTotalStateText : string.Empty;
    public bool IsManualTotalOverrideActive => pricing?.ManualTotalOverrideActive == true;
    public string NewOrderLabel => newOrderLabel;
    public bool HasReloadedOrder => reloadedOrder is not null;
    public string ReloadedOrderDisplay => reloadedOrder is null ? string.Empty : FormatSnapshot(reloadedOrder);

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var request = BeginRefresh(cancellationToken);
        try
        {
            var categories = await service.ListCategoriesAsync(request.Cancellation.Token);
            var products = await service.ListActiveProductsAsync(request.SearchText, request.CategoryId == Guid.Empty ? null : request.CategoryId, request.Cancellation.Token);
            if (!IsCurrent(request)) return;
            ApplyCategories(categories);
            ApplyProducts(products);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsCurrent(request)) { ValidationMessage = exception.Message; }
        finally { EndRefresh(request); }
    }

    public async Task RefreshOrderBrowserAsync(Guid? preferredOrderId = null, CancellationToken cancellationToken = default)
    {
        var request = BeginBrowserRefresh(cancellationToken);
        CancelBrowserSelection();
        var previousId = preferredOrderId ?? SelectedBrowserOrder?.Id;
        try
        {
            var date = DateOnly.FromDateTime(BrowseDate?.Date ?? service.BusinessDate.ToDateTime(TimeOnly.MinValue));
            var rows = await service.ListOrdersByPlannedDateAsync(date, request.Cancellation.Token);
            if (!IsCurrentBrowserRefresh(request)) return;

            BrowserOrders.Clear();
            foreach (var row in rows)
            {
                var item = new OrderBrowserRowViewModel(row);
                item.ApplyLocalization(localized);
                BrowserOrders.Add(item);
            }
            OnPropertyChanged(nameof(HasBrowserOrders));
            var selected = previousId is { } id ? BrowserOrders.FirstOrDefault(row => row.Id == id) : null;
            SelectedBrowserOrder = selected;
            if (selected is null && previousId is not null)
            {
                CancelBrowserSelection();
                ReloadedOrder = null;
            }
            else if (selected is not null && previousId is not null)
            {
                await SelectBrowserOrderAsync(selected, request.Cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsCurrentBrowserRefresh(request)) { ValidationMessage = exception.Message; }
        finally { EndBrowserRefresh(request); }
    }

    public async Task SelectBrowserOrderAsync(OrderBrowserRowViewModel? row, CancellationToken cancellationToken = default)
    {
        SelectedBrowserOrder = row;
        if (row is null)
        {
            CancelBrowserSelection();
            ReloadedOrder = null;
            return;
        }
        var request = BeginBrowserSelection(cancellationToken);
        try
        {
            var snapshot = await service.GetOrderByIdAsync(row.Id, request.Cancellation.Token);
            if (!IsCurrentBrowserSelection(request) || !ReferenceEquals(SelectedBrowserOrder, row)) return;
            ReloadOrderIdText = row.Id.ToString();
            ReloadedOrder = snapshot;
            ValidationMessage = snapshot is null ? Localized("OrderNotFound", "Commande introuvable.") : string.Empty;
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsCurrentBrowserSelection(request))
        {
            ReloadedOrder = null;
            ValidationMessage = exception.Message;
        }
        finally { EndBrowserSelection(request); }
    }

    public async Task AddSelectedProductAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProduct is not { } selected || IsCommitted) return;
        var product = await service.GetActiveProductAsync(selected.Id, cancellationToken);
        if (product is null) { ValidationMessage = Localized("ProductInactive", "Le produit n’est plus actif."); return; }
        PendingProduct = product;
        OnPropertyChanged(nameof(PendingProduct));
    }

    public OrderEntryProduct? PendingProduct { get; private set; }

    public void AddConfiguredLine(OrderEntryProduct product, IReadOnlyList<Guid> selectedOptionIds, IReadOnlyList<OrderLineAdjustmentDraft> customAdjustments, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (IsCommitted) return;
        var line = new OrderEntryCartLineViewModel(OrderLineDraft.Create(product.Aggregate, quantity) with
        {
            SelectedOptionIds = selectedOptionIds.ToArray(),
            CustomAdjustments = customAdjustments.ToArray(),
            CategoryName = product.CategoryName
        });
        Cart.Add(line);
        line.SetQuantityLabel(quantityLabel);
        PendingProduct = null;
        OnPropertyChanged(nameof(PendingProduct));
        _ = RepriceAsync(clearManualOverride: true);
    }

    public Task<OrderEntryProduct?> GetActiveProductForEditAsync(Guid productId, CancellationToken cancellationToken = default) => service.GetActiveProductAsync(productId, cancellationToken);
    public Task<IReadOnlyList<ProductSummary>> ListActiveProductsAsync(string? search = null, CancellationToken cancellationToken = default) => service.ListActiveProductsAsync(search, null, cancellationToken);

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
        if (IsCommitted) return;
        if (quantity <= 0)
        {
            if (quantity == 0) RemoveLine(line);
            return;
        }
        line.Replace(line.Draft with { Quantity = quantity });
        _ = RepriceAsync(clearManualOverride: true);
    }

    public void SetManualTotal(string text)
    {
        if (!M03Presentation.TryParseDecimalInput(text, out var euros))
        {
            manualTotalOverride = null;
            TotalText = (pricing?.TotalTtc ?? Money.Zero).Euros.ToString("0.00", CultureInfo.CurrentCulture);
            ValidationMessage = Localized("InvalidManualTotal", "Le total TTC saisi est invalide.");
            _ = RepriceAsync(clearManualOverride: true);
            return;
        }
        try { manualTotalOverride = Money.FromEuros(euros); }
        catch (OverflowException)
        {
            manualTotalOverride = null;
            TotalText = (pricing?.TotalTtc ?? Money.Zero).Euros.ToString("0.00", CultureInfo.CurrentCulture);
            ValidationMessage = Localized("InvalidManualTotal", "Le total TTC saisi est invalide.");
            _ = RepriceAsync(clearManualOverride: true);
            return;
        }
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
            ApplyPricing(result);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsCurrent(request)) { ValidationMessage = exception.Message; OnPropertyChanged(nameof(CanConfirm)); }
        finally { EndPrice(request); }
    }

    /// <summary>
    /// Applies a successful M03 settings save to an uncommitted draft. The before/after
    /// comparison intentionally uses normal pricing (without a manual override), so an
    /// unchanged or irrelevant settings save preserves an operator-entered authoritative
    /// total while a pricing-relevant change restores the current normal calculation.
    /// </summary>
    public void ApplySettingsSaved(BusinessSettings previous, BusinessSettings current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        if (IsCommitted) return;

        InvalidatePendingPrice();
        var normalDraft = BuildDraft() with { ManualTotalOverride = null };
        var before = OrderEntryService.PriceWithSettings(normalDraft, previous);
        var after = OrderEntryService.PriceWithSettings(normalDraft, current);
        if (!PricingOutcomeEquals(before, after)) manualTotalOverride = null;
        ApplyPricing(OrderEntryService.PriceWithSettings(BuildDraft(), current));
    }

    public async Task<ConfirmOrderResult?> ConfirmAsync(CancellationToken cancellationToken = default)
    {
        if (disposed || IsCommitted) return null;
        if (!PlannedTimeValid)
        {
            var invalidTime = ConfirmOrderResult.Failure(new ValidationIssue("planned-time", "The planned fulfilment time is not valid.", ValidationCodes.PlannedTimeInvalid));
            SetValidationIssues(invalidTime.Issues);
            return invalidTime;
        }
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeCancellation.Token);
        var operationToken = operationCancellation.Token;
        IsBusy = true;
        try
        {
            var result = await service.ConfirmNewOrderAsync(BuildDraft(), operationToken);
            if (disposed) return result;
            if (!result.Succeeded)
            {
                SetValidationIssues(result.Issues);
                return result;
            }
            IsCommitted = true;
            var committedId = result.CommittedOrder?.Id ?? result.PersistedOrderId;
            var committedLabel = !string.IsNullOrWhiteSpace(result.CommittedOrder?.Reference)
                ? result.CommittedOrder!.Reference
                : committedId?.ToString() ?? "—";
            if (committedId is { } id)
            {
                ReloadOrderIdText = id.ToString();
                ReloadedOrder = result.CommittedOrder;
                if (result.CommittedOrder is { } committed && BrowseDate is { } currentBrowseDate && committed.PlannedFulfilmentDate == DateOnly.FromDateTime(currentBrowseDate.Date))
                {
                    // Keep older rows and select the new persisted row when the browser is
                    // already showing the same planned business date.
                    try { await RefreshOrderBrowserAsync(committed.Id, operationToken); }
                    catch (OperationCanceledException) when (operationToken.IsCancellationRequested) { throw; }
                }
            }
            CommittedMessage = result.HasOutputFailure
                ? string.Format(CultureInfo.CurrentCulture, Localized("OrderSavedOutputFailed", "Commande {0} enregistrée ; l’envoi de sortie a échoué."), committedLabel)
                : string.Format(CultureInfo.CurrentCulture, Localized("OrderSaved", "Commande {0} enregistrée."), committedLabel);
            ValidationMessage = result.HasOutputFailure ? string.Join(Environment.NewLine, result.Issues.Select(LocalizeIssue)) : string.Empty;
            return result;
        }
        catch (OperationCanceledException) when (disposed || lifetimeCancellation.IsCancellationRequested) { return null; }
        finally { if (!disposed) IsBusy = false; }
    }

    public async Task<bool> ReloadOrderAsync(CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(ReloadOrderIdText.Trim(), out var id)) { ReloadedOrder = null; ValidationMessage = Localized("InvalidOrderId", "Identifiant de commande invalide."); return false; }
        ReloadedOrder = await service.GetOrderByIdAsync(id, cancellationToken);
        if (ReloadedOrder is null) ValidationMessage = Localized("OrderNotFound", "Commande introuvable.");
        else ValidationMessage = string.Empty;
        return ReloadedOrder is not null;
    }

    public void StartNewOrder()
    {
        if (!CanStartNewOrder) return;
        CancelBrowserSelection();
        lock (priceLock) priceCancellation?.Cancel();
        Cart.Clear();
        SelectedCartLine = null;
        selectedFulfilment = null;
        plannedDate = service.BusinessDate.ToDateTime(TimeOnly.MinValue);
        plannedTime = null;
        selectedPlannedHour = null;
        selectedPlannedMinute = null;
        telephone = string.Empty;
        deliveryAddress = string.Empty;
        comment = string.Empty;
        pickupDiscountRequested = false;
        manualTotalOverride = null;
        pricing = null;
        activeValidationIssues = null;
        pricingValidationActive = false;
        totalText = "0.00";
        validationMessage = string.Empty;
        committedMessage = string.Empty;
        reloadOrderIdText = string.Empty;
        reloadedOrder = null;
        isCommitted = false;
        PendingProduct = null;
        OnPropertyChanged(string.Empty);
        _ = RepriceAsync(clearManualOverride: true);
    }

    private NewOrderDraft BuildDraft() => new(
        Cart.Select(line => line.Draft).ToArray(), SelectedFulfilment,
        PlannedDate is { } date ? DateOnly.FromDateTime(date.Date) : null,
        PlannedTime is { } time ? TimeOnly.FromTimeSpan(time) : null,
        Telephone, DeliveryAddress, Comment, PickupDiscountRequested, manualTotalOverride);

    private string Localized(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;

    private static IEnumerable<TimeChoice> BuildTimeChoices(IEnumerable<int> values)
    {
        yield return new TimeChoice(null, "—");
        foreach (var value in values)
            yield return new TimeChoice(value, value.ToString("D2", CultureInfo.InvariantCulture));
    }

    private static bool IsApprovedPlannedTime(TimeSpan time) =>
        time >= TimeSpan.Zero && time < TimeSpan.FromDays(1) &&
        time.Seconds == 0 && time.Milliseconds == 0 &&
        time.Hours is 11 or 12 or 13 or 14 or 18 or 19 or 20 or 21 or 22 &&
        time.Minutes % 5 == 0;

    private void UpdatePlannedTime()
    {
        PlannedTime = selectedPlannedHour is { } hour && selectedPlannedMinute is { } minute
            ? new TimeSpan(hour, minute, 0)
            : null;
    }

    private void ApplyPricing(OrderPricingResult result)
    {
        pricing = result;
        TotalText = pricing.TotalTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
        for (var index = 0; index < Math.Min(Cart.Count, pricing.Lines.Count); index++) Cart[index].SetLineTotal(pricing.Lines[index].CalculatedLineTotalTtc);
        pricingValidationActive = true;
        activeValidationIssues = null;
        RenderPricingValidationMessage();
        OnPropertyChanged(nameof(IsManualTotalOverrideActive));
        OnPropertyChanged(nameof(ManualTotalStateText));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void SetValidationIssues(IReadOnlyList<ValidationIssue> issues)
    {
        activeValidationIssues = issues;
        pricingValidationActive = false;
        if (issues.Count == 0) ValidationMessage = string.Empty;
        else RenderValidationIssues();
    }

    private void RenderValidationIssues()
    {
        if (activeValidationIssues is not { Count: > 0 }) return;
        RenderValidationMessage(string.Join(Environment.NewLine, activeValidationIssues.Select(LocalizeIssue)));
    }

    private void RenderPricingValidationMessage()
    {
        if (!pricingValidationActive || pricing is null) return;
        var messages = pricing.ValidationErrors
            .Concat(pricing.DiscountNotAppliedReason is { } reason && PickupDiscountRequested ? [reason] : [])
            .Select(message => LocalizeOrderMessage(message))
            .ToList();
        if (!PlannedDateValid && PlannedDate is not null)
            messages.Insert(0, Localized("ValidationPlannedDatePast", "La date prévue ne peut pas être antérieure à la date d’activité."));
        if (!PlannedTimeValid)
            messages.Insert(0, Localized("ValidationPlannedTimeInvalid", "L’heure prévue doit utiliser un créneau autorisé de 5 minutes."));
        RenderValidationMessage(string.Join(Environment.NewLine, messages));
    }

    private void RenderValidationMessage(string message)
    {
        renderingValidationMessage = true;
        try { ValidationMessage = message; }
        finally { renderingValidationMessage = false; }
    }

    private static bool PricingOutcomeEquals(OrderPricingResult left, OrderPricingResult right) =>
        left.IsValid == right.IsValid &&
        left.ValidationErrors.SequenceEqual(right.ValidationErrors, StringComparer.Ordinal) &&
        left.TotalBeforeManualOverride == right.TotalBeforeManualOverride &&
        left.TotalTtc == right.TotalTtc &&
        left.PickupDiscountRequested == right.PickupDiscountRequested &&
        left.PickupDiscountApplied == right.PickupDiscountApplied &&
        left.PickupDiscountRate == right.PickupDiscountRate &&
        string.Equals(left.DiscountNotAppliedReason, right.DiscountNotAppliedReason, StringComparison.Ordinal) &&
        left.DeliveryCommercialAmountTtc == right.DeliveryCommercialAmountTtc &&
        left.DeliveryFeeTtc == right.DeliveryFeeTtc &&
        left.Lines.Count == right.Lines.Count &&
        left.Lines.Zip(right.Lines).All(pair => LinePricingEquals(pair.First, pair.Second)) &&
        left.TaxBreakdown.Count == right.TaxBreakdown.Count &&
        left.TaxBreakdown.Zip(right.TaxBreakdown).All(pair =>
            pair.First.VatRate == pair.Second.VatRate &&
            pair.First.TaxableTtc == pair.Second.TaxableTtc &&
            pair.First.IncludedVatTtc == pair.Second.IncludedVatTtc);

    private static bool LinePricingEquals(OrderLinePricing left, OrderLinePricing right) =>
        left.ExtendedBaseTtc == right.ExtendedBaseTtc &&
        left.ExtendedAdjustmentTtc == right.ExtendedAdjustmentTtc &&
        left.CalculatedLineTotalTtc == right.CalculatedLineTotalTtc &&
        left.ProductVatComponentTtc == right.ProductVatComponentTtc &&
        left.PositiveAdjustmentComponentTtc == right.PositiveAdjustmentComponentTtc &&
        left.DiscountTtc == right.DiscountTtc &&
        left.Adjustments.Count == right.Adjustments.Count &&
        left.Adjustments.Zip(right.Adjustments).All(pair =>
            pair.First.OptionId == pair.Second.OptionId &&
            string.Equals(pair.First.GroupName, pair.Second.GroupName, StringComparison.Ordinal) &&
            string.Equals(pair.First.Label, pair.Second.Label, StringComparison.Ordinal) &&
            pair.First.AmountTtcPerUnit == pair.Second.AmountTtcPerUnit &&
            pair.First.VatRate == pair.Second.VatRate &&
            pair.First.Kind == pair.Second.Kind &&
            pair.First.DisplayOrder == pair.Second.DisplayOrder);

    private string LocalizeOrderMessage(string message)
    {
        if (message.Contains("fulfilment mode", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationFulfilmentRequired", "Le mode de commande est obligatoire.");
        if (message.Contains("cannot be in the past", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationPlannedDatePast", "La date prévue ne peut pas être antérieure à la date d’activité.");
        if (message.Contains("planned fulfilment date", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationPlannedDateRequired", "La date prévue est obligatoire.");
        if (message.Contains("planned fulfilment time", StringComparison.OrdinalIgnoreCase) && message.Contains("required", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationPlannedTimeRequired", "Sélectionnez une heure prévue.");
        if (message.Contains("planned fulfilment time", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationPlannedTimeInvalid", "L’heure prévue doit utiliser un créneau autorisé de 5 minutes.");
        if (message.Contains("at least one order line", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationCartRequired", "Ajoutez au moins une ligne.");
        if (message.Contains("quantity", StringComparison.OrdinalIgnoreCase)) return Localized("InvalidQuantity", "La quantité doit être un entier positif.");
        if (message.Contains("no longer active", StringComparison.OrdinalIgnoreCase) || message.Contains("current Catalogue", StringComparison.OrdinalIgnoreCase)) return Localized("ProductInactive", "Le produit n’est plus actif.");
        if (message.Contains("option", StringComparison.OrdinalIgnoreCase) || message.Contains("selection", StringComparison.OrdinalIgnoreCase)) return Localized("InvalidOptions", "La sélection d’options n’est plus valide.");
        if (message.Contains("custom adjustment", StringComparison.OrdinalIgnoreCase)) return Localized("InvalidAdjustment", "Chaque ajustement doit avoir un libellé et un montant valides.");
        if (message.Contains("delivery merchandise", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationDeliveryMinimum", "Le minimum Livraison n’est pas atteint.");
        if (message.Contains("discounted total", StringComparison.OrdinalIgnoreCase) || message.Contains("eligible product", StringComparison.OrdinalIgnoreCase)) return Localized("ValidationPickupDiscount", "La remise Retrait n’a pas été appliquée.");
        return message;
    }

    private string LocalizeIssue(ValidationIssue issue) => issue.StableCode == ValidationCodes.Busy
        ? Localized("ValidationBusy", "Une opération est déjà en cours.")
        : LocalizeOrderMessage(issue.Message);

    private string FormatSnapshot(OrderSnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1}", Localized("OrderId", "ID commande"), snapshot.Id));
        builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1}", Localized("OrderStatus", "Statut"), LocalizeStatus(snapshot.Status)));
        builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1}", Localized("Fulfilment", "Mode"), LocalizeFulfilment(snapshot.Fulfilment)));
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1:yyyy-MM-dd}", Localized("PlannedDate", "Date prévue"), snapshot.PlannedFulfilmentDate));
        if (snapshot.PlannedFulfilmentTime is { } time) builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}: {1}", Localized("PlannedTime", "Heure prévue"), OrderTimeFormatting.Format(time)));
        AppendOptional(builder, "Telephone", snapshot.Telephone);
        AppendOptional(builder, "DeliveryAddress", snapshot.DeliveryAddress);
        AppendOptional(builder, "Comment", snapshot.Comment);
        builder.AppendLine(Localized("OrderLines", "Lignes") + ":");
        foreach (var item in snapshot.Items.OrderBy(item => item.Position))
        {
            builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "  {0}. {1} — {2} × {3}: {4:0.00} €", item.Position + 1, item.ProductCode, item.ProductName, item.Quantity, item.CalculatedLineTotalTtc.Euros));
            foreach (var adjustment in item.Adjustments.OrderBy(adjustment => adjustment.DisplayOrder))
                builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "     + {0}: {1:+0.00;-0.00;0.00} €/{2}", adjustment.Label, adjustment.AdjustmentTtcPerUnit.Euros, Localized("Unit", "unité")));
        }
        builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1:0.00} €", Localized("TotalTtc", "Total TTC"), snapshot.TotalTtc.Euros));
        if (snapshot.ManualTotalOverrideActive) builder.AppendLine(Localized("ManualTotalActive", "Total manuel faisant autorité"));
        if (snapshot.PickupDiscountApplied) builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1:P2}", Localized("PickupDiscountApplied", "Remise Retrait"), snapshot.PickupDiscountRate));
        if (snapshot.DeliveryFeeTtc != Money.Zero) builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1:0.00} €", Localized("DeliveryFee", "Frais de livraison"), snapshot.DeliveryFeeTtc.Euros));
        builder.AppendLine(Localized("TaxSnapshot", "TVA enregistrée") + ":");
        foreach (var tax in snapshot.TaxBreakdown.OrderBy(tax => tax.VatRate)) builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "  {0:0.##}%: {1:0.00} € TTC / {2:0.00} € TVA", tax.VatRate, tax.TaxableTtc.Euros, tax.IncludedVatTtc.Euros));
        return builder.ToString().TrimEnd();
    }

    private void AppendOptional(StringBuilder builder, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) builder.AppendLine(string.Format(CultureInfo.CurrentCulture, "{0}: {1}", Localized(key, key), value));
    }

    private string LocalizeStatus(OrderStatus status) => status switch
    {
        OrderStatus.Open => Localized("OrderStatusOpen", "Ouverte"),
        OrderStatus.Closed => Localized("OrderStatusClosed", "Clôturée"),
        OrderStatus.Cancelled => Localized("OrderStatusCancelled", "Annulée"),
        _ => status.ToString()
    };

    private string LocalizeFulfilment(FulfilmentMode mode) => mode == FulfilmentMode.Retrait
        ? retraitFulfilmentLabel
        : livraisonFulfilmentLabel;

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetimeCancellation.Cancel();
        lock (refreshLock) refreshCancellation?.Cancel();
        lock (priceLock) priceCancellation?.Cancel();
        lock (browserLock)
        {
            browserCancellation?.Cancel();
            browserSelectionCancellation?.Cancel();
        }
        lifetimeCancellation.Dispose();
        service.Dispose();
    }

    private BrowserRefreshRequest BeginBrowserRefresh(CancellationToken externalCancellationToken)
    {
        lock (browserLock)
        {
            browserCancellation?.Cancel();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            browserCancellation = cancellation;
            return new(++browserVersion, cancellation);
        }
    }

    private bool IsCurrentBrowserRefresh(BrowserRefreshRequest request)
    {
        lock (browserLock) return !disposed && request.Version == browserVersion && ReferenceEquals(request.Cancellation, browserCancellation);
    }

    private void EndBrowserRefresh(BrowserRefreshRequest request)
    {
        lock (browserLock)
        {
            if (ReferenceEquals(request.Cancellation, browserCancellation)) browserCancellation = null;
        }
        request.Cancellation.Dispose();
    }

    private BrowserSelectionRequest BeginBrowserSelection(CancellationToken externalCancellationToken)
    {
        lock (browserLock)
        {
            browserSelectionCancellation?.Cancel();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
            browserSelectionCancellation = cancellation;
            return new(++browserSelectionVersion, cancellation);
        }
    }

    private bool IsCurrentBrowserSelection(BrowserSelectionRequest request)
    {
        lock (browserLock) return !disposed && request.Version == browserSelectionVersion && ReferenceEquals(request.Cancellation, browserSelectionCancellation);
    }

    private void EndBrowserSelection(BrowserSelectionRequest request)
    {
        lock (browserLock)
        {
            if (ReferenceEquals(request.Cancellation, browserSelectionCancellation)) browserSelectionCancellation = null;
        }
        request.Cancellation.Dispose();
    }

    private void CancelBrowserSelection()
    {
        lock (browserLock)
        {
            browserSelectionVersion++;
            browserSelectionCancellation?.Cancel();
        }
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

    private void InvalidatePendingPrice()
    {
        lock (priceLock)
        {
            priceCancellation?.Cancel();
            priceCancellation = null;
            priceVersion++;
        }
    }

    private readonly record struct RefreshRequest(long Version, string SearchText, Guid CategoryId, CancellationTokenSource Cancellation);
    private readonly record struct PriceRequest(long Version, CancellationTokenSource Cancellation);
    private readonly record struct BrowserRefreshRequest(long Version, CancellationTokenSource Cancellation);
    private readonly record struct BrowserSelectionRequest(long Version, CancellationTokenSource Cancellation);
    private void ApplyCategories(IReadOnlyList<CategorySummary> categories)
    {
        var desired = new CategorySummary[1 + categories.Count];
        desired[0] = new CategorySummary(Guid.Empty, AllCategoriesLabel);
        for (var index = 0; index < categories.Count; index++) desired[index + 1] = categories[index];

        if (Categories.Count == desired.Length && Categories.SequenceEqual(desired)) return;

        var previousSelectedCategoryId = selectedCategoryId;
        Categories.Clear();
        foreach (var category in desired) Categories.Add(category);

        if (previousSelectedCategoryId != Guid.Empty && !categories.Any(category => category.Id == previousSelectedCategoryId))
        {
            selectedCategoryId = Guid.Empty;
        }
        else
        {
            selectedCategoryId = previousSelectedCategoryId;
        }
        OnPropertyChanged(nameof(SelectedCategoryId));
    }

    private void ApplyProducts(IReadOnlyList<ProductSummary> products)
    {
        Products.Clear();
        foreach (var product in products) Products.Add(product);
        if (SelectedProduct is not null) SelectedProduct = Products.FirstOrDefault(product => product.Id == SelectedProduct.Id);
    }

    private async Task RefreshProductsAsync()
    {
        var request = BeginRefresh(CancellationToken.None);
        try
        {
            var products = await service.ListActiveProductsAsync(request.SearchText, request.CategoryId == Guid.Empty ? null : request.CategoryId, request.Cancellation.Token);
            if (!IsCurrent(request)) return;
            ApplyProducts(products);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (IsCurrent(request)) { ValidationMessage = exception.Message; }
        finally { EndRefresh(request); }
    }
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class OrderEntryCartLineViewModel : INotifyPropertyChanged
{
    private OrderLineDraft draft;
    private string lineTotalText = "0.00";
    private string quantityLabel = "Quantité";
    public OrderEntryCartLineViewModel(OrderLineDraft draft) => this.draft = draft;
    public event PropertyChangedEventHandler? PropertyChanged;
    public OrderLineDraft Draft => draft;
    public string DisplayName => $"{draft.Product.Product.Code} — {draft.Product.Product.Name}";
    public string Configuration => string.Join(", ", draft.SelectedOptionIds.Select(id => draft.Product.OptionsByGroup.Values.SelectMany(options => options).FirstOrDefault(option => option.Id == id)?.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Concat(draft.CustomAdjustments.Select(adjustment => adjustment.Label)));
    public int Quantity => draft.Quantity;
    public string QuantityDisplayText => $"{quantityLabel}: {Quantity}";
    public string LineTotalText => lineTotalText;
    public void SetQuantityLabel(string value) { quantityLabel = string.IsNullOrWhiteSpace(value) ? "Quantité" : value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(QuantityDisplayText))); }
    public void Replace(OrderLineDraft next) { draft = next; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); }
    public void SetLineTotal(Money total) { lineTotalText = total.Euros.ToString("0.00", CultureInfo.CurrentCulture); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LineTotalText))); }
}
