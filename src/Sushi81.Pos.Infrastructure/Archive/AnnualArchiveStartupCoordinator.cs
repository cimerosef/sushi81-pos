using Microsoft.Extensions.Logging;
using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.Infrastructure.Archive;

public enum AnnualArchiveStartupOutcome
{
    NoTargetYet,
    SkippedNotAuthoritative,
    CompletedNow,
    AlreadyCompleted,
    FailedRetryable
}

public sealed record AnnualArchiveStartupResult(
    AnnualArchiveStartupOutcome Outcome,
    AnnualArchiveFinalizationResult? Finalization = null);

/// <summary>
/// Runs the accepted WP2 annual-archive finalizer once at a safe application startup.
/// It does not duplicate archive policy/publication logic and never schedules an
/// in-session retry or emits a second durable-change notification.
/// </summary>
public sealed partial class AnnualArchiveStartupCoordinator(
    IWriteAuthorityGuard authorityGuard,
    Func<CancellationToken, Task<AnnualArchiveFinalizationResult>> finalizeAsync,
    ILogger logger)
{
    private readonly IWriteAuthorityGuard authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
    private readonly Func<CancellationToken, Task<AnnualArchiveFinalizationResult>> finalizeAsync = finalizeAsync ?? throw new ArgumentNullException(nameof(finalizeAsync));
    private readonly ILogger logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public async Task<AnnualArchiveStartupResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (authorityGuard.State != WriteAuthorityState.Authoritative)
            return new(AnnualArchiveStartupOutcome.SkippedNotAuthoritative);

        try
        {
            var finalization = await finalizeAsync(cancellationToken);
            return finalization.Outcome switch
            {
                AnnualArchiveFinalizationOutcome.NoTargetYet => new(AnnualArchiveStartupOutcome.NoTargetYet, finalization),
                AnnualArchiveFinalizationOutcome.CompletedNow => new(AnnualArchiveStartupOutcome.CompletedNow, finalization),
                AnnualArchiveFinalizationOutcome.AlreadyCompleted => new(AnnualArchiveStartupOutcome.AlreadyCompleted, finalization),
                _ => throw new InvalidDataException("The annual archive finalizer returned an unsupported startup outcome.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogArchiveFailure(logger, exception.GetType().Name);
            return new(AnnualArchiveStartupOutcome.FailedRetryable);
        }
    }

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Error,
        Message = "Automatic annual archive startup maintenance failed with {ExceptionType}; eligible live data remains retryable for a later authoritative startup.")]
    private static partial void LogArchiveFailure(ILogger logger, string exceptionType);
}
