using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Printing;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

public sealed record ArchiveStatusOption(OrderStatus? Value, string Label);

/// <summary>
/// Dedicated read-only historical surface. It intentionally has no lifecycle
/// mutation or printing commands; export is an explicit copy of the selected
/// validated canonical archive.
/// </summary>
public sealed class AnnualArchiveAccessViewModel(
    IAnnualArchiveAccess access,
    IArchivedOrderPrintApplicationService? printService = null,
    DesktopOperationDiagnostics? diagnostics = null) : INotifyPropertyChanged
{
    private readonly IAnnualArchiveAccess access = access ?? throw new ArgumentNullException(nameof(access));
    private readonly IArchivedOrderPrintApplicationService? printService = printService;
    private readonly DesktopOperationDiagnostics? operationDiagnostics = diagnostics;
    private AnnualArchiveDescriptor? selectedArchive;
    private OrderBrowserRow? selectedRow;
    private OrderSnapshot? selectedOrder;
    private string searchText = string.Empty;
    private string fromDateText = string.Empty;
    private string toDateText = string.Empty;
    private ArchiveStatusOption? selectedStatus;
    private LocalizedMessageState? statusMessageState;
    private LocalizedMessageState? errorMessageState;
    private LocalizedMessageState? printStatusMessageState;
    private bool isBusy;
    private IReadOnlyDictionary<string, string> localized = new Dictionary<string, string>(StringComparer.Ordinal);
    private long discoveryGeneration;
    private long searchGeneration;
    private long detailGeneration;
    private CancellationTokenSource? discoveryCancellation;
    private CancellationTokenSource? searchCancellation;
    private CancellationTokenSource? detailCancellation;
    private int activeOperations;
    private long selectionGeneration;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AnnualArchiveDescriptor> AvailableArchives { get; } = [];
    public ObservableCollection<OrderBrowserRow> Orders { get; } = [];
    public ObservableCollection<ArchiveStatusOption> StatusOptions { get; } = [];

    public AnnualArchiveDescriptor? SelectedArchive
    {
        get => selectedArchive;
        set
        {
            if (ReferenceEquals(selectedArchive, value)) return;
            selectedArchive = value;
            SelectionChanged();
            CancelSearch();
            CancelDetail();
            selectedRow = null;
            selectedOrder = null;
            Orders.Clear();
            OnPropertyChanged(nameof(SelectedRow));
            OnPropertyChanged(nameof(SelectedOrder));
            if (value is not null) _ = RefreshOrdersGuardedAsync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExport));
            OnPropertyChanged(nameof(CanReprint));
        }
    }

    public OrderBrowserRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (ReferenceEquals(selectedRow, value)) return;
            selectedRow = value;
            SelectionChanged();
            selectedOrder = null;
            CancelDetail();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedOrder));
            OnPropertyChanged(nameof(CanReprint));
            if (value is not null && selectedArchive is not null)
                _ = LoadSelectedAsync(selectedArchive.ArchiveYear, value.Id);
        }
    }

    public OrderSnapshot? SelectedOrder
    {
        get => selectedOrder;
        private set
        {
            selectedOrder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanReprint));
        }
    }

    public string SearchText
    {
        get => searchText;
        set { if (searchText != value) { searchText = value; OnPropertyChanged(); } }
    }

    public string FromDateText
    {
        get => fromDateText;
        set { if (fromDateText != value) { fromDateText = value; OnPropertyChanged(); } }
    }

    public string ToDateText
    {
        get => toDateText;
        set { if (toDateText != value) { toDateText = value; OnPropertyChanged(); } }
    }

    public ArchiveStatusOption? SelectedStatus
    {
        get => selectedStatus;
        set { if (!ReferenceEquals(selectedStatus, value)) { selectedStatus = value; OnPropertyChanged(); } }
    }

    public string StatusMessage => statusMessageState?.Render(localized) ?? string.Empty;

    public string ErrorMessage => errorMessageState?.Render(localized) ?? string.Empty;

    public bool IsBusy
    {
        get => isBusy;
        private set { if (isBusy != value) { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExport)); OnPropertyChanged(nameof(CanReprint)); } }
    }

    public bool CanExport => SelectedArchive is not null && !IsBusy;

    public bool CanReprint => printService is not null
        && SelectedArchive is not null
        && SelectedRow is not null
        && SelectedOrder is not null
        && SelectedRow.Id == SelectedOrder.Id
        && !IsBusy;

    public string PrintStatusMessage => printStatusMessageState?.Render(localized) ?? string.Empty;

    public void ApplyLocalization(IReadOnlyDictionary<string, string> values)
    {
        localized = values ?? throw new ArgumentNullException(nameof(values));
        var previous = SelectedStatus?.Value;
        StatusOptions.Clear();
        StatusOptions.Add(new(null, Text("ArchiveStatusAll", "All statuses")));
        StatusOptions.Add(new(OrderStatus.Closed, Text("OrderStatusClosed", "Closed")));
        StatusOptions.Add(new(OrderStatus.Cancelled, Text("OrderStatusCancelled", "Cancelled")));
        SelectedStatus = StatusOptions.FirstOrDefault(option => option.Value == previous) ?? StatusOptions[0];
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanReprint));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ErrorMessage));
        OnPropertyChanged(nameof(PrintStatusMessage));
    }

    public async Task ReprintSelectedAsync(PrintDocumentKind kind, CancellationToken cancellationToken = default)
    {
        if (!CanReprint || printService is null || SelectedArchive is not { } archive
            || SelectedRow is not { } row || SelectedOrder is not { } snapshot)
            return;

        var archiveYear = archive.ArchiveYear;
        var orderId = snapshot.Id;
        var generation = selectionGeneration;
        BeginOperation();
        SetPrintStatus(null);
        try
        {
            var result = await printService.ReprintAsync(snapshot, kind, cancellationToken);
            if (IsCurrentPrintSelection(generation, archiveYear, orderId))
            {
                var messageState = result.Succeeded
                    ? LocalizedMessageState.Resource("OrderPrintSuccess", "The requested document was accepted by the printer.")
                    : LocalizedMessageState.Issue(
                        new ValidationIssue(
                            kind == PrintDocumentKind.Kitchen ? "kitchen-print" : "customer-print",
                            result.OperatorMessage,
                            result.Status == PrintOutcomeStatus.AmbiguousSubmission ? ValidationCodes.PrintAmbiguous : ValidationCodes.Generic));
                SetPrintStatus(messageState);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            operationDiagnostics?.ReportUnexpectedFailure("archive-access.print", exception);
            if (IsCurrentPrintSelection(generation, archiveYear, orderId))
                SetPrintStatus(LocalizedMessageState.Issue(
                    new ValidationIssue(kind == PrintDocumentKind.Kitchen ? "kitchen-print" : "customer-print", string.Empty)));
        }
        finally { EndOperation(); }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = discoveryCancellation;
        discoveryCancellation = operation;
        var generation = ++discoveryGeneration;
        previous?.Cancel();
        BeginOperation();
        try
        {
            SetErrorMessage(null);
            var archives = await access.DiscoverAsync(operation.Token);
            if (!IsCurrent(discoveryCancellation, operation, discoveryGeneration, generation)) return;

            AvailableArchives.Clear();
            foreach (var archive in archives) AvailableArchives.Add(archive);

            var selectedYear = selectedArchive?.ArchiveYear;
            var preservedSelection = selectedYear is null
                ? null
                : archives.FirstOrDefault(item => item.ArchiveYear == selectedYear.Value);
            if (preservedSelection is null)
            {
                if (selectedArchive is not null) SelectedArchive = null;
                else
                {
                    CancelSearch();
                    CancelDetail();
                    Orders.Clear();
                    SelectedRow = null;
                    SelectedOrder = null;
                }
            }
            else if (!ReferenceEquals(selectedArchive, preservedSelection))
                SelectedArchive = preservedSelection;
            else
                await RefreshOrdersAsync(operation.Token);

            if (!IsCurrent(discoveryCancellation, operation, discoveryGeneration, generation)) return;
            SetStatusMessage(archives.Count == 0
                ? LocalizedMessageState.Resource("ArchiveNoArchives", "No validated annual archives are available.")
                : LocalizedMessageState.Resource("ArchiveAvailable", "{0} annual archive(s) available.", archives.Count));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            operationDiagnostics?.ReportUnexpectedFailure("archive-access.discover", exception);
            if (!IsCurrent(discoveryCancellation, operation, discoveryGeneration, generation)) return;
            AvailableArchives.Clear();
            Orders.Clear();
            SelectedArchive = null;
            SelectedRow = null;
            SelectedOrder = null;
            SetErrorMessage(LocalizedMessageState.Resource("ArchiveAccessFailed", "Historical archive access failed."));
        }
        finally
        {
            if (ReferenceEquals(discoveryCancellation, operation)) discoveryCancellation = null;
            operation.Dispose();
            EndOperation();
        }
    }

    public async Task RefreshOrdersAsync(CancellationToken cancellationToken = default)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = searchCancellation;
        searchCancellation = operation;
        var generation = ++searchGeneration;
        previous?.Cancel();
        BeginOperation();
        var archiveYear = selectedArchive?.ArchiveYear;
        try
        {
            if (SelectedArchive is null)
            {
                if (IsCurrent(searchCancellation, operation, searchGeneration, generation))
                {
                    Orders.Clear();
                    SelectedRow = null;
                    SelectedOrder = null;
                }
                return;
            }

            if (!TryReadDate(FromDateText, out var from) || !TryReadDate(ToDateText, out var to) || from is not null && to is not null && from > to)
            {
                if (IsCurrentSearch(operation, generation, archiveYear))
                    SetErrorMessage(LocalizedMessageState.Resource("ArchiveInvalidDateRange", "The historical date range is invalid."));
                return;
            }

            SetErrorMessage(null);
            var criteria = new AnnualArchiveSearchCriteria(SearchText, SelectedStatus?.Value, from, to);
            var rows = await access.SearchAsync(archiveYear!.Value, criteria, operation.Token);
            if (!IsCurrentSearch(operation, generation, archiveYear)) return;
            Orders.Clear();
            foreach (var row in rows) Orders.Add(row);
            if (SelectedRow is not null && !rows.Any(row => row.Id == SelectedRow.Id))
                SelectedRow = null;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            operationDiagnostics?.ReportUnexpectedFailure("archive-access.search", exception);
            if (IsCurrentSearch(operation, generation, archiveYear))
            {
                Orders.Clear();
                SelectedRow = null;
                SelectedOrder = null;
                SetErrorMessage(LocalizedMessageState.Resource("ArchiveAccessFailed", "Historical archive access failed."));
            }
        }
        finally
        {
            if (ReferenceEquals(searchCancellation, operation)) searchCancellation = null;
            operation.Dispose();
            EndOperation();
        }
    }

    public async Task LoadSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedArchive is not null && SelectedRow is not null)
            await LoadSelectedAsync(SelectedArchive.ArchiveYear, SelectedRow.Id, cancellationToken);
    }

    public async Task<AnnualArchiveCopyResult?> CopySelectedAsync(string? destinationPath, CancellationToken cancellationToken = default)
    {
        if (SelectedArchive is null || string.IsNullOrWhiteSpace(destinationPath)) return null;
        BeginOperation();
        SetErrorMessage(null);
        try
        {
            var result = await access.CopyAsync(SelectedArchive.ArchiveYear, destinationPath, cancellationToken);
            SetStatusMessage(LocalizedMessageState.Resource("ArchiveCopySucceeded", "Archive copied to {0}.", result.DestinationPath));
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            operationDiagnostics?.ReportUnexpectedFailure("archive-access.copy", exception);
            SetErrorMessage(LocalizedMessageState.Resource("ArchiveCopyFailed", "The selected archive could not be copied."));
            return null;
        }
        finally { EndOperation(); }
    }

    private async Task RefreshOrdersGuardedAsync(CancellationToken cancellationToken = default)
    {
        await RefreshOrdersAsync(cancellationToken);
    }

    private async Task LoadSelectedAsync(int archiveYear, Guid orderId, CancellationToken cancellationToken = default)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previous = detailCancellation;
        detailCancellation = operation;
        var generation = ++detailGeneration;
        previous?.Cancel();
        BeginOperation();
        try
        {
            var order = await access.GetOrderAsync(archiveYear, orderId, operation.Token);
            if (IsCurrentDetail(operation, generation, archiveYear, orderId)) SelectedOrder = order;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested) { }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            operationDiagnostics?.ReportUnexpectedFailure("archive-access.order-detail", exception);
            if (IsCurrentDetail(operation, generation, archiveYear, orderId))
            {
                SelectedOrder = null;
                SetErrorMessage(LocalizedMessageState.Resource("ArchiveAccessFailed", "Historical archive access failed."));
            }
        }
        finally
        {
            if (ReferenceEquals(detailCancellation, operation)) detailCancellation = null;
            operation.Dispose();
            EndOperation();
        }
    }

    private bool IsCurrentSearch(CancellationTokenSource operation, long generation, int? archiveYear) =>
        IsCurrent(searchCancellation, operation, searchGeneration, generation) &&
        archiveYear is not null && selectedArchive?.ArchiveYear == archiveYear;

    private bool IsCurrentDetail(CancellationTokenSource operation, long generation, int archiveYear, Guid orderId) =>
        IsCurrent(detailCancellation, operation, detailGeneration, generation) &&
        selectedArchive?.ArchiveYear == archiveYear && selectedRow?.Id == orderId;

    private bool IsCurrentPrintSelection(long generation, int archiveYear, Guid orderId) =>
        selectionGeneration == generation && selectedArchive?.ArchiveYear == archiveYear
        && selectedRow?.Id == orderId && selectedOrder?.Id == orderId;

    private void SelectionChanged()
    {
        selectionGeneration++;
        SetPrintStatus(null);
    }

    private void SetStatusMessage(LocalizedMessageState? state)
    {
        statusMessageState = state;
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void SetErrorMessage(LocalizedMessageState? state)
    {
        errorMessageState = state;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void SetPrintStatus(LocalizedMessageState? state)
    {
        printStatusMessageState = state;
        OnPropertyChanged(nameof(PrintStatusMessage));
    }

    private static bool IsCurrent(CancellationTokenSource? current, CancellationTokenSource operation, long currentGeneration, long generation) =>
        ReferenceEquals(current, operation) && currentGeneration == generation && !operation.IsCancellationRequested;

    private void CancelSearch()
    {
        searchGeneration++;
        var previous = searchCancellation;
        searchCancellation = null;
        previous?.Cancel();
    }

    private void CancelDetail()
    {
        detailGeneration++;
        var previous = detailCancellation;
        detailCancellation = null;
        previous?.Cancel();
    }

    private void BeginOperation()
    {
        activeOperations++;
        if (activeOperations == 1) IsBusy = true;
    }

    private void EndOperation()
    {
        activeOperations--;
        if (activeOperations == 0) IsBusy = false;
    }

    private static bool TryReadDate(string value, out DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(value)) { date = null; return true; }
        if (DateOnly.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed)) { date = parsed; return true; }
        date = null;
        return false;
    }

    private string Text(string key, string fallback) => localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
