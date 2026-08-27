namespace Sushi81.Pos.Domain;

/// <summary>Immutable business money represented as signed euro cents.</summary>
public readonly record struct Money(long Cents) : IComparable<Money>
{
    public static Money Zero => new(0);

    public decimal Euros => Cents / 100m;

    public static Money FromEuros(decimal euros) => new(BusinessRounding.ToCents(euros));

    public static Money FromCents(long cents) => new(cents);

    public static Money operator +(Money left, Money right) => new(checked(left.Cents + right.Cents));

    public static Money operator -(Money left, Money right) => new(checked(left.Cents - right.Cents));

    public static Money operator -(Money value) => new(checked(-value.Cents));

    public static Money operator *(Money value, long multiplier) => new(checked(value.Cents * multiplier));

    public static bool operator <(Money left, Money right) => left.Cents < right.Cents;

    public static bool operator <=(Money left, Money right) => left.Cents <= right.Cents;

    public static bool operator >(Money left, Money right) => left.Cents > right.Cents;

    public static bool operator >=(Money left, Money right) => left.Cents >= right.Cents;

    public int CompareTo(Money other) => Cents.CompareTo(other.Cents);
}
