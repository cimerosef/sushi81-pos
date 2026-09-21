using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Export;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Time;

namespace Sushi81.Pos.Desktop;

public sealed record GestionExportActionSummary(string Action, string Label, int Count);

public sealed record GestionExportDiagnosticPresentation(
    string Severity,
    string OrderId,
    string Code,
    string Message);

public sealed record GestionExportHistoryRow(
    Guid BatchId,
    DateTimeOffset GeneratedAt,
    DateOnly? FilterStartDate,
    DateOnly? FilterEndDate,
    string GeneratedAtText,
    string FilterText,
    int OrderCount,
    string StatusText);

public sealed record GestionExportPreparedBatchRow(
    Guid BatchId,
    DateTimeOffset PreparedAt,
    DateOnly? FilterStartDate,
    DateOnly? FilterEndDate,
    string PreparedAtText,
    string FilterText,
    int OrderCount,
    string StatusText);

/// <summary>
/// Presentation-owned orchestration for the M11 Desktop workflow. Preview uses only
/// read paths; export is the sole path that prepares and finalizes a new batch.
/// </summary>
public sealed class GestionExportWorkflowViewModel : INotifyPropertyChanged
{
    private readonly GestionExportWorkbookService workbookService;
    private readonly IExportBatchHistoryReader historyReader;
    private readonly IExportPreparedBatchReader? preparedBatchReader;
    private readonly IWriteAuthorityGuard authorityGuard;
    private readonly string appVersion;
    private readonly Func<bool> presentationRefreshBlocked;
    private IReadOnlyDictionary<string, string> localized;
    private DateTime? startDate;
    private DateTime? endDate;
    private bool busy;
    private bool businessPresentationRefreshBlocked;
    private ExportSelectionResult? preview;
    private string validationMessage = string.Empty;
    private string statusMessage = string.Empty;
    private string failureMessage = string.Empty;
    private GestionExportHistoryRow? selectedHistory;
    private GestionExportPreparedBatchRow? selectedPreparedBatch;

