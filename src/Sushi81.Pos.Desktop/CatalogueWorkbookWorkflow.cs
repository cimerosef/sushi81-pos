using System.ComponentModel;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.Desktop;

/// <summary>Small, presentation-owned seam for native Catalogue workbook file dialogs.</summary>
public interface ICatalogueWorkbookFileDialogs
{
    string? ShowSave(object owner, string suggestedFileName, string? filter = null);

    string? ShowOpen(object owner, string? filter = null);
}

/// <summary>One row shown in the read-only import preview issue list.</summary>
public sealed record CatalogueImportIssuePresentation(
    CatalogueImportIssueSeverity Severity,
    string SeverityText,
    string Worksheet,
    string Row,
    string Field,
    string Message,
    string Code);

/// <summary>One informational affected-row entry shown in the import preview.</summary>
public sealed record CatalogueImportAffectedRowPresentation(
    string Worksheet,
    int ExcelRow,
    string EntityType,
    string Actions);

public enum CatalogueImportWorkflowOutcome
{
    None,
    PreviewFailed,
    CommitFailed,
    NoChange,
    Changed,
    RefreshFailed,
}

/// <summary>Localized mapping for parser/planner/commit diagnostics.</summary>
public static class CatalogueImportIssuePresenter
{
    private static readonly IReadOnlyDictionary<string, string> KnownMessageKeys = BuildKnownMessageKeys();

    public static IReadOnlyCollection<string> KnownStableCodes => KnownMessageKeys.Keys.ToArray();

    public static IReadOnlyList<CatalogueImportIssuePresentation> Present(
        IEnumerable<CatalogueImportIssue> issues,
        IReadOnlyDictionary<string, string> localized)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(localized);
        return issues.Select(issue => new CatalogueImportIssuePresentation(
            issue.Severity,
            issue.Severity == CatalogueImportIssueSeverity.Error
                ? Read(localized, "Errors", "Errors")
                : Read(localized, "Warnings", "Warnings"),
            issue.Worksheet ?? string.Empty,
            issue.ExcelRow?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            issue.FieldKey ?? string.Empty,
            Message(issue, localized),
            issue.Code)).ToArray();
    }

    public static string Message(CatalogueImportIssue issue, IReadOnlyDictionary<string, string> localized)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(localized);

        if (KnownMessageKeys.TryGetValue(issue.Code, out var key) && localized.TryGetValue(key, out var known))
            return known;

        var generic = Read(localized, "CatalogueImportGenericIssue", "Import issue ({0}).");
        // Future codes retain a stable diagnostic identity without exposing a raw
        // parser/exception message as the normal operator-facing text.
        return string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            generic,
            issue.Code);
    }

    private static Dictionary<string, string> BuildKnownMessageKeys()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(result, "CatalogueImportWorkbookStructureIssue",
            "unreadable-workbook", "missing-sheet", "unsupported-sheet", "unsupported-contract-version",
            "business-sheet-visibility", "descriptor-corrupt", "duplicate-manifest-key", "duplicate-row-binding",
            "formula-not-allowed", "malformed-entity-id", "manifest-corrupt", "metadata-corrupt", "metadata-visibility",
            "misbound-identity", "missing-header", "sheet-order", "unknown-row-key", "wrong-parent-binding");
        Add(result, "CatalogueImportCatalogueValidationIssue",
            "add-only-existing-binding", "add-only-product-code-collision", "ambiguous-parent", "category-short-code-duplicate",
            "category-short-code-too-long", "conflicting-category-short-code", "duplicate-category-name", "duplicate-entity-id",
            "duplicate-product-code", "duplicate-row-key", "existing-category-short-code-change", "invalid-group-structure",
            "invalid-mode", "invalid-option-structure", "invalid-scalar", "missing-parent", "price-negative",
            "required-active-choices", "required-field", "stale-conflict", "unknown-entity-id", "vat-range", "wrong-entity-type",
            "preview-required");
        Add(result, "CatalogueImportCommitValidationIssue",
            "category-name-duplicate", "category-operation-forbidden", "category-payload-mismatch", "category-reference-missing",
            "category-short-code-invalid", "category-short-code-mismatch", "contradictory-operation", "contradictory-state-operation",
            "duplicate-baseline-category-id", "duplicate-baseline-group-id", "duplicate-baseline-option-id", "duplicate-baseline-product-id",
            "duplicate-create", "duplicate-modify", "duplicate-operation", "duplicate-state-operation", "invalid-category-key",
            "invalid-group-action", "invalid-local-key", "invalid-operation", "invalid-operation-id", "invalid-product",
            "misbound-reference", "mixed-create", "modify-missing", "modify-redundant", "orphan-category", "parent-payload-mismatch",
            "parent-reference-missing", "reparent-forbidden", "state-operation-missing", "state-payload-mismatch", "state-redundant",
            "unexpected-category-reference", "unexpected-parent-reference", "unknown-category-reference", "unknown-parent-reference");
        Add(result, "CatalogueImportCommitFailure",
            "authority-blocked", "baseline-token-mismatch", "commit-store-unavailable", "concurrent-write-conflict",
            "invalid-allocated-id", "invalid-request", "persistence-conflict", "stale-baseline", "transaction-failed", "unknown-reference");
        result["existing-category-short-code-change"] = "CatalogueImportCategoryShortCodeChange";
        result["stale-conflict"] = "CatalogueImportStaleConflict";
        result["stale-baseline"] = "CatalogueImportStaleConflict";
        result["baseline-token-mismatch"] = "CatalogueImportStaleConflict";
        result["authority-blocked"] = "CatalogueImportAuthorityBlocked";
        result["commit-store-unavailable"] = "CatalogueImportCommitUnavailable";
        result["persistence-conflict"] = "CatalogueImportPersistenceConflict";
        result["transaction-failed"] = "CatalogueImportPersistenceConflict";
        result["concurrent-write-conflict"] = "CatalogueImportConcurrentWriteConflict";
        result["unreadable-workbook"] = "CatalogueImportUnreadableWorkbook";
        result["missing-sheet"] = "CatalogueImportMissingSheet";
        result["unsupported-sheet"] = "CatalogueImportUnsupportedSheet";
        result["unsupported-contract-version"] = "CatalogueImportUnsupportedContract";
        result["invalid-mode"] = "CatalogueImportInvalidMode";
        return result;
    }

    private static void Add(Dictionary<string, string> target, string key, params string[] codes)
    {
        foreach (var code in codes) target[code] = key;
    }

    private static string Read(IReadOnlyDictionary<string, string> localized, string key, string fallback) =>
        localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}

