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

    private sealed class FixedClock(DateTimeOffset utcNow, TimeZoneInfo timeZone) : IBusinessClock
    {
        public DateTimeOffset UtcNow => utcNow;

        public DateOnly BusinessDate => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, timeZone).DateTime);

        public TimeZoneInfo BusinessTimeZone => timeZone;
    }
}
