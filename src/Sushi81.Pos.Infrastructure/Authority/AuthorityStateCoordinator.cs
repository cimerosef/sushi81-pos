using Microsoft.Extensions.Logging;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Time;

namespace Sushi81.Pos.Infrastructure.Authority;

public sealed record AuthorityResolution(WriteAuthorityState State, Exception? Error)
{
    public bool IsUsable => Error is null;
}

/// <summary>
/// Loads the durable local authority state and performs the one-time supported single-device bootstrap.
/// Any ambiguity is represented as RecoveryRequired; it never becomes writable authority.
/// </summary>
public sealed partial class AuthorityStateCoordinator(
    IAuthorityStateStore store,
    WriteAuthorityGuard guard,
    IBusinessClock clock,
    ILogger logger)
{
    private const int CurrentSchemaVersion = 1;

    public async Task<AuthorityResolution> InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await store.LoadAsync(cancellationToken);
            if (document is not null)
            {
                if (!await store.HasBootstrapMarkerAsync(cancellationToken))
                    return FailClosed(new InvalidDataException("The durable authority state exists without its bootstrap marker."));
                guard.SetState(document.State);
                return new(document.State, null);
            }

            if (await store.HasBootstrapMarkerAsync(cancellationToken))
                return FailClosed(new InvalidDataException("The durable authority state is missing after local bootstrap."));

            // M06 permits the one-time local single-device bootstrap after the supported M01-M05
            // schema has been migrated. The marker makes deletion of the state file fail closed later.
            var bootstrap = new AuthorityStateDocument(CurrentSchemaVersion, WriteAuthorityState.Authoritative, clock.UtcNow);
            await store.SaveAsync(bootstrap, cancellationToken);
            await store.WriteBootstrapMarkerAsync(cancellationToken);
            guard.SetState(WriteAuthorityState.Authoritative);
            return new(WriteAuthorityState.Authoritative, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return FailClosed(exception);
        }
    }

    private AuthorityResolution FailClosed(Exception exception)
    {
        guard.SetState(WriteAuthorityState.RecoveryRequired);
        LogAuthorityStateFailure(logger, exception);
        return new(WriteAuthorityState.RecoveryRequired, exception);
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Local authority state could not be validated; startup is read-only.")]
    private static partial void LogAuthorityStateFailure(ILogger logger, Exception exception);
}
