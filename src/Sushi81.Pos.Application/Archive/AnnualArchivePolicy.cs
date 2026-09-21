using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.Archive;

public enum AnnualArchiveEndKind
{
    Closed,
    Cancelled
}

public enum AnnualArchiveEligibilityReason
{
    Eligible,
    NoTargetYear,
    OpenOrder,
    EndTimestampMissing,
    EndYearDoesNotMatchTarget
}

/// <summary>
/// The persisted order facts required to decide annual-archive eligibility.
/// CreatedAtUtc is deliberately present for callers but is not an eligibility
/// boundary: the business-local terminal timestamp is authoritative.
/// </summary>
public sealed record AnnualArchiveOrderCandidate(
    Guid OrderId,
    OrderSourceType SourceType,
    OrderStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    DateTimeOffset? CancelledAtUtc);

public sealed record AnnualArchiveEligibility(
    Guid OrderId,
    OrderSourceType SourceType,
    bool IsEligible,
    int? TerminalBusinessYear,
    AnnualArchiveEndKind? EndKind,
    AnnualArchiveEligibilityReason Reason);

/// <summary>
/// Pure annual-archive timing and order eligibility rules. This boundary has
/// no UI, storage, scheduler, export, or authority dependencies.
/// </summary>
public static class AnnualArchivePolicy
{
    /// <summary>
    /// Returns the previous complete calendar year from February onward. January
    /// deliberately has no target because the preceding year is not yet outside
    /// the January safety window.
    /// </summary>
    public static int? GetTargetArchiveYear(DateOnly businessDate) =>
        businessDate.Month == 1 ? null : businessDate.Year - 1;

    /// <summary>
    /// Evaluates an order against a target archive year using the business-local
    /// year of its terminal UTC timestamp. Open orders are never eligible.
    /// </summary>
    public static AnnualArchiveEligibility Evaluate(
        AnnualArchiveOrderCandidate candidate,
        int? targetYear,
        TimeZoneInfo businessTimeZone)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(businessTimeZone);

        if (candidate.Status == OrderStatus.Open)
            return Ineligible(candidate, null, null, AnnualArchiveEligibilityReason.OpenOrder);

        if (targetYear is null)
            return Ineligible(candidate, null, null, AnnualArchiveEligibilityReason.NoTargetYear);

        return candidate.Status switch
        {
            OrderStatus.Closed => EvaluateTerminal(candidate, targetYear.Value, businessTimeZone, AnnualArchiveEndKind.Closed, candidate.ClosedAtUtc),
            OrderStatus.Cancelled => EvaluateTerminal(candidate, targetYear.Value, businessTimeZone, AnnualArchiveEndKind.Cancelled, candidate.CancelledAtUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(candidate), candidate.Status, "The order status is not supported by the annual archive policy.")
        };
    }

    public static int? GetEligibleArchiveYear(
        OrderStatus status,
        DateTimeOffset? closedAtUtc,
        DateTimeOffset? cancelledAtUtc,
        TimeZoneInfo businessTimeZone)
    {
        ArgumentNullException.ThrowIfNull(businessTimeZone);
        var terminalAtUtc = status switch
        {
            OrderStatus.Closed => closedAtUtc,
            OrderStatus.Cancelled => cancelledAtUtc,
            _ => null
        };

        return terminalAtUtc is { } timestamp
            ? TimeZoneInfo.ConvertTime(timestamp, businessTimeZone).Year
            : null;
    }

    public static bool IsEligibleForTargetYear(OrderSnapshot order, int targetYear, TimeZoneInfo businessTimeZone)
    {
        ArgumentNullException.ThrowIfNull(order);
        return Evaluate(
            new AnnualArchiveOrderCandidate(order.Id, order.SourceType, order.Status, order.CreatedAt, order.ClosedAt, order.CancelledAt),
            targetYear,
            businessTimeZone).IsEligible;
    }

    private static AnnualArchiveEligibility EvaluateTerminal(
        AnnualArchiveOrderCandidate candidate,
        int targetYear,
        TimeZoneInfo businessTimeZone,
        AnnualArchiveEndKind endKind,
        DateTimeOffset? terminalAtUtc)
    {
        if (terminalAtUtc is not { } timestamp)
            return Ineligible(candidate, null, endKind, AnnualArchiveEligibilityReason.EndTimestampMissing);

        var terminalBusinessYear = TimeZoneInfo.ConvertTime(timestamp, businessTimeZone).Year;
        return new(
            candidate.OrderId,
            candidate.SourceType,
            terminalBusinessYear == targetYear,
            terminalBusinessYear,
            endKind,
            terminalBusinessYear == targetYear
                ? AnnualArchiveEligibilityReason.Eligible
                : AnnualArchiveEligibilityReason.EndYearDoesNotMatchTarget);
    }

    private static AnnualArchiveEligibility Ineligible(
        AnnualArchiveOrderCandidate candidate,
        int? terminalBusinessYear,
        AnnualArchiveEndKind? endKind,
        AnnualArchiveEligibilityReason reason) =>
        new(candidate.OrderId, candidate.SourceType, false, terminalBusinessYear, endKind, reason);
}
