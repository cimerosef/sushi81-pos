using System.IO;
using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.GitHubTransport;
using Sushi81.Pos.Application.Pairing.SystemMetadata;
using Sushi81.Pos.Infrastructure.Authority;
using Sushi81.Pos.Infrastructure.GitHubTransport;

namespace Sushi81.Pos.Desktop;

/// <summary>Composed M07 services kept behind the desktop shell; none are authority alternatives.</summary>
public sealed class M07RuntimeServices(
    WriteAuthorityGuard authorityGuard,
    IAuthorityStateStore authorityStore,
    ISystemMetadataStore systemMetadata,
    SelfJoinService selfJoin,
    NormalHandoffService? normalHandoff,
    TargetAcquisitionService? targetAcquisition,
    GitHubHandoffConnectionTester? connectionTester) : IAsyncDisposable
{
    public WriteAuthorityGuard AuthorityGuard { get; } = authorityGuard;
    public IAuthorityStateStore AuthorityStore { get; } = authorityStore;
    public ISystemMetadataStore SystemMetadata { get; } = systemMetadata;
    public SelfJoinService SelfJoin { get; } = selfJoin;
    public NormalHandoffService? NormalHandoff { get; } = normalHandoff;
    public TargetAcquisitionService? TargetAcquisition { get; } = targetAcquisition;
    public GitHubHandoffConnectionTester? ConnectionTester { get; } = connectionTester;

    public async Task<IReadOnlyList<DeviceRegistrationArtifact>> GetEligibleTransferTargetsAsync(
        CancellationToken cancellationToken = default)
    {
        var document = await AuthorityStore.LoadAsync(cancellationToken);
        var protocol = document?.Protocol
            ?? throw new InvalidDataException("The canonical M07 authority state is unavailable.");
        protocol.Validate();
        if (protocol.Phase is not (AuthorityPhase.Authoritative or AuthorityPhase.ClosedRetainedAuthority)
            || protocol.LineageId is not { } lineageId)
            throw new WriteAuthorityException(protocol.WriteState);
        return (await SystemMetadata.ListCurrentGenerationDevicesAsync(lineageId, protocol.Generation, cancellationToken))
            .Where(device => device.DeviceId != protocol.DeviceId)
            .ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        if (TargetAcquisition is not null) await TargetAcquisition.DisposeAsync();
        if (NormalHandoff is not null) await NormalHandoff.DisposeAsync();
    }
}
