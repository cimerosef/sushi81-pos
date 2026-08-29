namespace Sushi81.Pos.OneDriveFeasibility;

public enum ArtifactSyncStatus
{
    ConfirmedInSync,
    Pending,
    Partial,
    Invalid,
    Unknown,
    Error
}

public sealed record ArtifactSyncObservation(
    string Path,
    ArtifactSyncStatus Status,
    string? Error = null)
{
    public bool IsConfirmedInSync => Status == ArtifactSyncStatus.ConfirmedInSync;
}

/// <summary>
/// Narrow observation-only boundary used by directed handoff publication. It
/// deliberately exposes a result, rather than treating a delay or file
/// existence as synchronization evidence.
/// </summary>
public interface IArtifactSyncObserver
{
    ArtifactSyncObservation Observe(string path);
}

/// <summary>Adapts the already-verified Cloud Files reader to the directed proof.</summary>
public sealed class CloudFileArtifactSyncObserver(ICloudFileStateReader reader) : IArtifactSyncObserver
{
    private readonly ICloudFileStateReader reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public ArtifactSyncObservation Observe(string path)
    {
        var observation = reader.Observe(path);
        var status = observation.State switch
        {
            CloudFilePublicationState.InSync => ArtifactSyncStatus.ConfirmedInSync,
            CloudFilePublicationState.Pending => ArtifactSyncStatus.Pending,
            CloudFilePublicationState.Partial => ArtifactSyncStatus.Partial,
            CloudFilePublicationState.Invalid => ArtifactSyncStatus.Invalid,
            CloudFilePublicationState.Missing => ArtifactSyncStatus.Error,
            _ => ArtifactSyncStatus.Unknown
        };
        return new(observation.Path, status, observation.Error);
    }
}

/// <summary>Deterministic fake used by synthetic tests; no clock or sleep implies success.</summary>
public sealed class DeterministicArtifactSyncObserver : IArtifactSyncObserver
{
    private readonly Dictionary<string, ArtifactSyncObservation> observations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ArtifactSyncStatus defaultStatus;

    public DeterministicArtifactSyncObserver(ArtifactSyncStatus defaultStatus = ArtifactSyncStatus.ConfirmedInSync)
    {
        this.defaultStatus = defaultStatus;
    }

    public void Set(string path, ArtifactSyncStatus status, string? error = null) =>
        observations[Path.GetFullPath(path)] = new(Path.GetFullPath(path), status, error);

    public ArtifactSyncObservation Observe(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return observations.TryGetValue(fullPath, out var observation)
            ? observation
            : new(fullPath, defaultStatus);
    }
}