    public GestionExportWorkflowViewModel(
        GestionExportWorkbookService workbookService,
        IExportBatchHistoryReader historyReader,
        IWriteAuthorityGuard authorityGuard,
        string appVersion,
        IReadOnlyDictionary<string, string>? localized = null,
        Func<bool>? presentationRefreshBlocked = null,
        IExportPreparedBatchReader? preparedBatchReader = null)
    {
        this.workbookService = workbookService ?? throw new ArgumentNullException(nameof(workbookService));
        this.historyReader = historyReader ?? throw new ArgumentNullException(nameof(historyReader));
        this.authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
        this.preparedBatchReader = preparedBatchReader;
        this.appVersion = string.IsNullOrWhiteSpace(appVersion) ? "1.0.0" : appVersion;
        this.localized = localized ?? new Dictionary<string, string>(StringComparer.Ordinal);
        this.presentationRefreshBlocked = presentationRefreshBlocked ?? (() => false);
        History = [];
        PendingBatches = [];
        Diagnostics = [];
        ActionSummary =
        [
            new GestionExportActionSummary("CREATE", Read("GestionExportCreate", "CREATE"), 0),
            new GestionExportActionSummary("UPDATE", Read("GestionExportUpdate", "UPDATE"), 0),
            new GestionExportActionSummary("CANCEL", Read("GestionExportCancel", "CANCEL"), 0),
        ];
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GestionExportHistoryRow> History { get; }

    public ObservableCollection<GestionExportPreparedBatchRow> PendingBatches { get; }

    public ObservableCollection<GestionExportDiagnosticPresentation> Diagnostics { get; }

    public IReadOnlyList<GestionExportActionSummary> ActionSummary { get; private set; }

    public DateTime? StartDate
    {
        get => startDate;
        set
        {
            if (startDate == value) return;
            startDate = value?.Date;
            ClearSelectionState();
            NotifyDateState();
        }
    }

    public DateTime? EndDate
    {
        get => endDate;
        set
        {
            if (endDate == value) return;
            endDate = value?.Date;
            ClearSelectionState();
            NotifyDateState();
        }
    }

    public bool IsBusy => busy;

    public bool IsAuthoritative => authorityGuard.State == WriteAuthorityState.Authoritative;

    public string AuthorityStatusText => IsAuthoritative
        ? Read("GestionExportAuthority", "New export requires write authority; preview and history remain read-only.")
        : Read("GestionExportReadOnly", "Read-only authority: new export is unavailable; successful history can still be regenerated safely.");

    public bool CanPreview => !busy && !IsPresentationRefreshBlocked;

    public bool CanExport => !busy && IsAuthoritative && !IsPresentationRefreshBlocked;

    public bool CanRegenerate => !busy && selectedHistory is not null && !IsPresentationRefreshBlocked;

    public bool CanRetryPrepared => !busy && selectedPreparedBatch is not null && IsAuthoritative && !IsPresentationRefreshBlocked;

    private bool IsPresentationRefreshBlocked => businessPresentationRefreshBlocked || presentationRefreshBlocked();

    public bool HasPreview => preview is not null;

    public ExportSelectionResult? Preview => preview;

    public GestionExportHistoryRow? SelectedHistory
    {
        get => selectedHistory;
        set
        {
            if (ReferenceEquals(selectedHistory, value)) return;
            selectedHistory = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRegenerate));
        }
    }

    public GestionExportPreparedBatchRow? SelectedPreparedBatch
    {
        get => selectedPreparedBatch;
        set
        {
            if (ReferenceEquals(selectedPreparedBatch, value)) return;
            selectedPreparedBatch = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRetryPrepared));
        }
    }

    public string InclusiveDateText => FormatDateRange();

    public string SelectionSummary { get; private set; } = string.Empty;

    public string ValidationMessage
    {
        get => validationMessage;
        private set
        {
            if (validationMessage == value) return;
            validationMessage = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set
        {
            if (statusMessage == value) return;
            statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string FailureMessage
    {
        get => failureMessage;
        private set
        {
            if (failureMessage == value) return;
            failureMessage = value;
            OnPropertyChanged();
        }
    }

    public async Task PreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!CanPreview) return;
        if (!TryBuildOptions(out var options)) return;

        SetBusy(true);
        FailureMessage = string.Empty;
        StatusMessage = Read("GestionExportPreviewBusy", "Loading export preview…");
        try
        {
            // SelectAsync is intentionally used here instead of PrepareBatchAsync:
            // opening or refreshing the preview must never create PREPARED state.
            preview = await workbookService.SelectAsync(options, cancellationToken);
            UpdateSelectionPresentation();
            StatusMessage = preview.IsBlocked
                ? Read("GestionExportPreviewBlocked", "The preview contains blocking diagnostics.")
                : Format("GestionExportPreviewReady", "Preview ready: {0} action(s).", preview.Actions.Count);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Read("GestionExportCancelled", "Export preview cancelled.");
        }
        catch (Exception exception)
        {
            preview = null;
            UpdateSelectionPresentation();
            FailureMessage = Read("GestionExportFailure", "The export preview could not be loaded.");
            StatusMessage = FailureMessage;
            _ = exception;
        }
        finally
        {
            SetBusy(false);
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    public async Task<ExportWorkbookResult?> ExportAsync(string finalPath, CancellationToken cancellationToken = default)
    {
        if (!CanExport) return null;
        if (!TryBuildOptions(out var options)) return null;
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            ValidationMessage = Read("GestionExportDestinationRequired", "Choose an export destination.");
            return null;
        }

        SetBusy(true);
        FailureMessage = string.Empty;
        StatusMessage = Read("GestionExportBusy", "Generating and validating the export workbook…");
        try
        {
            var outcome = await workbookService.GenerateWithOutcomeAsync(options, appVersion, finalPath, cancellationToken);
            if (outcome.Result is null)
            {
                preview = outcome.Selection;
                UpdateSelectionPresentation();
                StatusMessage = outcome.Selection.IsBlocked
                    ? Format("GestionExportBlockedAtExport", "Export blocked: {0}", LocalizeDiagnosticMessage(outcome.Selection.Diagnostics[0]))
                    : Read("GestionExportNoPending", "There are no pending export actions for this scope.");
                return null;
            }

            StatusMessage = Format("GestionExportSucceeded", "Export succeeded: {0}.", Path.GetFileName(outcome.Result.FinalPath));
            await RefreshHistoryCoreAsync(cancellationToken);
            await RefreshPreviewCoreAsync(cancellationToken);
            return outcome.Result;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Read("GestionExportCancelled", "Export cancelled.");
            return null;
        }
        catch (Exception exception)
        {
            try
            {
                // Preparation is durable before workbook generation/finalization. Refresh the
                // pending list so a failed finalization immediately exposes the same BatchId
                // for a safe retry instead of leaving the operator with stale UI state.
                await RefreshHistoryCoreAsync(CancellationToken.None);
            }
            catch
            {
                // Keep the operator-facing failure safe even if the refresh itself is unavailable.
            }
            FailureMessage = Read("GestionExportFailure", "The export could not be completed; any prepared batch remains available under pending batches for retry.");
            StatusMessage = FailureMessage;
            _ = exception;
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task<ExportWorkbookResult?> RegenerateAsync(string finalPath, CancellationToken cancellationToken = default)
    {
        if (!CanRegenerate || selectedHistory is not { } selected) return null;
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            ValidationMessage = Read("GestionExportDestinationRequired", "Choose an export destination.");
            return null;
        }

        SetBusy(true);
        FailureMessage = string.Empty;
        StatusMessage = Read("GestionExportRegenerateBusy", "Regenerating the selected successful batch…");
        try
        {
            var result = await workbookService.RegenerateAsync(selected.BatchId, finalPath, cancellationToken);
            StatusMessage = Format("GestionExportRegenerated", "Batch regenerated: {0}.", Path.GetFileName(result.FinalPath));
            return result;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Read("GestionExportCancelled", "Regeneration cancelled.");
            return null;
        }
        catch (Exception exception)
        {
            FailureMessage = Read("GestionExportRegenerateFailure", "The selected batch could not be regenerated.");
            StatusMessage = FailureMessage;
            _ = exception;
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task<ExportWorkbookResult?> RetryPreparedAsync(string finalPath, CancellationToken cancellationToken = default)
    {
        if (!CanRetryPrepared || selectedPreparedBatch is not { } selected) return null;
        if (string.IsNullOrWhiteSpace(finalPath))
        {
            ValidationMessage = Read("GestionExportDestinationRequired", "Choose an export destination.");
            return null;
        }

        SetBusy(true);
        FailureMessage = string.Empty;
        StatusMessage = Read("GestionExportPreparedRetryBusy", "Retrying the pending export batch…");
        try
        {
            var result = await workbookService.FinalizePreparedBatchAsync(selected.BatchId, finalPath, cancellationToken);
            await RefreshHistoryCoreAsync(cancellationToken);
            await RefreshPreviewCoreAsync(cancellationToken);
            StatusMessage = Format("GestionExportPreparedRetrySucceeded", "Pending batch completed: {0}.", Path.GetFileName(result.FinalPath));
            return result;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Read("GestionExportCancelled", "Export cancelled.");
            return null;
        }
        catch (Exception exception)
        {
            FailureMessage = Read("GestionExportPreparedRetryFailure", "The pending batch could not be completed; it remains available for retry.");
            StatusMessage = FailureMessage;
            _ = exception;
            return null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task RefreshHistoryAsync(CancellationToken cancellationToken = default)
    {
        if (busy) return;
        SetBusy(true);
        try { await RefreshHistoryCoreAsync(cancellationToken); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            FailureMessage = Read("GestionExportHistoryFailure", "Export history could not be loaded.");
            StatusMessage = FailureMessage;
        }
        finally { SetBusy(false); }
    }

    public void RefreshAuthorityState()
    {
        OnPropertyChanged(nameof(IsAuthoritative));
        OnPropertyChanged(nameof(AuthorityStatusText));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(CanRetryPrepared));
    }

    public void RefreshPresentationState()
    {
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(CanRetryPrepared));
    }

    public void SetBusinessPresentationRefreshBlocked(bool blocked)
    {
        if (businessPresentationRefreshBlocked == blocked) return;
        businessPresentationRefreshBlocked = blocked;
        RefreshPresentationState();
    }

    public void ApplyLocalization(IReadOnlyDictionary<string, string> values)
    {
        localized = values ?? throw new ArgumentNullException(nameof(values));
        UpdateSelectionPresentation();
        RebuildHistory(History.Select(row => row.BatchId).ToArray());
        RebuildPendingBatches(PendingBatches.Select(row => row.BatchId).ToArray());
        OnPropertyChanged(nameof(InclusiveDateText));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(AuthorityStatusText));
    }

    public async Task RefreshAfterLiveDatabaseReplacementAsync(CancellationToken cancellationToken = default)
    {
        if (busy)
            throw new InvalidOperationException("The Gestion export workflow is busy while the live database is being replaced.");

        ClearDatabaseBoundState();
        SetBusy(true);
        try
        {
            await RefreshHistoryCoreAsync(cancellationToken);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshHistoryCoreAsync(CancellationToken cancellationToken)
    {
        var batches = await historyReader.ListSuccessfulBatchesAsync(cancellationToken);
        var prepared = preparedBatchReader is null
            ? Array.Empty<ExportBatchRecord>()
            : await preparedBatchReader.ListPreparedBatchesAsync(cancellationToken);
        var selectedId = selectedHistory?.BatchId;
        var selectedPreparedId = selectedPreparedBatch?.BatchId;
        History.Clear();
        PendingBatches.Clear();
        foreach (var batch in batches.Where(batch => batch.Status == ExportBatchStatus.Success))
            History.Add(ToHistoryRow(batch));
        foreach (var batch in prepared.Where(batch => batch.Status == ExportBatchStatus.Prepared))
            PendingBatches.Add(ToPreparedBatchRow(batch));
        SelectedHistory = selectedId is { } id ? History.FirstOrDefault(row => row.BatchId == id) : History.FirstOrDefault();
        SelectedPreparedBatch = selectedPreparedId is { } preparedId
            ? PendingBatches.FirstOrDefault(row => row.BatchId == preparedId)
            : PendingBatches.FirstOrDefault();
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(CanRetryPrepared));
    }

    private async Task RefreshPreviewCoreAsync(CancellationToken cancellationToken)
    {
        if (!TryBuildOptions(out var options))
            return;
        preview = await workbookService.SelectAsync(options, cancellationToken);
        UpdateSelectionPresentation();
    }

    private bool TryBuildOptions(out ExportSelectionOptions options)
    {
        options = new ExportSelectionOptions();
        ValidationMessage = string.Empty;
        if (startDate is { } start && endDate is { } end && start.Date > end.Date)
        {
            ValidationMessage = Read("GestionExportInvalidRange", "The start date cannot be after the end date.");
            return false;
        }

        options = new ExportSelectionOptions(
            startDate is { } startDateValue ? DateOnly.FromDateTime(startDateValue.Date) : null,
            endDate is { } endDateValue ? DateOnly.FromDateTime(endDateValue.Date) : null);
        return true;
    }

    private void UpdateSelectionPresentation()
    {
        var counts = preview?.Actions.GroupBy(action => action.Action).ToDictionary(group => group.Key, group => group.Count())
            ?? new Dictionary<ExportAction, int>();
        ActionSummary =
        [
            new GestionExportActionSummary("CREATE", Read("GestionExportCreate", "CREATE"), counts.GetValueOrDefault(ExportAction.Create)),
            new GestionExportActionSummary("UPDATE", Read("GestionExportUpdate", "UPDATE"), counts.GetValueOrDefault(ExportAction.Update)),
            new GestionExportActionSummary("CANCEL", Read("GestionExportCancel", "CANCEL"), counts.GetValueOrDefault(ExportAction.Cancel)),
        ];
        Diagnostics.Clear();
        foreach (var diagnostic in preview?.Diagnostics ?? [])
        {
            Diagnostics.Add(new GestionExportDiagnosticPresentation(
                Read("GestionExportBlocking", "Blocking"),
                diagnostic.OrderId.ToString("D"),
                diagnostic.Code,
                LocalizeDiagnosticMessage(diagnostic)));
        }

        SelectionSummary = Format(
            "GestionExportSelectionSummary",
            "{0} action(s): {1} CREATE, {2} UPDATE, {3} CANCEL.",
            preview?.Actions.Count ?? 0,
            counts.GetValueOrDefault(ExportAction.Create),
            counts.GetValueOrDefault(ExportAction.Update),
            counts.GetValueOrDefault(ExportAction.Cancel));
        OnPropertyChanged(nameof(ActionSummary));
        OnPropertyChanged(nameof(Diagnostics));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(HasPreview));
    }

    private string LocalizeDiagnosticMessage(ExportSelectionDiagnostic diagnostic) =>
        diagnostic.Code switch
        {
            "SETTLEMENT_DATE_UNAVAILABLE" => Read(
                "GestionExportDiagnosticSettlementDateUnavailable",
                "Settlement date is unavailable for this order."),
            _ => diagnostic.Message
        };

    private void ClearSelectionState()
    {
        preview = null;
        ValidationMessage = string.Empty;
        UpdateSelectionPresentation();
    }

    private void ClearDatabaseBoundState()
    {
        preview = null;
        selectedHistory = null;
        selectedPreparedBatch = null;
        History.Clear();
        PendingBatches.Clear();
        ValidationMessage = string.Empty;
        StatusMessage = string.Empty;
        FailureMessage = string.Empty;
        UpdateSelectionPresentation();
        OnPropertyChanged(nameof(SelectedHistory));
        OnPropertyChanged(nameof(SelectedPreparedBatch));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(CanRetryPrepared));
    }

    private void NotifyDateState()
    {
        OnPropertyChanged(nameof(StartDate));
        OnPropertyChanged(nameof(EndDate));
        OnPropertyChanged(nameof(InclusiveDateText));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanExport));
    }

    private GestionExportHistoryRow ToHistoryRow(ExportBatchRecord batch)
    {
        var meta = batch.Payload.Meta;
        return new GestionExportHistoryRow(
            meta.BatchId,
            meta.GeneratedAt,
            meta.FilterStartDate,
            meta.FilterEndDate,
            meta.GeneratedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            FormatFilter(meta.FilterStartDate, meta.FilterEndDate),
            meta.OrderCount,
            Read("GestionExportSuccessStatus", "SUCCESS"));
    }

    private GestionExportPreparedBatchRow ToPreparedBatchRow(ExportBatchRecord batch)
    {
        var meta = batch.Payload.Meta;
        return new GestionExportPreparedBatchRow(
            meta.BatchId,
            meta.GeneratedAt,
            meta.FilterStartDate,
            meta.FilterEndDate,
            meta.GeneratedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            FormatFilter(meta.FilterStartDate, meta.FilterEndDate),
            meta.OrderCount,
            Read("GestionExportPreparedStatus", "PREPARED"));
    }

    private void RebuildHistory(Guid[] ids)
    {
        if (ids.Length == 0) return;
        var selectedId = selectedHistory?.BatchId;
        var current = History.ToDictionary(row => row.BatchId);
        History.Clear();
        foreach (var id in ids)
            if (current.TryGetValue(id, out var row))
                History.Add(row with
                {
                    GeneratedAtText = row.GeneratedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    FilterText = FormatFilter(row.FilterStartDate, row.FilterEndDate),
                    StatusText = Read("GestionExportSuccessStatus", "SUCCESS"),
                });
        SelectedHistory = selectedId is { } selected ? History.FirstOrDefault(row => row.BatchId == selected) : History.FirstOrDefault();
    }

    private void RebuildPendingBatches(Guid[] ids)
    {
        if (ids.Length == 0) return;
        var selectedId = selectedPreparedBatch?.BatchId;
        var current = PendingBatches.ToDictionary(row => row.BatchId);
        PendingBatches.Clear();
        foreach (var id in ids)
            if (current.TryGetValue(id, out var row))
                PendingBatches.Add(row with
                {
                    PreparedAtText = row.PreparedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                    FilterText = FormatFilter(row.FilterStartDate, row.FilterEndDate),
                    StatusText = Read("GestionExportPreparedStatus", "PREPARED"),
                });
        SelectedPreparedBatch = selectedId is { } selected ? PendingBatches.FirstOrDefault(row => row.BatchId == selected) : PendingBatches.FirstOrDefault();
    }

    private string FormatDateRange() => (startDate, endDate) switch
    {
        (null, null) => Read("GestionExportAllDates", "All applicable dates"),
        ({ } start, null) => Format("GestionExportFromDate", "From {0} inclusive", start.ToString("d", CultureInfo.CurrentCulture)),
        (null, { } end) => Format("GestionExportToDate", "Through {0} inclusive", end.ToString("d", CultureInfo.CurrentCulture)),
        ({ } start, { } end) => Format("GestionExportDateRange", "{0} through {1}, inclusive", start.ToString("d", CultureInfo.CurrentCulture), end.ToString("d", CultureInfo.CurrentCulture)),
    };

    private string FormatFilter(DateOnly? start, DateOnly? end) => (start, end) switch
    {
        (null, null) => Read("GestionExportAllDates", "All applicable dates"),
        ({ } startDateValue, null) => Format("GestionExportFromDate", "From {0} inclusive", startDateValue.ToString("d", CultureInfo.CurrentCulture)),
        (null, { } endDateValue) => Format("GestionExportToDate", "Through {0} inclusive", endDateValue.ToString("d", CultureInfo.CurrentCulture)),
        ({ } startDateValue, { } endDateValue) => Format("GestionExportDateRange", "{0} through {1}, inclusive", startDateValue.ToString("d", CultureInfo.CurrentCulture), endDateValue.ToString("d", CultureInfo.CurrentCulture)),
    };

    private string Read(string key, string fallback) => localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private string Format(string key, string fallback, params object[] args) => string.Format(CultureInfo.CurrentCulture, Read(key, fallback), args);

    private void SetBusy(bool value)
    {
        if (busy == value) return;
        busy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanPreview));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(CanRetryPrepared));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
