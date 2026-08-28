namespace Sushi81.Pos.OneDriveFeasibility.Protocol;

public sealed record ProtocolAuditResult(
    string Scenario,
    int DeviceCount,
    bool ClaimOnlyDoubleWriterObserved,
    bool FailClosedDoubleWriterObserved,
    IReadOnlyList<AcquisitionDecision> FailClosedDecisions,
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
}
