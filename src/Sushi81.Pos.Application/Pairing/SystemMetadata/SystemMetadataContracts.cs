using Sushi81.Pos.Application.Foundation.Authority;

namespace Sushi81.Pos.Application.Pairing.SystemMetadata;

/// <summary>
/// The shared System root or a required metadata artifact is temporarily unavailable.
/// This is distinct from readable contradictory/corrupt metadata, which must fail closed.
/// </summary>
public sealed class SystemMetadataUnavailableException(string message, Exception? innerException = null)
    : IOException(message, innerException);

/// <summary>Stable names and validation rules for non-authority OneDrive System artifacts.</summary>
public static class SystemMetadataContract
{
    public const int SchemaVersion = 1;
    public const string ProtocolVersion = "M07";
    public const string SystemDirectoryName = "System";
    public const string LineageDirectoryName = "Lineage";
    public const string DevicesDirectoryName = "Devices";
    public const string SeedsDirectoryName = "Seeds";
    public const string LineageFileName = "lineage.json";
    public const string DeviceArtifactKind = "device-membership";
    public const string ReadOnlySeedArtifactKind = "read-only-initialization-seed";

    public static string DeviceFileName(Guid deviceId) => $"{deviceId:N}.device.json";

    public static string SeedFileName(Guid seedId) => $"{seedId:N}.seed.json";

    public static string SeedPayloadFileName(Guid seedId) => $"{seedId:N}.seed.db";

    public static bool IsSha256(string? value)
    {
        if (value is null || value.Length != 64) return false;
        foreach (var character in value)
        {
            if (!Uri.IsHexDigit(character)) return false;
        }

        return true;
    }
}

/// <summary>Shared lineage identity. It contains no authority grant or writer state.</summary>
public sealed record SystemLineageMetadata(
    int SchemaVersion,
    string ProtocolVersion,
    Guid LineageId,
    long CurrentGeneration,
    DateTimeOffset CreatedAtUtc)
{
    public void Validate()
    {
        if (SchemaVersion != SystemMetadataContract.SchemaVersion
            || !string.Equals(ProtocolVersion, SystemMetadataContract.ProtocolVersion, StringComparison.Ordinal)
            || LineageId == Guid.Empty
            || CurrentGeneration < 1
            || CreatedAtUtc == default)
        {
            throw new InvalidDataException("The shared System lineage metadata is unsupported or contradictory.");
        }
    }
}

/// <summary>Immutable membership evidence for one device and one current generation.</summary>
public sealed record DeviceRegistrationArtifact(
    int SchemaVersion,
    string ProtocolVersion,
    string ArtifactKind,
    Guid DeviceId,
    string DisplayName,
    Guid LineageId,
    long Generation,
    DateTimeOffset RegisteredAtUtc)
{
    public void Validate()
    {
        if (SchemaVersion != SystemMetadataContract.SchemaVersion
            || !string.Equals(ProtocolVersion, SystemMetadataContract.ProtocolVersion, StringComparison.Ordinal)
            || !string.Equals(ArtifactKind, SystemMetadataContract.DeviceArtifactKind, StringComparison.Ordinal)
            || DeviceId == Guid.Empty
            || string.IsNullOrWhiteSpace(DisplayName)
            || LineageId == Guid.Empty
            || Generation < 1
            || RegisteredAtUtc == default)
        {
            throw new InvalidDataException("The device registration artifact is unsupported or contradictory.");
        }
    }
}

/// <summary>
/// Metadata for an optional read-only initialization seed. The shape deliberately has no
/// authority, target, grant or writer fields.
/// </summary>
public sealed record ReadOnlySeedMetadata(
    int SchemaVersion,
    string ProtocolVersion,
    string ArtifactKind,
    Guid SeedId,
    Guid LineageId,
    long Generation,
    Guid SourceDeviceId,
    long BusinessRevision,
    string PayloadFileName,
    long PayloadSize,
    string PayloadSha256,
    DateTimeOffset CreatedAtUtc)
{
    public void Validate()
    {
        if (SchemaVersion != SystemMetadataContract.SchemaVersion
            || !string.Equals(ProtocolVersion, SystemMetadataContract.ProtocolVersion, StringComparison.Ordinal)
            || !string.Equals(ArtifactKind, SystemMetadataContract.ReadOnlySeedArtifactKind, StringComparison.Ordinal)
            || SeedId == Guid.Empty
            || LineageId == Guid.Empty
            || Generation < 1
            || SourceDeviceId == Guid.Empty
            || BusinessRevision < 0
            || !string.Equals(PayloadFileName, SystemMetadataContract.SeedPayloadFileName(SeedId), StringComparison.Ordinal)
            || PayloadSize < 0
            || !SystemMetadataContract.IsSha256(PayloadSha256)
            || CreatedAtUtc == default)
        {
            throw new InvalidDataException("The read-only seed metadata is unsupported or contradictory.");
        }
    }
}

public sealed record ValidatedReadOnlySeed(ReadOnlySeedMetadata Metadata, string PayloadPath);

public enum PairingReadiness
{
    PairedUninitializedReadOnly,
    NonAuthoritativeReadOnly
}

/// <summary>
/// Result of self-join. The coarse write state is intentionally fixed to read-only; this seam
/// cannot create or return writable authority regardless of seed availability.
/// </summary>
public sealed record DeviceSelfJoinResult(
    DeviceRegistrationArtifact Registration,
    bool RegistrationCreated,
    ValidatedReadOnlySeed? Seed)
{
    public PairingReadiness Readiness => Seed is null
        ? PairingReadiness.PairedUninitializedReadOnly
        : PairingReadiness.NonAuthoritativeReadOnly;

    public WriteAuthorityState WriteAuthorityState => Readiness switch
    {
        PairingReadiness.PairedUninitializedReadOnly or PairingReadiness.NonAuthoritativeReadOnly
            => WriteAuthorityState.NonAuthoritativeReadOnly,
        _ => throw new InvalidDataException("Unsupported pairing readiness cannot be writable.")
    };
}

public sealed record ReadOnlySeedPublicationResult(ReadOnlySeedMetadata Metadata, bool Created);

/// <summary>
/// Application seam for OneDrive System membership and optional non-authority seed metadata.
/// Implementations must not mutate the canonical authority state or the business database.
/// </summary>
public interface ISystemMetadataStore
{
    Task<SystemLineageMetadata> EnsureCurrentLineageAsync(
        Guid lineageId,
        long generation,
        CancellationToken cancellationToken = default);

    Task<SystemLineageMetadata> ReadLineageAsync(CancellationToken cancellationToken = default);

    Task<DeviceSelfJoinResult> JoinCurrentGenerationAsync(
        Guid deviceId,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviceRegistrationArtifact>> ListCurrentGenerationDevicesAsync(
        Guid lineageId,
        long generation,
        CancellationToken cancellationToken = default);

    Task<ValidatedReadOnlySeed?> FindValidatedReadOnlySeedAsync(
        Guid lineageId,
        long generation,
        CancellationToken cancellationToken = default);

    Task<ReadOnlySeedPublicationResult> PublishReadOnlySeedAsync(
        ReadOnlySeedMetadata metadata,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default);
}
