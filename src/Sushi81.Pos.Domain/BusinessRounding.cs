namespace Sushi81.Pos.Domain;

/// <summary>Single approved round-half-up rule for business monetary values.</summary>
public static class BusinessRounding
{
    public static decimal ToCentsDecimal(decimal euros) => Math.Round(euros * 100m, 0, MidpointRounding.AwayFromZero);

    public static long ToCents(decimal euros) => checked((long)ToCentsDecimal(euros));

    public static decimal ToEuros(long cents) => cents / 100m;
}