/// <summary>
/// Application-facing Desktop workflow state. It owns no WPF controls and is therefore
/// deterministic under tests; MainWindow supplies native dialogs and renders the state.
/// </summary>
public sealed class CatalogueWorkbookWorkflowViewModel : INotifyPropertyChanged
{
    private readonly CatalogueWorkbookService exporter;
    private readonly CatalogueImportService importer;
    private readonly IWriteAuthorityGuard authorityGuard;
    private readonly Func<bool> presentationRefreshBlocked;
    private readonly Func<CancellationToken, Task>? refreshAfterChangedImport;
    private IReadOnlyDictionary<string, string> localized;
    private bool busy;
    private bool commitAttempted;
    private CatalogueImportResult? preview;
    private CatalogueImportCommitResult? lastCommit;
    private IReadOnlyList<CatalogueImportIssue> displayedIssues = [];
    private CatalogueImportWorkflowOutcome outcome;

    public CatalogueWorkbookWorkflowViewModel(
        CatalogueWorkbookService exporter,
        CatalogueImportService importer,
        IWriteAuthorityGuard authorityGuard,
        IReadOnlyDictionary<string, string>? localized = null,
        Func<bool>? presentationRefreshBlocked = null,
        Func<CancellationToken, Task>? refreshAfterChangedImport = null)
    {
        this.exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        this.importer = importer ?? throw new ArgumentNullException(nameof(importer));
        this.authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
        this.localized = localized ?? new Dictionary<string, string>(StringComparer.Ordinal);
        this.presentationRefreshBlocked = presentationRefreshBlocked ?? (() => false);
        this.refreshAfterChangedImport = refreshAfterChangedImport;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsBusy => busy;

    public bool CanExport => !busy && !presentationRefreshBlocked();

    public bool CanImport => !busy && !presentationRefreshBlocked();

    public bool IsAuthoritative => authorityGuard.State == WriteAuthorityState.Authoritative;

    public CatalogueImportResult? Preview => preview;

    public CatalogueImportCommitResult? LastCommit => lastCommit;

    public CatalogueImportWorkflowOutcome Outcome => outcome;

    public bool IsCompleted => !busy && outcome is not CatalogueImportWorkflowOutcome.None;

    public IReadOnlyList<CatalogueImportIssuePresentation> Issues =>
        CatalogueImportIssuePresenter.Present(displayedIssues, localized);

    public IReadOnlyList<CatalogueImportAffectedRowPresentation> AffectedRows =>
        preview?.Preview.AffectedRows.Select(row => new CatalogueImportAffectedRowPresentation(
            row.Worksheet,
            row.ExcelRow,
            LocalizeEntity(row.EntityType),
            FormatAffectedRowActions(row))).ToArray() ?? [];

    private string FormatAffectedRowActions(CatalogueImportAffectedRow row)
    {
        var actions = string.Join(", ", row.Operations.Select(LocalizeOperation));
        if (row.EntityType != CatalogueImportEntityType.Product) return actions;
        var operation = preview?.Plan?.Operations.FirstOrDefault(value =>
            value.EntityType == CatalogueImportEntityType.Product && value.ExcelRow == row.ExcelRow && value.Worksheet == row.Worksheet);
        if (operation is null || !operation.Values.TryGetValue("vatRate", out var vatRate) || string.IsNullOrWhiteSpace(vatRate)) return actions;
        return $"{actions}, {Read("Vat", "VAT")} {vatRate}%";
    }

    public bool CanConfirm => !busy
        && !commitAttempted
        && IsAuthoritative
        && !presentationRefreshBlocked()
        && preview is { HasErrors: false, Plan: not null, PreviewBaseline: not null };

    public async Task ExportToPathAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        if (!CanExport) throw new InvalidOperationException("Catalogue workbook export is unavailable while the workflow is busy or the presentation is refreshing.");
        if (string.IsNullOrWhiteSpace(targetPath)) throw new ArgumentException("A target path is required.", nameof(targetPath));

        var fullTarget = Path.GetFullPath(targetPath);
        var directory = Path.GetDirectoryName(fullTarget) ?? throw new InvalidOperationException("The target directory is unavailable.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(fullTarget)}.{Guid.NewGuid():N}.tmp");
        SetBusy(true);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
            {
                await exporter.ExportAsync(stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, fullTarget, overwrite: true);
        }
        finally
        {
            SetBusy(false);
            TryDelete(temporary);
        }
    }

