using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.OrderEntry;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

public sealed class OrderManagementRowViewModel(OrderBrowserRow row) : INotifyPropertyChanged
{
    private IReadOnlyDictionary<string, string> localized = new Dictionary<string, string>();
    public event PropertyChangedEventHandler? PropertyChanged;
    public OrderBrowserRow Row { get; } = row ?? throw new ArgumentNullException(nameof(row));
    public Guid Id => Row.Id;
    public string ReferenceText => string.IsNullOrWhiteSpace(Row.Reference) ? Row.Id.ToString("N")[..8] : Row.Reference;
    public string PlannedDateText => Row.PlannedFulfilmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string PlannedTimeText => OrderTimeFormatting.Format(Row.PlannedFulfilmentTime);
    public string FulfilmentText => Row.Fulfilment == FulfilmentMode.Retrait ? Text("Retrait", "Retrait") : Text("Livraison", "Livraison");
    public string StatusText => Row.Status switch { OrderStatus.Open => Text("OrderStatusOpen", "Ouverte"), OrderStatus.Closed => Text("OrderStatusClosed", "Clôturée"), _ => Text("OrderStatusCancelled", "Annulée") };
    public string TotalText => Row.TotalTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string CardText => Row.CardPaymentTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string CashText => Row.CashPaymentTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string DifferenceText => Row.DifferenceTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string TelephoneText => Row.Telephone ?? Text("OrderBrowserEmptyTelephone", "—");
    public void ApplyLocalization(IReadOnlyDictionary<string, string> values)
    {
        localized = values ?? new Dictionary<string, string>();
        foreach (var name in new[] { nameof(FulfilmentText), nameof(StatusText), nameof(TelephoneText), nameof(TotalText), nameof(CardText), nameof(CashText), nameof(DifferenceText) }) PropertyChanged?.Invoke(this, new(name));
    }
    private string Text(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;
}

public sealed class OrderDetailLineViewModel(OrderItemSnapshot item) : INotifyPropertyChanged
{
    private int quantity = item.Quantity;
    public event PropertyChangedEventHandler? PropertyChanged;
    public OrderItemSnapshot Item { get; private set; } = item ?? throw new ArgumentNullException(nameof(item));
    public string ProductText => $"{Item.ProductCode} — {Item.ProductName}";
    public string OptionsText => string.Join(", ", Item.Adjustments.Select(adjustment => adjustment.Label));
    public int Quantity { get => quantity; set { if (value <= 0 || quantity == value) return; quantity = value; PropertyChanged?.Invoke(this, new(nameof(Quantity))); } }
    public OrderItemSnapshot ToSnapshot()
    {
        var unitBase = Item.Quantity == 0 ? Money.Zero : Money.FromCents(Item.ExtendedBaseTtc.Cents / Item.Quantity);
        var adjustmentUnit = Item.Adjustments.Aggregate(Money.Zero, (sum, adjustment) => sum + adjustment.AdjustmentTtcPerUnit);
        return Item with { Quantity = quantity, ExtendedBaseTtc = unitBase * quantity, CalculatedLineTotalTtc = unitBase * quantity + adjustmentUnit * quantity };
    }
    public OrderLineDraft ToCurrentDraft(OrderEntryProduct product) => new(
        Item.Id, product.Aggregate, Item.Adjustments.Where(adjustment => adjustment.SourceOptionId is not null).Select(adjustment => adjustment.SourceOptionId!.Value).ToArray(),
        Item.Adjustments.Where(adjustment => adjustment.Kind == OrderAdjustmentKind.CustomAdjustment).Select(adjustment => new OrderLineAdjustmentDraft(
            null, adjustment.GroupName, adjustment.Label, adjustment.AdjustmentTtcPerUnit, OrderAdjustmentKind.CustomAdjustment, adjustment.DisplayOrder)).ToArray(), quantity, Item.CategoryName);
    public void SetPosition(int position) { Item = Item with { Position = position }; }
}

/// <summary>Presentation state for the dedicated M05 Commandes master/detail workflow.</summary>
public sealed class OrderLifecycleShellViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly OrderLifecycleService service;
    private readonly object refreshLock = new();
    private CancellationTokenSource? refreshCancellation;
    private long refreshVersion;
    private string searchText = string.Empty;
    private DateTime? browseDate;
    private OrderManagementRowViewModel? selectedRow;
    private OrderSnapshot? selectedOrder;
    private bool isEditing;
    private string editTelephone = string.Empty;
    private string editAddress = string.Empty;
    private string editComment = string.Empty;
    private string editCard = "0.00";
    private string editCash = "0.00";
    private string editTotal = "0.00";
    private FulfilmentMode? editFulfilment;
    private DateTime? editPlannedDate;
    private int? editPlannedHour;
    private int? editPlannedMinute;
    private bool editPickupDiscountRequested;
    private DateTime? effectivePaymentDate;
    private string validationMessage = string.Empty;
    private IReadOnlyDictionary<string, string> localized = new Dictionary<string, string>();
    private bool disposed;
    private OperationalOrderView? operationalView;
    private OrderOperationalSummary summary = new(Money.Zero, Money.Zero, Money.Zero, Money.Zero, 0, 0, 0);

