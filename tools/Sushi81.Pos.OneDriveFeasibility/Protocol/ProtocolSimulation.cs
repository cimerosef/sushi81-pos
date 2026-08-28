namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

public sealed record ProtocolAuditResult(
    string Scenario,
    int DeviceCount,
    bool ClaimOnlyDoubleWriterObserved,
    bool FailClosedDoubleWriterObserved,
    IReadOnlyList<AcquisitionDecision> FailClosedDecisions,
    string Finding);

public sealed record InterleavingAuditResult(
    int DeviceCount,
    int ScheduleCount,
    bool ClaimOnlyDoubleWriterObserved,
    bool FailClosedDoubleWriterObserved,
    bool FailClosedWritableObserved,
    IReadOnlyList<string> ClaimOnlyWitness,
    string Finding);

public static class ProtocolSimulation
{
    /// <summary>
    /// Two claims are published before either claimant sees the other one. This
    /// is the minimum delayed-visibility schedule that defeats a file-only lock.
    /// The same setup is evaluated with three devices to keep the model N-device.
    /// </summary>
    public static ProtocolAuditResult RunContentionAudit()
    {
        var devices = new[] { "device-a", "device-b", "device-c" };
        var handoff = new HandoffIdentity("synthetic-lineage", 7, 11);
        var transport = new DeterministicSyncTransport(devices);
        var claims = devices.Select(id => new AcquisitionClaim($"claim-{id}", handoff, id)).ToArray();
        foreach (var claim in claims)
        {
            transport.PublishClaim(claim);
        }

        // Deliberately reordered and incomplete visibility: A sees A, B sees B,
        // C sees C. Duplicate/replay is exercised without changing set semantics.
        foreach (var device in devices)
        {
            transport.DeliverClaim(device, $"claim-{device}");
        }

        transport.ReplayClaim("device-a", "claim-device-a");
        var unsafeDecisions = devices.Select(device => ClaimOnlyAcquisitionProtocol.Evaluate(transport.Observe(device), handoff)).ToArray();

        var safeDecisions = devices.Select(device => FailClosedAcquisitionProtocol.Evaluate(transport.Observe(device), devices, handoff)).ToArray();
        var unsafeWritable = unsafeDecisions.Count(x => x.IsWritable);
        var safeWritable = safeDecisions.Count(x => x.IsWritable);

        return new(
            "three-device delayed/reordered simultaneous acquisition",
            devices.Length,
            unsafeWritable > 1,
            safeWritable > 1,
            safeDecisions,
            unsafeWritable > 1 && safeWritable == 0
                ? "file-only claim visibility permits a double writer; fail-closed protocol blocks unresolved N-device contention"
                : "unexpected protocol result");
    }

    /// <summary>
    /// Enumerates every interleaving of per-device PublishClaim then DeliverClaim
    /// for the supplied devices. The evaluator is run after every operation, not
    /// only at the final state. This makes the race a deterministic safety proof
    /// exercise rather than a timing test.
    /// </summary>
    public static InterleavingAuditResult RunAdversarialInterleavingAudit(IReadOnlyList<string> deviceIds)
    {
        ArgumentNullException.ThrowIfNull(deviceIds);
        if (deviceIds.Count < 2 || deviceIds.Any(string.IsNullOrWhiteSpace) ||
            deviceIds.Distinct(StringComparer.Ordinal).Count() != deviceIds.Count)
        {
            throw new ArgumentException("At least two distinct non-empty device IDs are required.", nameof(deviceIds));
        }

        var handoff = new HandoffIdentity("synthetic-lineage", 7, 11);
        var claims = deviceIds.ToDictionary(
            id => id,
            id => new AcquisitionClaim($"claim-{id}", handoff, id),
            StringComparer.Ordinal);
        var schedules = 0;
        var claimOnlyDoubleWriter = false;
        var failClosedDoubleWriter = false;
        var failClosedWritable = false;
        string[] witness = [];

        void Explore(DeterministicSyncTransport transport, HashSet<string> published, HashSet<string> delivered, List<string> schedule)
        {
            var claimOnlyDecisions = deviceIds
                .Select(id => ClaimOnlyAcquisitionProtocol.Evaluate(transport.Observe(id), handoff))
                .ToArray();
            var failClosedDecisions = deviceIds
                .Select(id => FailClosedAcquisitionProtocol.Evaluate(transport.Observe(id), deviceIds, handoff))
                .ToArray();

            if (claimOnlyDecisions.Count(x => x.IsWritable) > 1)
            {
                claimOnlyDoubleWriter = true;
                if (witness.Length == 0)
                {
                    witness = schedule.ToArray();
                }
            }

            if (failClosedDecisions.Count(x => x.IsWritable) > 1)
            {
                failClosedDoubleWriter = true;
                if (witness.Length == 0)
                {
                    witness = schedule.ToArray();
                }
            }

            failClosedWritable |= failClosedDecisions.Any(x => x.IsWritable);
            if (published.Count == deviceIds.Count && delivered.Count == deviceIds.Count)
            {
                schedules++;
                return;
            }

            foreach (var id in deviceIds)
            {
                if (!published.Contains(id))
                {
                    var nextTransport = CloneTransport(transport, deviceIds);
                    nextTransport.PublishClaim(claims[id]);
                    var nextPublished = new HashSet<string>(published, StringComparer.Ordinal) { id };
                    var nextSchedule = new List<string>(schedule) { $"publish:{id}" };
                    Explore(nextTransport, nextPublished, delivered, nextSchedule);
                }
                else if (!delivered.Contains(id))
                {
                    var nextTransport = CloneTransport(transport, deviceIds);
                    nextTransport.DeliverClaim(id, claims[id].ClaimId);
                    var nextDelivered = new HashSet<string>(delivered, StringComparer.Ordinal) { id };
                    var nextSchedule = new List<string>(schedule) { $"deliver:{id}" };
                    Explore(nextTransport, published, nextDelivered, nextSchedule);
                }
            }
        }

        Explore(new DeterministicSyncTransport(deviceIds), [], [], []);
        return new(
            deviceIds.Count,
            schedules,
            claimOnlyDoubleWriter,
            failClosedDoubleWriter,
            failClosedWritable,
            witness,
            claimOnlyDoubleWriter && !failClosedDoubleWriter && !failClosedWritable
                ? "adversarial file-only interleavings produce a double writer; fail-closed protocol preserves safety by refusing N-device writable activation"
                : "unexpected protocol result");
    }

    private static DeterministicSyncTransport CloneTransport(DeterministicSyncTransport source, IReadOnlyList<string> deviceIds)
    {
        var clone = new DeterministicSyncTransport(deviceIds);
        foreach (var claim in source.PublishedClaims)
        {
            clone.PublishClaim(claim);
        }

        foreach (var id in deviceIds)
        {
            var view = source.Observe(id);
            clone.SetSyncState(id, view.SyncState);
            clone.SetParticipantPresent(id, view.ParticipantPresent);
            foreach (var claim in view.VisibleClaims)
            {
                clone.DeliverClaim(id, claim.ClaimId);
            }
        }

        return clone;
    }
}
