using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

public sealed record ArchiveStatusOption(OrderStatus? Value, string Label);

/// <summary>
/// Dedicated read-only historical surface. It intentionally has no lifecycle
/// mutation or printing commands; export is an explicit copy of the selected
/// validated canonical archive.
/// </summary>
public sealed class AnnualArchiveAccessViewModel(IAnnualArchiveAccess access) : INotifyPropertyChanged
{
    private readonly IAnnualArchiveAccess access = access ?? throw new ArgumentNullException(nameof(access));
    private AnnualArchiveDescriptor? selectedArchive;
    private OrderBrowserRow? selectedRow;
    private OrderSnapshot? selectedOrder;
    private string searchText = string.Empty;
    private string fromDateText = string.Empty;
    private string toDateText = string.Empty;
    private ArchiveStatusOption? selectedStatus;
    private string statusMessage = string.Empty;
    private string errorMessage = string.Empty;
    private bool isBusy;
    private IReadOnlyDictionary<string, string> localized = new Dictionary<string, string>(StringComparer.Ordinal);

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
            SelectedRow = null;
            _ = RefreshOrdersGuardedAsync();
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanExport));
        }
    }

    public OrderBrowserRow? SelectedRow
    {
        get => selectedRow;
        set
        {
            if (ReferenceEquals(selectedRow, value)) return;
            selectedRow = value;
            selectedOrder = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedOrder));
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

    public string StatusMessage
    {
        get => statusMessage;
        private set { if (statusMessage != value) { statusMessage = value; OnPropertyChanged(); } }
    }

    public string ErrorMessage
    {
        get => errorMessage;
        private set { if (errorMessage != value) { errorMessage = value; OnPropertyChanged(); } }
    }

    public bool IsBusy
    {
        get => isBusy;
        private set { if (isBusy != value) { isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExport)); } }
    }

    public bool CanExport => SelectedArchive is not null && !IsBusy;

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
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var archives = await access.DiscoverAsync(cancellationToken);
            AvailableArchives.Clear();
            foreach (var archive in archives) AvailableArchives.Add(archive);
            if (selectedArchive is null || !archives.Any(item => item.ArchiveYear == selectedArchive.ArchiveYear))
                SelectedArchive = archives.Count == 0 ? null : archives[0];
            else
                await RefreshOrdersGuardedAsync(cancellationToken);
            StatusMessage = archives.Count == 0
                ? Text("ArchiveNoArchives", "No validated annual archives are available.")
                : string.Format(CultureInfo.CurrentCulture, Text("ArchiveAvailable", "{0} annual archive(s) available."), archives.Count);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            AvailableArchives.Clear();
            Orders.Clear();
            SelectedArchive = null;
            ErrorMessage = Text("ArchiveAccessFailed", "Historical archive access failed.");
        }
        finally { IsBusy = false; }
    }

    public async Task RefreshOrdersAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedArchive is null)
        {
            Orders.Clear();
            SelectedOrder = null;
            return;
        }

        if (!TryReadDate(FromDateText, out var from) || !TryReadDate(ToDateText, out var to) || from is not null && to is not null && from > to)
        {
            ErrorMessage = Text("ArchiveInvalidDateRange", "The historical date range is invalid.");
            return;
        }

        ErrorMessage = string.Empty;
        var rows = await access.SearchAsync(SelectedArchive.ArchiveYear, new AnnualArchiveSearchCriteria(SearchText, SelectedStatus?.Value, from, to), cancellationToken);
        Orders.Clear();
        foreach (var row in rows) Orders.Add(row);
        if (SelectedRow is not null && !rows.Any(row => row.Id == SelectedRow.Id))
            SelectedRow = null;
    }

    public async Task LoadSelectedAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedArchive is not null && SelectedRow is not null)
            await LoadSelectedAsync(SelectedArchive.ArchiveYear, SelectedRow.Id, cancellationToken);
    }

    public async Task<AnnualArchiveCopyResult?> CopySelectedAsync(string? destinationPath, CancellationToken cancellationToken = default)
    {
        if (SelectedArchive is null || string.IsNullOrWhiteSpace(destinationPath)) return null;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var result = await access.CopyAsync(SelectedArchive.ArchiveYear, destinationPath, cancellationToken);
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Text("ArchiveCopySucceeded", "Archive copied to {0}."), result.DestinationPath);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            ErrorMessage = Text("ArchiveCopyFailed", "The selected archive could not be copied.");
            return null;
        }
        finally { IsBusy = false; }
    }

    private async Task RefreshOrdersGuardedAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try { await RefreshOrdersAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { Orders.Clear(); ErrorMessage = Text("ArchiveAccessFailed", "Historical archive access failed."); }
        finally { IsBusy = false; }
    }

    private async Task LoadSelectedAsync(int archiveYear, Guid orderId, CancellationToken cancellationToken = default)
    {
        try { SelectedOrder = await access.GetOrderAsync(archiveYear, orderId, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { SelectedOrder = null; ErrorMessage = Text("ArchiveAccessFailed", "Historical archive access failed."); }
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
