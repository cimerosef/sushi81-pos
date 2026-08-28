using Sushi81.Pos.Application.Foundation.Time;

namespace Sushi81.Pos.Infrastructure.Time;

public sealed class TimeProviderBusinessClock(TimeProvider timeProvider, TimeZoneInfo businessTimeZone) : IBusinessClock
{
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public DateOnly BusinessDate => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, businessTimeZone).DateTime);

    public TimeZoneInfo BusinessTimeZone { get; } = businessTimeZone ?? throw new ArgumentNullException(nameof(businessTimeZone));
}
