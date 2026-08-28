using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Domain.Tests;

[TestClass]
public sealed class MoneyTests
{
    [TestMethod]
    public void FromEurosUsesExactSignedCents()
    {
        Assert.AreEqual(1L, Money.FromEuros(0.01m).Cents);
        Assert.AreEqual(1850L, Money.FromEuros(18.50m).Cents);
        Assert.AreEqual(0L, Money.FromEuros(0m).Cents);
        Assert.AreEqual(-250L, Money.FromEuros(-2.50m).Cents);
        Assert.AreEqual(18.50m, Money.FromCents(1850).Euros);
    }

    [TestMethod]
    public void FromEurosRoundsSignedMidpointsAwayFromZero()
    {
        Assert.AreEqual(1364L, Money.FromEuros(13.635m).Cents);
        Assert.AreEqual(-1364L, Money.FromEuros(-13.635m).Cents);
    }

    [TestMethod]
    public void ValuesAreComparableAndSupportCheckedArithmetic()
    {
        var amount = Money.FromCents(100);
        Assert.IsTrue(amount > Money.Zero);
        Assert.IsTrue(Money.FromCents(-1) < Money.Zero);
        Assert.AreEqual(Money.FromCents(300), amount + Money.FromCents(200));
        Assert.AreEqual(Money.FromCents(-100), -amount);
        Assert.Throws<OverflowException>(() => _ = Money.FromCents(long.MaxValue) + Money.FromCents(1));
        Assert.Throws<OverflowException>(() => _ = Money.FromCents(long.MaxValue) * 2);
    }
}
