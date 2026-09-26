using Microsoft.Extensions.Logging;
using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.Infrastructure.Export;

public enum GestionExportCompactionStartupOutcome
{
    SkippedNotAuthoritative,
    Completed,
    FailedRetryable
}

public sealed record GestionExportCompactionStartupResult(
    GestionExportCompactionStartupOutcome Outcome,
    GestionExportCompactionResult? Compaction = null);

/// <summary>Runs one safe, non-fatal Gestion history compaction attempt during application startup.</summary>
public sealed partial class GestionExportCompactionStartupCoordinator(
    IWriteAuthorityGuard authorityGuard,
    SqliteGestionExportCompactionService compactionService,
    ILogger logger)
{
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly SqliteGestionExportCompactionService compactionService = compactionService ?? throw new ArgumentNullException(nameof(compactionService));
    private readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<GestionExportCompactionStartupResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (authorityGuard.State != WriteAuthorityState.Authoritative)
            return new(GestionExportCompactionStartupOutcome.SkippedNotAuthoritative);

        try
        {
            return new(GestionExportCompactionStartupOutcome.Completed,
                await compactionService.CompactAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogCompactionFailure(logger, exception);
            return new(GestionExportCompactionStartupOutcome.FailedRetryable);
        }
    }

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "Gestion export startup compaction failed; transactional export history was retained and can be retried at a later authoritative startup.")]
    private static partial void LogCompactionFailure(ILogger logger, Exception exception);
}