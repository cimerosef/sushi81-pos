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

/// <summary>Localized mapping for parser/planner/commit diagnostics.</summary>
public static class CatalogueImportIssuePresenter
{
    private static readonly Dictionary<string, string> KnownMessageKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["existing-category-short-code-change"] = "CatalogueImportCategoryShortCodeChange",
            ["stale-conflict"] = "CatalogueImportStaleConflict",
            ["authority-blocked"] = "CatalogueImportAuthorityBlocked",
            ["commit-store-unavailable"] = "CatalogueImportCommitUnavailable",
            ["persistence-conflict"] = "CatalogueImportPersistenceConflict",
            ["concurrent-write-conflict"] = "CatalogueImportPersistenceConflict",
            ["unreadable-workbook"] = "CatalogueImportUnreadableWorkbook",
            ["missing-sheet"] = "CatalogueImportMissingSheet",
            ["unsupported-sheet"] = "CatalogueImportUnsupportedSheet",
            ["unsupported-contract-version"] = "CatalogueImportUnsupportedContract",
            ["invalid-mode"] = "CatalogueImportInvalidMode",
        };

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

        var generic = Read(localized, "CatalogueImportGenericIssue", "Import issue ({0}). Details: {1}");
        // The stable code is the primary operator-facing identity. The parser text is
        // retained as secondary detail so an unexpected future code remains diagnosable
        // without making an English exception the primary localized label.
        return string.Format(
            System.Globalization.CultureInfo.CurrentCulture,
            generic,
            issue.Code,
            issue.Message);
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

    public IReadOnlyList<CatalogueImportIssuePresentation> Issues =>
        preview is null ? [] : CatalogueImportIssuePresenter.Present(preview.Preview.Issues, localized);

    public IReadOnlyList<CatalogueImportAffectedRowPresentation> AffectedRows =>
        preview?.Preview.AffectedRows.Select(row => new CatalogueImportAffectedRowPresentation(
            row.Worksheet,
            row.ExcelRow,
            row.EntityType.ToString(),
            string.Join(", ", row.Operations.Select(operation => operation.ToString())))).ToArray() ?? [];

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
        commitAttempted = true;
        NotifyPreviewChanged();
        SetBusy(true);
        try
        {
            await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
            preview = await importer.PreviewAsync(stream, mode, sourceName ?? Path.GetFileName(sourcePath), cancellationToken);
            lastCommit = null;
            commitAttempted = false;
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
            lastCommit = await importer.CommitAsync(preview!, cancellationToken);
            if (lastCommit.Succeeded && lastCommit.Changed && refreshAfterChangedImport is not null)
                await refreshAfterChangedImport(cancellationToken);
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
}
