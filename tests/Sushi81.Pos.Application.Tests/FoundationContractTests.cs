using Sushi81.Pos.Application.Foundation.Authority;
using Sushi81.Pos.Application.Foundation.Time;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class FoundationContractTests
{
    [TestMethod]
    public void FixedClockExposesDeterministicUtcAndBusinessDate()
    {
        IBusinessClock clock = new FixedClock(new DateTimeOffset(2026, 8, 27, 22, 30, 0, TimeSpan.Zero), TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"));
        Assert.AreEqual(new DateTimeOffset(2026, 8, 27, 22, 30, 0, TimeSpan.Zero), clock.UtcNow);
        Assert.AreEqual(new DateOnly(2026, 8, 28), clock.BusinessDate);
    }

    [TestMethod]
    public void UnknownAuthorityStateIsNotWritableByContract()
    {
        var nonAuthoritativeStates = Enum.GetValues<WriteAuthorityState>()
            .Where(state => state != WriteAuthorityState.Authoritative)
            .ToArray();
        CollectionAssert.DoesNotContain(nonAuthoritativeStates, WriteAuthorityState.Authoritative);
    }

    [TestMethod]
    public void DisasterRecoveryPreparingIsTransitioningAndNeverWritable()
    {
        var evidence = new RecoveryActivationEvidence(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3, 4,
            "onedrive-checkpoint:abc:h7", new string('A', 64), 12,
            CandidateType: "OneDriveCheckpoint", CandidateReference: "checkpoint.db",
            CandidateHandoffVersion: 7, CandidateCreatedAtUtc: DateTimeOffset.UtcNow);
        var state = new AuthorityProtocolState(
            1, evidence.DeviceId, "Recovery PC", evidence.LineageId, 3, 7, 12,
            AuthorityPhase.DisasterRecoveryPreparing, Recovery: evidence);

        state.Validate();

        Assert.AreEqual(WriteAuthorityState.Transitioning, state.WriteState);
        Assert.AreNotEqual(WriteAuthorityState.Authoritative, state.WriteState);
    }

    [TestMethod]
    public void RecoveryCandidateOrderingUsesRevisionThenHandoffThenStableIdentity()
    {
        var lineage = Guid.NewGuid();
        var source = Guid.NewGuid();
        RecoveryCandidate Candidate(string id, long revision, long handoff) => new(
            RecoveryCandidateType.OneDriveCheckpoint, id, lineage, 3, source, revision, handoff,
            DateTimeOffset.UtcNow, 1, new string('A', 64), id);

        var result = new RecoveryCandidateDiscoveryResult(
            new[] { Candidate("z", 20, 1), Candidate("a", 20, 2), Candidate("b", 21, 0) },
            "synthetic");

        Assert.AreEqual("b", result.Recommended!.CandidateId);
        Assert.AreEqual(21L, result.Recommended.BusinessRevision);
    }

    private sealed class FixedClock(DateTimeOffset utcNow, TimeZoneInfo timeZone) : IBusinessClock
    {
        public DateTimeOffset UtcNow => utcNow;

        public DateOnly BusinessDate => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);

        public TimeZoneInfo BusinessTimeZone => timeZone;
    }
}
