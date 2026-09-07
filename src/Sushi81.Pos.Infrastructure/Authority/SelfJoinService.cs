using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Time;
using Sushi81.Pos.Application.Pairing.SystemMetadata;

namespace Sushi81.Pos.Infrastructure.Authority;

/// <summary>
/// Completes the non-authority self-join transition for a fresh/replacement installation.
/// Device identity is durable before shared membership publication; membership never enables
/// writes and no old authoritative device is consulted.
/// </summary>
public sealed class SelfJoinService(
    IAuthorityStateStore authorityStore,
    WriteAuthorityGuard guard,
    ISystemMetadataStore systemMetadata,
    IBusinessClock clock)
{
    public async Task<DeviceSelfJoinResult> JoinAsync(
        string displayName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A device display name is required.", nameof(displayName));

        var existing = await authorityStore.LoadAsync(cancellationToken);
        var protocol = existing?.Protocol;
        if (protocol is not null && !CanResumeFreshOrUnboundJoin(protocol))
            throw new InvalidOperationException("Self-join is only available to a fresh or safely migrated unbound read-only installation.");
        if (protocol is { Phase: AuthorityPhase.NonAuthoritativeReadOnly })
        {
            if (!await authorityStore.HasBootstrapMarkerAsync(cancellationToken)
                || !await authorityStore.HasBootstrapAnchorAsync(cancellationToken))
                throw new InvalidDataException("An established unbound installation is missing independent local authority evidence.");
        }

        var deviceId = protocol is not null && protocol.DeviceId != Guid.Empty
            ? protocol.DeviceId
            : Guid.NewGuid();
        var identity = new AuthorityProtocolState(
            Revision: Math.Max(protocol?.Revision ?? 0, 1),
            DeviceId: deviceId,
            DisplayName: displayName.Trim(),
            LineageId: null,
            Generation: 0,
            HandoffVersion: 0,
            BusinessRevision: 0,
            Phase: AuthorityPhase.Uninitialized);
        identity.Validate();

        // Persisting this state is the local identity boundary. If the process dies after this
        // write, retry reuses the same ID; a deleted/reinstalled config necessarily gets a new ID.
        await PersistAsync(identity, cancellationToken);

        try
        {
            // Self-join establishes the same independent local evidence boundary used by
            // M06. Both writes are idempotent; a crash between them leaves the canonical
            // state non-writable and a retry completes the evidence without a new identity.
            await authorityStore.WriteBootstrapMarkerAsync(cancellationToken);
            await authorityStore.WriteBootstrapAnchorAsync(cancellationToken);
            var lineage = await systemMetadata.ReadLineageAsync(cancellationToken);
            var registration = await systemMetadata.JoinCurrentGenerationAsync(deviceId, displayName, cancellationToken);
            var next = identity with
            {
                Revision = checked(identity.Revision + 1),
                LineageId = lineage.LineageId,
                Generation = lineage.CurrentGeneration,
                // The current self-join seam validates an optional seed but does not install
                // its SQLite payload. Do not claim hydrated data until an explicit safe
                // installer exists; remain paired/uninitialized and read-only.
                BusinessRevision = 0,
                Phase = AuthorityPhase.PairedUninitializedReadOnly
            };
            await PersistAsync(next, cancellationToken);
            return registration with { Seed = null };
        }
        catch
        {
            guard.SetState(WriteAuthorityState.RecoveryRequired);
            throw;
        }
    }

    private async Task PersistAsync(AuthorityProtocolState state, CancellationToken cancellationToken)
    {
        state.Validate();
        await authorityStore.SaveAsync(
            new AuthorityStateDocument(2, state.WriteState, clock.UtcNow) { Protocol = state },
            cancellationToken);
        guard.SetState(state.WriteState);
    }

    private static bool CanResumeFreshOrUnboundJoin(AuthorityProtocolState protocol) =>
        protocol.Phase == AuthorityPhase.Uninitialized
        || protocol.Phase == AuthorityPhase.NonAuthoritativeReadOnly
            && protocol.LineageId is null
            && protocol.Generation == 0
            && protocol.HandoffVersion == 0
            && protocol.BusinessRevision == 0
            && protocol.Transfer is null
            && protocol.Recovery is null;
}