    public OrderLifecycleShellViewModel(OrderLifecycleService service)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        browseDate = service.BusinessDate.ToDateTime(TimeOnly.MinValue);
        effectivePaymentDate = service.BusinessDate.ToDateTime(TimeOnly.MinValue);
        Orders = new ObservableCollection<OrderManagementRowViewModel>();
        DetailLines = new ObservableCollection<OrderDetailLineViewModel>();
        FulfilmentChoices = new ObservableCollection<FulfilmentChoice>
        {
            new(FulfilmentMode.Retrait, "Retrait"),
            new(FulfilmentMode.Livraison, "Livraison")
        };
        PlannedHourChoices = new ObservableCollection<TimeChoice>(BuildTimeChoices([11, 12, 13, 14, 18, 19, 20, 21, 22]));
        PlannedMinuteChoices = new ObservableCollection<TimeChoice>(BuildTimeChoices([0, 5, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55]));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<OrderSnapshot>? ReuseCustomerRequested;
    public ObservableCollection<OrderManagementRowViewModel> Orders { get; }
    public ObservableCollection<OrderDetailLineViewModel> DetailLines { get; }
    public ObservableCollection<FulfilmentChoice> FulfilmentChoices { get; }
    public ObservableCollection<TimeChoice> PlannedHourChoices { get; }
    public ObservableCollection<TimeChoice> PlannedMinuteChoices { get; }
    public string SearchText { get => searchText; set { if (searchText == value) return; searchText = value; OnPropertyChanged(); } }
    public DateTime? BrowseDate { get => browseDate; set { var next = (value ?? service.BusinessDate.ToDateTime(TimeOnly.MinValue)).Date; if (browseDate?.Date == next) return; browseDate = next; OnPropertyChanged(); _ = RefreshAsync(); } }
    public OrderManagementRowViewModel? SelectedRow { get => selectedRow; set { if (ReferenceEquals(selectedRow, value)) return; selectedRow = value; OnPropertyChanged(); if (value is not null) _ = SelectAsync(value); } }
    public OrderSnapshot? SelectedOrder { get => selectedOrder; private set { selectedOrder = value; OnPropertyChanged(); RaiseDetailProperties(); } }
    public bool IsEditing { get => isEditing; private set { if (isEditing == value) return; isEditing = value; OnPropertyChanged(); RaiseCommandProperties(); } }
    public string EditTelephone { get => editTelephone; set { editTelephone = value; OnPropertyChanged(); } }
    public string EditAddress { get => editAddress; set { editAddress = value; OnPropertyChanged(); } }
    public string EditComment { get => editComment; set { editComment = value; OnPropertyChanged(); } }
    public string EditCard { get => editCard; set { editCard = value; OnPropertyChanged(); RaiseCommandProperties(); RaiseEditPaymentProperties(); } }
    public string EditCash { get => editCash; set { editCash = value; OnPropertyChanged(); RaiseCommandProperties(); RaiseEditPaymentProperties(); } }
    public string EditTotal { get => editTotal; set { editTotal = value; OnPropertyChanged(); RaiseCommandProperties(); RaiseEditPaymentProperties(); } }
    public FulfilmentMode? EditFulfilment
    {
        get => editFulfilment;
        set
        {
            if (editFulfilment == value) return;
            editFulfilment = value;
            if (value != FulfilmentMode.Retrait) editPickupDiscountRequested = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EditPickupDiscountRequested));
            OnPropertyChanged(nameof(IsEditPickupDiscountEnabled));
            RaiseCommandProperties();
        }
    }
    public DateTime? EditPlannedDate { get => editPlannedDate; set { editPlannedDate = value; OnPropertyChanged(); RaiseCommandProperties(); } }
    public DateTime MinimumEditPlannedDate => SelectedOrder is { PlannedFulfilmentDate: var plannedDate } && plannedDate < service.BusinessDate
        ? plannedDate.ToDateTime(TimeOnly.MinValue)
        : service.BusinessDate.ToDateTime(TimeOnly.MinValue);
    public int? EditPlannedHour { get => editPlannedHour; set { editPlannedHour = value; OnPropertyChanged(); RaiseCommandProperties(); } }
    public int? EditPlannedMinute { get => editPlannedMinute; set { editPlannedMinute = value; OnPropertyChanged(); RaiseCommandProperties(); } }
    public bool EditPickupDiscountRequested
    {
        get => editPickupDiscountRequested;
        set
        {
            var next = value && EditFulfilment == FulfilmentMode.Retrait;
            if (editPickupDiscountRequested == next) return;
            editPickupDiscountRequested = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditPickupDiscountEnabled));
            RaiseCommandProperties();
        }
    }
    public bool IsEditPickupDiscountEnabled => IsEditing && EditFulfilment == FulfilmentMode.Retrait;
    public DateTime? EffectivePaymentDate { get => effectivePaymentDate; set { effectivePaymentDate = value; OnPropertyChanged(); } }
    public string ValidationMessage { get => validationMessage; private set { validationMessage = value; OnPropertyChanged(); } }
    public bool HasSelectedOrder => SelectedOrder is not null;
    public DateOnly BusinessDate => service.BusinessDate;
    public string ReferenceText => SelectedOrder?.Reference ?? string.Empty;
    public string StatusText => SelectedOrder is null ? string.Empty : LocalizeStatus(SelectedOrder.Status);
    public string FulfilmentText => SelectedOrder is null ? string.Empty : SelectedOrder.Fulfilment == FulfilmentMode.Retrait ? Text("Retrait", "Retrait") : Text("Livraison", "Livraison");
    public string AdvanceText => SelectedOrder?.AdvanceOrderMarker == true ? Text("OrderAdvance", "Commande anticipée") : string.Empty;
    public string ManualTotalText => SelectedOrder?.ManualTotalOverrideActive == true ? Text("ManualTotalActive", "Total TTC manuel") : string.Empty;
    public string TaxSummaryText => SelectedOrder is null ? string.Empty : string.Join(" · ", SelectedOrder.TaxBreakdown.OrderBy(tax => tax.VatRate).Select(tax => $"{tax.VatRate:0.#}% {tax.IncludedVatTtc.Euros:0.00} €"));
    public string PlannedDateText => SelectedOrder?.PlannedFulfilmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
    public string PlannedTimeText => OrderTimeFormatting.Format(SelectedOrder?.PlannedFulfilmentTime);
    public string TotalText => SelectedOrder?.TotalTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture) ?? string.Empty;
    public string PaidText => SelectedOrder is null ? string.Empty : OrderPaymentState.From(SelectedOrder).Total.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string DifferenceText => SelectedOrder is null ? string.Empty : OrderPaymentState.From(SelectedOrder).Difference.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string EditPaidText => TryParse(EditCard, out var card) && TryParse(EditCash, out var cash)
        ? (card + cash).Euros.ToString("0.00", CultureInfo.CurrentCulture)
        : string.Empty;
    public string EditDifferenceText => TryParse(EditTotal, out var total) && TryParse(EditCard, out var card) && TryParse(EditCash, out var cash)
        ? (total - card - cash).Euros.ToString("0.00", CultureInfo.CurrentCulture)
        : string.Empty;
    public string EditCloseEligibilityText
    {
        get
        {
            if (!IsEditing) return string.Empty;
            if (!TryParse(EditTotal, out var total) || !TryParse(EditCard, out var card) || !TryParse(EditCash, out var cash)) return string.Empty;
            if (card < Money.Zero || cash < Money.Zero) return Text("ValidationPaymentNegative", "Les montants encaissés ne peuvent pas être négatifs.");
            return total - card - cash == Money.Zero ? Text("OrderCloseEligible", "Clôture possible") : string.Empty;
        }
    }
    public string TelephoneText => SelectedOrder?.Telephone ?? string.Empty;
    public string AddressText => SelectedOrder?.DeliveryAddress ?? string.Empty;
    public string CommentText => SelectedOrder?.Comment ?? string.Empty;
    public bool CanModify => SelectedOrder is { Status: not OrderStatus.Cancelled } && !IsEditing;
    public bool CanSave => SelectedOrder is not null && IsEditing && DetailLines.Count > 0 && EditFulfilment is not null && EditPlannedDate is not null && TryParse(EditTotal, out var total) && TryParse(EditCard, out var card) && TryParse(EditCash, out var cash) && total >= Money.Zero && card >= Money.Zero && cash >= Money.Zero;
    public bool CanAbandon => IsEditing;
    public bool CanClose => SelectedOrder is { Status: not OrderStatus.Cancelled } && !IsEditing && OrderPaymentState.From(SelectedOrder).IsExactlyReconciled;
    public bool CanCancel => SelectedOrder is { Status: not OrderStatus.Cancelled } && !IsEditing;
    public bool CanReuseCustomer => SelectedOrder is not null;
    public bool CanAddCurrentLine => IsEditing && SelectedOrder is not null;
    public string DashboardTurnoverText => summary.TurnoverTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string DashboardReceivedText => summary.ReceivedTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string DashboardReceivedCardText => summary.ReceivedCardTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public string DashboardReceivedCashText => summary.ReceivedCashTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture);
    public int FutureOrderCount => summary.FutureOrderCount;
    public int DueTodayAdvanceOrderCount => summary.DueTodayAdvanceOrderCount;
    public int OverdueUnsettledOrderCount => summary.OverdueUnsettledOrderCount;

    public void ApplyLocalization(IReadOnlyDictionary<string, string> values)
    {
        localized = values ?? new Dictionary<string, string>();
        FulfilmentChoices.Clear();
        FulfilmentChoices.Add(new(FulfilmentMode.Retrait, Text("Retrait", "Retrait")));
        FulfilmentChoices.Add(new(FulfilmentMode.Livraison, Text("Livraison", "Livraison")));
        foreach (var row in Orders) row.ApplyLocalization(localized);
        RaiseDetailProperties();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? previous;
        long version;
        lock (refreshLock)
        {
            previous = refreshCancellation;
            refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            version = ++refreshVersion;
        }
        previous?.Cancel(); previous?.Dispose();
        try
        {
            var rows = string.IsNullOrWhiteSpace(SearchText)
                ? operationalView is { } view
                    ? await service.ListOperationalAsync(view, service.BusinessDate, refreshCancellation!.Token)
                    : await service.BrowseByPlannedDateAsync(DateOnly.FromDateTime(BrowseDate?.Date ?? service.BusinessDate.ToDateTime(TimeOnly.MinValue)), refreshCancellation!.Token)
                : await service.SearchLiveAsync(SearchText, refreshCancellation!.Token);
            lock (refreshLock) if (version != refreshVersion) return;
            Orders.Clear(); foreach (var row in rows) { var vm = new OrderManagementRowViewModel(row); vm.ApplyLocalization(localized); Orders.Add(vm); }
            if (selectedRow is not null) selectedRow = Orders.FirstOrDefault(row => row.Id == selectedRow.Id);
            OnPropertyChanged(nameof(SelectedRow));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { ValidationMessage = exception.Message; }
    }

    public async Task RefreshDashboardAsync(CancellationToken cancellationToken = default)
    {
        summary = await service.GetOperationalSummaryAsync(service.BusinessDate, cancellationToken);
        foreach (var name in new[] { nameof(DashboardTurnoverText), nameof(DashboardReceivedText), nameof(DashboardReceivedCardText), nameof(DashboardReceivedCashText), nameof(FutureOrderCount), nameof(DueTodayAdvanceOrderCount), nameof(OverdueUnsettledOrderCount) }) OnPropertyChanged(name);
    }

    public void SelectOperationalView(string? view)
    {
        SearchText = string.Empty;
        operationalView = view switch { "future" => OperationalOrderView.Future, "due" => OperationalOrderView.DueToday, "overdue" => OperationalOrderView.OverdueUnsettled, _ => null };
        BrowseDate = view switch
        {
            "future" => service.BusinessDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            "due" => service.BusinessDate.ToDateTime(TimeOnly.MinValue),
            "overdue" => service.BusinessDate.AddDays(-1).ToDateTime(TimeOnly.MinValue),
            _ => service.BusinessDate.ToDateTime(TimeOnly.MinValue)
        };
    }

    public async Task SelectAsync(OrderManagementRowViewModel row, CancellationToken cancellationToken = default)
    {
        var order = await service.GetOrderAsync(row.Id, cancellationToken);
        SelectedOrder = order;
        if (order is not null && !IsEditing) LoadEditableFields(order);
        ValidationMessage = order is null ? Text("OrderNotFound", "Commande introuvable.") : string.Empty;
    }

    public void BeginModification()
    {
        if (!CanModify || SelectedOrder is null) return;
        LoadEditableFields(SelectedOrder); DetailLines.Clear(); foreach (var item in SelectedOrder.Items.OrderBy(item => item.Position)) DetailLines.Add(new OrderDetailLineViewModel(item)); IsEditing = true;
    }

    public async Task SaveModificationAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave || SelectedOrder is null) return;
        if (!TryParse(EditTotal, out var total) || !TryParse(EditCard, out var card) || !TryParse(EditCash, out var cash)) { ValidationMessage = Text("ValidationInvalidNumber", "Entrez un montant valide."); return; }
        var items = DetailLines.Select(line => line.ToSnapshot()).ToArray();
        var itemsChanged = !ItemsEquivalent(SelectedOrder.Items, items);
        var manual = !itemsChanged && total != SelectedOrder.TotalTtc;
        var plannedTime = EditPlannedHour is { } hour && EditPlannedMinute is { } minute ? new TimeOnly(hour, minute) : (TimeOnly?)null;
        var proposed = SelectedOrder with { Fulfilment = EditFulfilment!.Value, PlannedFulfilmentDate = DateOnly.FromDateTime(EditPlannedDate!.Value.Date), PlannedFulfilmentTime = plannedTime, Telephone = EditTelephone, DeliveryAddress = EditAddress, Comment = EditComment, CardPaymentTtc = card, CashPaymentTtc = cash, Items = items, TotalTtc = total, ManualTotalOverrideActive = manual || SelectedOrder.ManualTotalOverrideActive, PickupDiscountApplied = EditPickupDiscountRequested };
        if (manual) proposed = proposed with { TaxBreakdown = [new OrderTaxBreakdown(OrderPricingService.DeliveryFeeVatRate, total, Money.FromCents(BusinessRounding.ToCents(total.Euros * OrderPricingService.DeliveryFeeVatRate / (100m + OrderPricingService.DeliveryFeeVatRate))), Guid.NewGuid())] };
        var result = await service.SaveModificationAsync(proposed, DateOnly.FromDateTime(EffectivePaymentDate?.Date ?? service.BusinessDate.ToDateTime(TimeOnly.MinValue)), cancellationToken);
        if (!result.Succeeded) { ValidationMessage = string.Join(" ", result.Issues.Select(issue => issue.Message)); return; }
        SelectedOrder = result.Snapshot; IsEditing = false; DetailLines.Clear(); await RefreshAsync(cancellationToken);
    }

    public async Task CloseSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedOrder is null) return; var result = await service.CloseAsync(SelectedOrder.Id, cancellationToken); ApplyResult(result);
    }

    public async Task CancelSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedOrder is null) return; var result = await service.CancelAsync(SelectedOrder.Id, cancellationToken); ApplyResult(result);
    }

    public void AbandonModification()
    {
        if (!IsEditing) return;
        IsEditing = false; DetailLines.Clear(); if (SelectedOrder is not null) LoadEditableFields(SelectedOrder); ValidationMessage = string.Empty;
    }

    public void ReuseCustomer() { if (SelectedOrder is not null) ReuseCustomerRequested?.Invoke(this, SelectedOrder); }

    public async Task AddCurrentCatalogueLineAsync(OrderLineDraft draft, CancellationToken cancellationToken = default)
    {
        if (!CanAddCurrentLine) return;
        var line = await service.CreateCurrentCatalogueLineAsync(draft, cancellationToken);
        if (line is null) { ValidationMessage = Text("ProductInactive", "Le produit n’est plus actif ou sa configuration est invalide."); return; }
        line = line with { Position = DetailLines.Count };
        DetailLines.Add(new OrderDetailLineViewModel(line));
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(CanSave));
    }

    public void RemoveLine(OrderDetailLineViewModel line)
    {
        if (!IsEditing || !DetailLines.Remove(line)) return;
        for (var index = 0; index < DetailLines.Count; index++) DetailLines[index].SetPosition(index);
        OnPropertyChanged(nameof(CanSave));
    }

    public async Task ReplaceLineAsync(OrderDetailLineViewModel line, OrderLineDraft draft, CancellationToken cancellationToken = default)
    {
        if (!IsEditing) return;
        var replacement = await service.CreateCurrentCatalogueLineAsync(draft, cancellationToken);
        if (replacement is null) { ValidationMessage = Text("ProductInactive", "Le produit n’est plus actif ou sa configuration est invalide."); return; }
        var index = DetailLines.IndexOf(line);
        if (index < 0) return;
        DetailLines[index] = new OrderDetailLineViewModel(replacement with { Id = line.Item.Id, Position = index });
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(CanSave));
    }

    private void ApplyResult(OrderLifecycleResult result) { if (!result.Succeeded) { ValidationMessage = string.Join(" ", result.Issues.Select(issue => issue.Message)); return; } SelectedOrder = result.Snapshot; _ = RefreshAsync(); }
    private void LoadEditableFields(OrderSnapshot order) { EditFulfilment = order.Fulfilment; editPickupDiscountRequested = order.PickupDiscountApplied; OnPropertyChanged(nameof(EditPickupDiscountRequested)); OnPropertyChanged(nameof(IsEditPickupDiscountEnabled)); EditPlannedDate = order.PlannedFulfilmentDate.ToDateTime(TimeOnly.MinValue); EditPlannedHour = order.PlannedFulfilmentTime?.Hour; EditPlannedMinute = order.PlannedFulfilmentTime?.Minute; EditTelephone = order.Telephone ?? string.Empty; EditAddress = order.DeliveryAddress ?? string.Empty; EditComment = order.Comment ?? string.Empty; EditTotal = order.TotalTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture); EditCard = order.CardPaymentTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture); EditCash = order.CashPaymentTtc.Euros.ToString("0.00", CultureInfo.CurrentCulture); EffectivePaymentDate = service.BusinessDate.ToDateTime(TimeOnly.MinValue); RaiseEditPaymentProperties(); }
    private string LocalizeStatus(OrderStatus status) => status switch { OrderStatus.Open => Text("OrderStatusOpen", "Ouverte"), OrderStatus.Closed => Text("OrderStatusClosed", "Clôturée"), _ => Text("OrderStatusCancelled", "Annulée") };
    private string Text(string key, string fallback) => localized.TryGetValue(key, out var value) ? value : fallback;
    private static bool TryParse(string value, out Money money) { if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out var parsed)) { money = Money.FromEuros(parsed); return true; } money = Money.Zero; return false; }
    private void RaiseDetailProperties() { OnPropertyChanged(nameof(HasSelectedOrder)); OnPropertyChanged(nameof(MinimumEditPlannedDate)); foreach (var name in new[] { nameof(ReferenceText), nameof(StatusText), nameof(FulfilmentText), nameof(AdvanceText), nameof(ManualTotalText), nameof(TaxSummaryText), nameof(PlannedDateText), nameof(PlannedTimeText), nameof(TotalText), nameof(PaidText), nameof(DifferenceText), nameof(TelephoneText), nameof(AddressText), nameof(CommentText), nameof(EditPickupDiscountRequested), nameof(IsEditPickupDiscountEnabled) }) OnPropertyChanged(name); RaiseEditPaymentProperties(); RaiseCommandProperties(); }
    private void RaiseCommandProperties() { foreach (var name in new[] { nameof(CanModify), nameof(CanSave), nameof(CanAbandon), nameof(CanClose), nameof(CanCancel), nameof(CanReuseCustomer), nameof(CanAddCurrentLine), nameof(IsEditPickupDiscountEnabled), nameof(EditCloseEligibilityText) }) OnPropertyChanged(name); }
    private void RaiseEditPaymentProperties() { foreach (var name in new[] { nameof(EditPaidText), nameof(EditDifferenceText), nameof(EditCloseEligibilityText) }) OnPropertyChanged(name); }
    private static bool ItemsEquivalent(IReadOnlyList<OrderItemSnapshot> left, OrderItemSnapshot[] right) =>
        left.Count == right.Length && left.OrderBy(item => item.Position).Zip(right.OrderBy(item => item.Position)).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Position == pair.Second.Position && pair.First.SourceProductId == pair.Second.SourceProductId &&
            pair.First.ProductCode == pair.Second.ProductCode && pair.First.ProductName == pair.Second.ProductName && pair.First.CategoryName == pair.Second.CategoryName &&
            pair.First.ProductBasePriceTtc == pair.Second.ProductBasePriceTtc && pair.First.ProductVatRate == pair.Second.ProductVatRate &&
            pair.First.ProductDiscountEligible == pair.Second.ProductDiscountEligible && pair.First.Quantity == pair.Second.Quantity &&
            pair.First.ExtendedBaseTtc == pair.Second.ExtendedBaseTtc && pair.First.CalculatedLineTotalTtc == pair.Second.CalculatedLineTotalTtc &&
            pair.First.Adjustments.SequenceEqual(pair.Second.Adjustments));
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private static IEnumerable<TimeChoice> BuildTimeChoices(IEnumerable<int> values)
    {
        yield return new TimeChoice(null, "—");
        foreach (var value in values) yield return new TimeChoice(value, value.ToString("D2", CultureInfo.InvariantCulture));
    }
    public void Dispose() { if (disposed) return; disposed = true; lock (refreshLock) { refreshVersion++; refreshCancellation?.Cancel(); refreshCancellation?.Dispose(); refreshCancellation = null; } service.Dispose(); }
}
