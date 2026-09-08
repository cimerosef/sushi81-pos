using Microsoft.Extensions.Logging;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;

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
    ILogger logger,
    ISystemMetadataStore? systemMetadata = null)
{
    private const int CanonicalSchemaVersion = 2;

    /// <summary>
    /// Resolves authority after migration using evidence captured before migration. This keeps a
    /// newly created database from qualifying for the one-time legacy bootstrap merely because
    /// startup just created its migration history.
    /// </summary>
    public Task<AuthorityResolution> InitializeAsync(
        bool legacyBootstrapEvidence,
        CancellationToken cancellationToken = default)
        => InitializeAsync(legacyBootstrapEvidence, preMigrationLiveDatabaseEvidence: true, cancellationToken);

    /// <summary>
    /// Resolves authority after migration using evidence captured before migration. The live
    /// database evidence is a defense-in-depth input: an established state must never become
    /// writable after startup has already observed that its pre-existing database was absent.
    /// </summary>
    public async Task<AuthorityResolution> InitializeAsync(
        bool legacyBootstrapEvidence,
        bool preMigrationLiveDatabaseEvidence,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await store.LoadAsync(cancellationToken);
            if (document is not null)
            {
                if (!preMigrationLiveDatabaseEvidence)
                    return FailClosed(new InvalidDataException("The established authority state cannot be restored without pre-existing live database evidence."));
                if (!await store.HasBootstrapMarkerAsync(cancellationToken))
                    return FailClosed(new InvalidDataException("The durable authority state exists without its bootstrap marker."));
                if (!await store.HasBootstrapAnchorAsync(cancellationToken))
                    return FailClosed(new InvalidDataException("The durable authority state exists without its independent bootstrap anchor."));
                if (document.SchemaVersion == 1)
                {
                    var phase = document.State switch
                    {
                        WriteAuthorityState.Authoritative => AuthorityPhase.Authoritative,
                        WriteAuthorityState.NonAuthoritativeReadOnly => AuthorityPhase.NonAuthoritativeReadOnly,
                        WriteAuthorityState.Transitioning or WriteAuthorityState.RecoveryRequired => AuthorityPhase.RecoveryRequired,
                        _ => AuthorityPhase.RecoveryRequired
                    };
                    // Only accepted legacy authority establishes a lineage. A legacy non-writer
                    // retains its coarse non-writer state without inventing membership or authority.
                    var established = phase == AuthorityPhase.Authoritative;
                    var protocol = new AuthorityProtocolState(
                        Revision: 1,
                        DeviceId: Guid.NewGuid(),
                        DisplayName: Environment.MachineName,
                        LineageId: established ? Guid.NewGuid() : null,
                        Generation: established ? 1 : 0,
                        HandoffVersion: 0,
                        BusinessRevision: 0,
                        Phase: phase);
                    protocol.Validate();
                    document = new AuthorityStateDocument(CanonicalSchemaVersion, protocol.WriteState, clock.UtcNow) { Protocol = protocol };
                    await store.SaveAsync(document, cancellationToken);
                }
                if (document.Protocol is { } observedProtocol
                    && await ObserveNewerGenerationAsync(observedProtocol, cancellationToken))
                {
                    var staleDocument = await store.LoadAsync(cancellationToken)
                        ?? throw new InvalidDataException("The stale-generation authority state disappeared after persistence.");
                    guard.SetState(staleDocument.EffectiveState);
                    return new(staleDocument.EffectiveState, null);
                }
                if (document.Protocol is { Phase: AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority } existingProtocol)
                {
                    try
                    {
                        await EnsureAuthoritativeMembershipAsync(existingProtocol, cancellationToken);
                    }
                    catch (Exception exception) when (IsUnavailableSystemMetadata(exception))
                    {
                        // Ordinary authority is local-first. A disconnected/unavailable
                        // OneDrive System publication is a deferred diagnostic, not a remote
                        // lease failure and must not revoke a valid local writer.
                        LogSystemMetadataUnavailable(logger, exception);
                    }
                }
                guard.SetState(document.EffectiveState);
                return new(document.EffectiveState, null);
            }

            if (await store.HasBootstrapMarkerAsync(cancellationToken))
                return FailClosed(new InvalidDataException("The durable authority state is missing after local bootstrap."));

            if (await store.HasBootstrapAnchorAsync(cancellationToken))
                return FailClosed(new InvalidDataException("The durable authority state is missing after established M06 bootstrap."));

            if (!legacyBootstrapEvidence)
                return FailClosed(new InvalidDataException("No supported M01-M05 local data evidence permits the one-time authority bootstrap."));

            // M06 permits the one-time local single-device bootstrap after the supported M01-M05
            // schema has been migrated. The marker and independent data-directory anchor make
            // deletion of the state files fail closed later.
            var bootstrapProtocol = new AuthorityProtocolState(
                Revision: 1,
                DeviceId: Guid.NewGuid(),
                DisplayName: Environment.MachineName,
                LineageId: Guid.NewGuid(),
                Generation: 1,
                HandoffVersion: 0,
                BusinessRevision: 0,
                Phase: AuthorityPhase.Authoritative);
            bootstrapProtocol.Validate();
            var bootstrap = new AuthorityStateDocument(CanonicalSchemaVersion, bootstrapProtocol.WriteState, clock.UtcNow)
            {
                Protocol = bootstrapProtocol
            };
            await store.SaveAsync(bootstrap, cancellationToken);
            await store.WriteBootstrapMarkerAsync(cancellationToken);
            await store.WriteBootstrapAnchorAsync(cancellationToken);
            await EnsureAuthoritativeMembershipAsync(bootstrapProtocol, cancellationToken);
            guard.SetState(WriteAuthorityState.Authoritative);
            return new(WriteAuthorityState.Authoritative, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            return FailClosed(exception);
        }
    }

    private async Task EnsureAuthoritativeMembershipAsync(
        AuthorityProtocolState protocol,
        CancellationToken cancellationToken)
    {
        if (systemMetadata is null || protocol.LineageId is not { } lineageId || protocol.Generation < 1)
            return;

        // System metadata is a non-authority membership publication. A contradiction is
        // surfaced to startup as RecoveryRequired; it can never rewrite another lineage or
        // promote a joining device.
        await systemMetadata.EnsureCurrentLineageAsync(lineageId, protocol.Generation, cancellationToken);
        await systemMetadata.JoinCurrentGenerationAsync(protocol.DeviceId, protocol.DisplayName, cancellationToken);
    }

    private async Task<bool> ObserveNewerGenerationAsync(
        AuthorityProtocolState protocol,
        CancellationToken cancellationToken)
    {
        if (systemMetadata is null || protocol.LineageId is not { } lineageId || protocol.Generation < 1
            || protocol.Phase is AuthorityPhase.DisasterRecoveryPreparing or AuthorityPhase.DisasterRecoveryPending)
            return false;

        SystemLineageMetadata lineage;
        try
        {
            lineage = await systemMetadata.ReadLineageAsync(cancellationToken);
        }
        catch (Exception exception) when (IsUnavailableSystemMetadata(exception))
        {
            return false;
        }

        if (lineage.LineageId != lineageId)
            throw new InvalidDataException("The readable System lineage contradicts the local authority lineage.");
        if (lineage.CurrentGeneration < protocol.Generation)
            throw new InvalidDataException("The readable System generation regressed behind local authority state.");
        if (lineage.CurrentGeneration == protocol.Generation)
            return false;

        var stale = protocol with
        {
            Revision = checked(protocol.Revision + 1),
            Phase = AuthorityPhase.StaleGeneration,
            Transfer = null,
            Recovery = null
        };
        stale.Validate();
        await store.SaveAsync(new AuthorityStateDocument(CanonicalSchemaVersion, stale.WriteState, clock.UtcNow) { Protocol = stale }, cancellationToken);
        guard.SetState(stale.WriteState);
        LogStaleGeneration(logger, protocol.Generation, lineage.CurrentGeneration);
        return true;
    }

    private AuthorityResolution FailClosed(Exception exception)
    {
        guard.SetState(WriteAuthorityState.RecoveryRequired);
        LogAuthorityStateFailure(logger, exception);
        return new(WriteAuthorityState.RecoveryRequired, exception);
    }

    private static bool IsUnavailableSystemMetadata(Exception exception) => exception switch
    {
        SystemMetadataUnavailableException => true,
        DirectoryNotFoundException => true,
        FileNotFoundException => true,
        UnauthorizedAccessException => true,
        IOException => true,
        _ => false
    };

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Local authority state could not be validated; startup is read-only.")]
    private static partial void LogAuthorityStateFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Warning, Message = "Shared System metadata is unavailable; retaining valid local authority and deferring membership publication.")]
    private static partial void LogSystemMetadataUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "A newer shared System generation {currentGeneration} was observed over local generation {localGeneration}; local business writes are fenced until reinitialization.")]
    private static partial void LogStaleGeneration(ILogger logger, long localGeneration, long currentGeneration);
}
