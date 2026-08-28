namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

/// <summary>
/// Deterministic, in-memory transport. Publication is global, while visibility
/// is explicitly delivered per device. This models delayed and reordered
/// synchronization without sleeps or timing assumptions.
/// </summary>
public sealed class DeterministicSyncTransport
{
    private readonly Dictionary<string, DeviceState> devices;
    private readonly Dictionary<string, AcquisitionClaim> publishedClaims = new(StringComparer.Ordinal);

    public DeterministicSyncTransport(IEnumerable<string> deviceIds)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        var ids = deviceIds.ToArray();
        if (ids.Length == 0 || ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
        {
            throw new ArgumentException("At least one distinct non-empty device ID is required.", nameof(deviceIds));
        }

        devices = ids.ToDictionary(
            id => id,
            id => new DeviceState(SimulatedSyncState.InSync),
            StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> DeviceIds => devices.Keys.ToArray();

    public IReadOnlyCollection<AcquisitionClaim> PublishedClaims => publishedClaims.Values.ToArray();

    public void SetSyncState(string deviceId, SimulatedSyncState state) => GetDevice(deviceId).SyncState = state;

    public void SetParticipantPresent(string deviceId, bool present) => GetDevice(deviceId).ParticipantPresent = present;

    public void PublishClaim(AcquisitionClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (!claim.IsValid || !devices.ContainsKey(claim.DeviceId))
        {
            throw new ArgumentException("Claims must have valid metadata and originate from a known device.", nameof(claim));
        }

        if (publishedClaims.TryGetValue(claim.ClaimId, out var existing) && existing != claim)
        {
            throw new InvalidOperationException($"Claim ID replayed with different content: {claim.ClaimId}");
        }

        publishedClaims[claim.ClaimId] = claim;
    }

    /// <summary>Deliver one published claim to one device, in any chosen order.</summary>
    public void DeliverClaim(string deviceId, string claimId)
    {
        var device = GetDevice(deviceId);
        if (!publishedClaims.TryGetValue(claimId, out var claim))
        {
            throw new KeyNotFoundException($"Unknown claim: {claimId}");
        }

        device.VisibleClaims[claimId] = claim;
    }

    public void DeliverAll(string deviceId)
    {
        foreach (var claim in publishedClaims.Values.OrderBy(x => x.ClaimId, StringComparer.Ordinal))
        {
            DeliverClaim(deviceId, claim.ClaimId);
        }
    }

    /// <summary>Replay is observable as a duplicate event but remains set-like to evaluators.</summary>
    public void ReplayClaim(string deviceId, string claimId) => DeliverClaim(deviceId, claimId);

    public DeviceView Observe(string deviceId)
    {
        var device = GetDevice(deviceId);
        return new DeviceView(
            deviceId,
            device.SyncState,
            device.ParticipantPresent,
            device.VisibleClaims.Values.OrderBy(x => x.ClaimId, StringComparer.Ordinal).ToArray());
    }

    private DeviceState GetDevice(string deviceId) => devices.TryGetValue(deviceId, out var state)
        ? state
        : throw new KeyNotFoundException($"Unknown device: {deviceId}");

    private sealed class DeviceState(SimulatedSyncState syncState)
    {
        public SimulatedSyncState SyncState { get; set; } = syncState;
        public bool ParticipantPresent { get; set; } = true;
        public Dictionary<string, AcquisitionClaim> VisibleClaims { get; } = new(StringComparer.Ordinal);
    }
}

public sealed record DeviceView(
    string DeviceId,
    SimulatedSyncState SyncState,
    bool ParticipantPresent,
    IReadOnlyList<AcquisitionClaim> VisibleClaims);
