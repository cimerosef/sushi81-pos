using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Recovery;
using Sushi81.Pos.Application.Foundation.Time;

namespace Sushi81.Pos.Application.Export;

/// <summary>Application-facing seam for the versioned Gestion intermediate workbook.</summary>
public interface IExportWorkbookGateway
{
    Task WriteAsync(
        ExportBatchPayload payload,
        Stream destination,
        CancellationToken cancellationToken = default);

    Task ValidateAsync(
        Stream source,
        ExportBatchPayload expectedPayload,
        CancellationToken cancellationToken = default);
}

public sealed record ExportWorkbookResult(
    Guid BatchId,
    string FinalPath,
    bool IsRegeneration);

/// <summary>Distinguishes an empty selection from a selection blocked by diagnostics.</summary>
public sealed record ExportWorkbookGenerationOutcome(
    ExportWorkbookResult? Result,
    ExportSelectionResult Selection);

/// <summary>
/// Orchestrates the durable WP1 batch with the WP2 workbook boundary.  A batch is
/// marked SUCCESS only after a staged workbook has been reopened, validated and
/// finalized at the requested path.
/// </summary>
public sealed class GestionExportWorkbookService(
    GestionExportService exportService,
    IExportLedgerStore ledger,
    IExportWorkbookGateway workbookGateway,
    IBusinessClock clock,
    IWriteAuthorityGuard authorityGuard)
{
    private readonly GestionExportService exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    private readonly IExportLedgerStore ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
    private readonly IExportWorkbookGateway workbookGateway = workbookGateway ?? throw new ArgumentNullException(nameof(workbookGateway));
    private readonly IBusinessClock clock = clock ?? throw new ArgumentNullException(nameof(clock));
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));

    public Task<ExportSelectionResult> SelectAsync(
        ExportSelectionOptions options,
        CancellationToken cancellationToken = default) =>
        exportService.SelectAsync(options, cancellationToken);

    public async Task<ExportWorkbookResult?> GenerateAsync(
        ExportSelectionOptions options,
        string appVersion,
        string finalPath,
        CancellationToken cancellationToken = default)
    {
        var outcome = await GenerateWithOutcomeAsync(options, appVersion, finalPath, cancellationToken);
        return outcome.Result;
    }

    public async Task<ExportWorkbookGenerationOutcome> GenerateWithOutcomeAsync(
        ExportSelectionOptions options,
        string appVersion,
        string finalPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        authorityGuard.RequireWriteAuthority();
        var normalizedPath = NormalizeFinalPath(finalPath);
        var preparation = await exportService.PrepareBatchAsync(options, appVersion, cancellationToken);
        if (preparation.Batch is null)
            return new ExportWorkbookGenerationOutcome(null, preparation.Selection);

        await GenerateAndFinalizeAsync(preparation.Batch.Payload, normalizedPath, cancellationToken);
        await exportService.MarkBatchSucceededAsync(preparation.Batch, clock.UtcNow, cancellationToken);
        return new ExportWorkbookGenerationOutcome(
            new ExportWorkbookResult(preparation.Batch.Payload.Meta.BatchId, normalizedPath, false),
            preparation.Selection);
    }

    /// <summary>Regenerates an already successful batch without selecting current orders or emitting a new action.</summary>
    public async Task<ExportWorkbookResult> RegenerateAsync(
        Guid batchId,
        string finalPath,
        CancellationToken cancellationToken = default)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("A batch identity is required.", nameof(batchId));
        var normalizedPath = NormalizeFinalPath(finalPath);
        var batch = await ledger.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException("The requested export batch does not exist.");
        if (batch.Status != ExportBatchStatus.Success)
            throw new InvalidOperationException("Only a successful export batch can be regenerated.");

        await GenerateAndFinalizeAsync(batch.Payload, normalizedPath, cancellationToken);
        return new ExportWorkbookResult(batchId, normalizedPath, true);
    }

    /// <summary>
    /// Completes a previously prepared batch after a caller recovered from a
    /// post-finalization ledger failure. The same immutable batch is reused.
    /// </summary>
    public async Task<ExportWorkbookResult> FinalizePreparedBatchAsync(
        Guid batchId,
        string finalPath,
        CancellationToken cancellationToken = default)
    {
        if (batchId == Guid.Empty) throw new ArgumentException("A batch identity is required.", nameof(batchId));
        authorityGuard.RequireWriteAuthority();
        var normalizedPath = NormalizeFinalPath(finalPath);
        var batch = await ledger.GetBatchAsync(batchId, cancellationToken)
            ?? throw new InvalidOperationException("The requested export batch does not exist.");
        if (batch.Status != ExportBatchStatus.Prepared)
            throw new InvalidOperationException("Only a prepared export batch can be finalized.");

        await GenerateAndFinalizeAsync(batch.Payload, normalizedPath, cancellationToken);
        await exportService.MarkBatchSucceededAsync(batch, clock.UtcNow, cancellationToken);
        return new ExportWorkbookResult(batchId, normalizedPath, false);
    }

    private async Task GenerateAndFinalizeAsync(
        ExportBatchPayload payload,
        string finalPath,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new ArgumentException("The export path must include a directory.", nameof(finalPath));
        Directory.CreateDirectory(directory);
        var stagingPath = Path.Combine(
            directory,
            $".{Path.GetFileName(finalPath)}.{payload.Meta.BatchId:N}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var output = new FileStream(
                stagingPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await workbookGateway.WriteAsync(payload, output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            await using (var staged = new FileStream(stagingPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
                await workbookGateway.ValidateAsync(staged, payload, cancellationToken);

            if (File.Exists(finalPath))
            {
                // A crash after finalization but before the ledger commit is retryable
                // only when the existing file is this exact batch. Never replace an
                // unrelated or unknown good file implicitly.
                await using var existing = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                await workbookGateway.ValidateAsync(existing, payload, cancellationToken);
                return;
            }

            File.Move(stagingPath, finalPath, overwrite: false);
        }
        finally
        {
            if (File.Exists(stagingPath))
                File.Delete(stagingPath);
        }
    }

    private static string NormalizeFinalPath(string finalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPath);
        return Path.GetFullPath(finalPath);
    }
}