    public async Task<CatalogueImportResult> PreviewFromPathAsync(
        string sourcePath,
        CatalogueImportMode mode,
        string? sourceName = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanImport) throw new InvalidOperationException("Catalogue workbook import is unavailable while the workflow is busy or the presentation is refreshing.");
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A source path is required.", nameof(sourcePath));
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The selected workbook does not exist.", sourcePath);

        // Never leave an older successful preview confirmable after a new file or
        // parser attempt fails. A fresh successful preview below resets the gate.
        preview = null;
        lastCommit = null;
        displayedIssues = [];
        outcome = CatalogueImportWorkflowOutcome.None;
        commitAttempted = true;
        NotifyPreviewChanged();
        SetBusy(true);
        try
        {
            await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            preview = await importer.PreviewAsync(stream, mode, sourceName ?? Path.GetFileName(sourcePath), cancellationToken);
            displayedIssues = preview.Preview.Issues;
            lastCommit = null;
            commitAttempted = false;
            NotifyPreviewChanged();
            return preview;
        }
        catch (Exception)
        {
            var issue = new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "unreadable-workbook", "The selected workbook could not be read.", "", null, null);
            preview = new CatalogueImportResult(
                new CatalogueImportPreview(mode, sourceName ?? Path.GetFileName(sourcePath), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, [issue], [], true),
                null,
                CatalogueImportBaseline.Empty);
            displayedIssues = [issue];
            outcome = CatalogueImportWorkflowOutcome.PreviewFailed;
            NotifyPreviewChanged();
            return preview;
        }
        finally { SetBusy(false); }
    }

    public async Task<CatalogueImportCommitResult> CommitPreviewAsync(CancellationToken cancellationToken = default)
    {
        if (!CanConfirm)
        {
            var issue = new CatalogueImportIssue(CatalogueImportIssueSeverity.Error, "authority-blocked", "A fresh authoritative preview is required before confirmation.");
            return CatalogueImportCommitResult.Failure(issue);
        }

        SetBusy(true);
        commitAttempted = true;
        OnPropertyChanged(nameof(CanConfirm));
        try
        {
            // Pass the exact immutable object displayed in the preview. No re-plan or
            // reconstruction is allowed between preview and commit.
            try
            {
                lastCommit = await importer.CommitAsync(preview!, cancellationToken);
            }
            catch (Exception)
            {
                lastCommit = CatalogueImportCommitResult.Failure(new CatalogueImportIssue(
                    CatalogueImportIssueSeverity.Error, "transaction-failed", "The Catalogue import could not be completed.",
                    Worksheet: null, ExcelRow: null, FieldKey: null));
                outcome = CatalogueImportWorkflowOutcome.CommitFailed;
                displayedIssues = lastCommit.Issues;
                NotifyPreviewChanged();
                return lastCommit;
            }

            displayedIssues = lastCommit.Issues;
            if (!lastCommit.Succeeded)
            {
                outcome = CatalogueImportWorkflowOutcome.CommitFailed;
            }
            else if (!lastCommit.Changed)
            {
                outcome = CatalogueImportWorkflowOutcome.NoChange;
            }
            else if (refreshAfterChangedImport is null)
            {
                outcome = CatalogueImportWorkflowOutcome.Changed;
            }
            else
            {
                try
                {
                    await refreshAfterChangedImport(cancellationToken);
                    outcome = CatalogueImportWorkflowOutcome.Changed;
                }
                catch (Exception)
                {
                    // The durable import is already successful. Keep the shell barrier
                    // fail-closed and distinguish this from a commit-stage failure.
                    outcome = CatalogueImportWorkflowOutcome.RefreshFailed;
                }
            }

            NotifyPreviewChanged();
            return lastCommit;
        }
        finally
        {
            SetBusy(false);
            OnPropertyChanged(nameof(CanConfirm));
        }
    }

    public void RefreshAuthorityState()
    {
        OnPropertyChanged(nameof(IsAuthoritative));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanConfirm));
    }

    public void RefreshPresentationState()
    {
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanConfirm));
    }

    public void ApplyLocalization(IReadOnlyDictionary<string, string> values)
    {
        localized = values ?? throw new ArgumentNullException(nameof(values));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(AffectedRows));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void NotifyPreviewChanged()
    {
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(AffectedRows));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void SetBusy(bool value)
    {
        if (busy == value) return;
        busy = value;
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanConfirm));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort cleanup; the original export failure remains authoritative */ }
    }

    private string LocalizeEntity(CatalogueImportEntityType entityType) => entityType switch
    {
        CatalogueImportEntityType.Product => Read("Product", "Product"),
        CatalogueImportEntityType.OptionGroup => Read("OptionGroup", "Option group"),
        CatalogueImportEntityType.Option => Read("Option", "Option"),
        CatalogueImportEntityType.Category => Read("Category", "Category"),
        _ => Read("Entity", "Entity")
    };

    private string LocalizeOperation(CatalogueImportOperationKind operation) => operation switch
    {
        CatalogueImportOperationKind.Create => Read("Create", "Create"),
        CatalogueImportOperationKind.Modify => Read("Modify", "Modify"),
        CatalogueImportOperationKind.Activate => Read("Activate", "Activate"),
        CatalogueImportOperationKind.Deactivate => Read("Deactivate", "Deactivate"),
        _ => Read("Actions", "Action")
    };

    private string Read(string key, string fallback) => localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
}
