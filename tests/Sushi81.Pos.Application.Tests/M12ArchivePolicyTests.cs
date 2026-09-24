using Sushi81.Pos.Application.Archive;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class M12ArchivePolicyTests
{
    private static readonly TimeZoneInfo BusinessPlusTwo = TimeZoneInfo.CreateCustomTimeZone(
        "M12-Business-Plus-02",
        TimeSpan.FromHours(2),
        "M12 business time",
        "M12 business time");

    private static readonly TimeZoneInfo BusinessMinusOne = TimeZoneInfo.CreateCustomTimeZone(
        "M12-Business-Minus-01",
        TimeSpan.FromHours(-1),
        "M12 business time",
        "M12 business time");

    [TestMethod]
    public void JanuaryHasNoTargetAndFebruaryTargetsPreviousYear()
    {
        Assert.IsNull(AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2026, 1, 31)));
        Assert.AreEqual(2025, AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2026, 2, 1)));
    }

    [TestMethod]
    public void DelayedMarchOrDecemberStartStillTargetsOnlyPreviousYear()
    {
        Assert.AreEqual(2025, AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2026, 3, 17)));
        Assert.AreEqual(2025, AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2026, 12, 31)));
    }

    [TestMethod]
    public void JanuaryTargetAbsenceMakesAClosedOrderIneligible()
    {
        var result = AnnualArchivePolicy.Evaluate(
            Candidate(OrderStatus.Closed, closedAtUtc: Utc(2025, 12, 31, 12)),
            AnnualArchivePolicy.GetTargetArchiveYear(new DateOnly(2026, 1, 31)),
            TimeZoneInfo.Utc);

        Assert.IsFalse(result.IsEligible);
        Assert.AreEqual(AnnualArchiveEligibilityReason.NoTargetYear, result.Reason);
    }

    [TestMethod]
    public void ClosedUsesBusinessLocalYearAtUtcNewYearBoundary()
    {
        var crossesIntoNextLocalYear = AnnualArchivePolicy.Evaluate(
            Candidate(OrderStatus.Closed, closedAtUtc: Utc(2025, 12, 31, 22, 30)),
            2025,
            BusinessPlusTwo);
        var remainsInPreviousLocalYear = AnnualArchivePolicy.Evaluate(
            Candidate(OrderStatus.Closed, closedAtUtc: Utc(2026, 1, 1, 00, 30)),
            2025,
            BusinessMinusOne);

        Assert.IsFalse(crossesIntoNextLocalYear.IsEligible);
        Assert.AreEqual(2026, crossesIntoNextLocalYear.TerminalBusinessYear);
        Assert.AreEqual(AnnualArchiveEligibilityReason.EndYearDoesNotMatchTarget, crossesIntoNextLocalYear.Reason);
        Assert.IsTrue(remainsInPreviousLocalYear.IsEligible);
        Assert.AreEqual(2025, remainsInPreviousLocalYear.TerminalBusinessYear);
    }

    [TestMethod]
    public void CancelledUsesBusinessLocalYearAtUtcNewYearBoundary()
    {
        var result = AnnualArchivePolicy.Evaluate(
            Candidate(OrderStatus.Cancelled, cancelledAtUtc: Utc(2026, 1, 1, 00, 30)),
            2025,
            BusinessMinusOne);

        Assert.IsTrue(result.IsEligible);
        Assert.AreEqual(AnnualArchiveEndKind.Cancelled, result.EndKind);
        Assert.AreEqual(2025, result.TerminalBusinessYear);
    }

    [TestMethod]
    public void OpenOrderIsNeverEligibleEvenWhenOldAndGivenAClosedTimestamp()
    {
        var result = AnnualArchivePolicy.Evaluate(
            Candidate(
                OrderStatus.Open,
                createdAtUtc: Utc(2020, 1, 1, 12),
                closedAtUtc: Utc(2025, 12, 31, 12)),
            2025,
            TimeZoneInfo.Utc);

        Assert.IsFalse(result.IsEligible);
        Assert.AreEqual(AnnualArchiveEligibilityReason.OpenOrder, result.Reason);
        Assert.IsNull(result.TerminalBusinessYear);
        Assert.IsNull(result.EndKind);
    }

    [TestMethod]
    public void CreatedYearDoesNotOverrideTerminalBusinessYear()
    {
        var result = AnnualArchivePolicy.Evaluate(
            Candidate(
                OrderStatus.Closed,
                createdAtUtc: Utc(2026, 1, 1, 12),
                closedAtUtc: Utc(2025, 12, 31, 12)),
            2025,
            TimeZoneInfo.Utc);

        Assert.IsTrue(result.IsEligible);
        Assert.AreEqual(2025, result.TerminalBusinessYear);
    }

    [TestMethod]
    public void PosAndHiboutikPasteHaveIdenticalEligibilityRules()
    {
        var pos = AnnualArchivePolicy.Evaluate(
            Candidate(OrderStatus.Closed, OrderSourceType.Pos, closedAtUtc: Utc(2025, 8, 15, 12)),
            2025,
            TimeZoneInfo.Utc);
        var hiboutik = AnnualArchivePolicy.Evaluate(
            Candidate(OrderStatus.Closed, OrderSourceType.HiboutikPaste, closedAtUtc: Utc(2025, 8, 15, 12)),
            2025,
            TimeZoneInfo.Utc);

        Assert.AreEqual(pos.IsEligible, hiboutik.IsEligible);
        Assert.AreEqual(pos.TerminalBusinessYear, hiboutik.TerminalBusinessYear);
        Assert.AreEqual(pos.EndKind, hiboutik.EndKind);
        Assert.AreEqual(pos.Reason, hiboutik.Reason);
    }

    [TestMethod]
    public void MissingClosedOrCancelledTimestampFailsClosed()
    {
        var closed = AnnualArchivePolicy.Evaluate(Candidate(OrderStatus.Closed), 2025, TimeZoneInfo.Utc);
        var cancelled = AnnualArchivePolicy.Evaluate(Candidate(OrderStatus.Cancelled), 2025, TimeZoneInfo.Utc);

        Assert.IsFalse(closed.IsEligible);
        Assert.AreEqual(AnnualArchiveEligibilityReason.EndTimestampMissing, closed.Reason);
        Assert.IsFalse(cancelled.IsEligible);
        Assert.AreEqual(AnnualArchiveEligibilityReason.EndTimestampMissing, cancelled.Reason);
    }

    private static AnnualArchiveOrderCandidate Candidate(
        OrderStatus status,
        OrderSourceType sourceType = OrderSourceType.Pos,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? closedAtUtc = null,
        DateTimeOffset? cancelledAtUtc = null) =>
        new(
            Guid.Parse("12000000-0000-0000-0000-000000000001"),
            sourceType,
            status,
            createdAtUtc ?? Utc(2024, 6, 1, 12),
            closedAtUtc,
            cancelledAtUtc);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
