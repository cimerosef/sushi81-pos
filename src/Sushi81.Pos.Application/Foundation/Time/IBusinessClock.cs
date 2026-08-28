namespace Sushi81.Pos.Application.Foundation.Time;

/// <summary>Clock seam for deterministic technical timestamps and local business dates.</summary>
public interface IBusinessClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly BusinessDate { get; }

    TimeZoneInfo BusinessTimeZone { get; }
}
