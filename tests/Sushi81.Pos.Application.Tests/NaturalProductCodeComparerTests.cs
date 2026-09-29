using Sushi81.Pos.Application.Catalogue;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class NaturalProductCodeComparerTests
{
    [TestMethod]
    public void RequiredCodeFamiliesUseCaseInsensitiveNaturalOrder()
    {
        var codes = new[] { "R11", "R10", "R2", "R9", "R1", "ML10", "ML2", "ML9", "ML1", "R5", "R4c", "R4a", "R4", "R4b" };

        Array.Sort(codes, NaturalProductCodeComparer.Instance);

        var expected = new[] { "ML1", "ML2", "ML9", "ML10", "R1", "R2", "R4", "R4a", "R4b", "R4c", "R5", "R9", "R10", "R11" };
        CollectionAssert.AreEqual(expected, codes);
        Assert.IsLessThan(0, NaturalProductCodeComparer.Instance.Compare("r2", "R10"));
        Assert.IsLessThan(0, NaturalProductCodeComparer.Instance.Compare("a2B3", "A2b10"));
    }

    [TestMethod]
    public void EquivalentNumericValuesHaveDocumentedRawCodeTieOrder()
    {
        var codes = new[] { "r1", "R1", "R001", "R01" };

        Array.Sort(codes, NaturalProductCodeComparer.Instance);

        // Equal natural values fall back to ordinal-ignore-case raw code, then ordinal raw code.
        var expected = new[] { "R001", "R01", "R1", "r1" };
        CollectionAssert.AreEqual(expected, codes);
        Assert.AreEqual(0, NaturalProductCodeComparer.Instance.Compare("R1", "R1"));
        Assert.IsGreaterThan(0, NaturalProductCodeComparer.Instance.Compare("R000", "R0"));
    }

    [TestMethod]
    public void ArbitrarilyLongDigitRunsCompareByMagnitudeWithoutParsing()
    {
        var comparer = NaturalProductCodeComparer.Instance;

        Assert.IsLessThan(0, comparer.Compare("R9223372036854775808", "R9223372036854775809"));
        Assert.IsLessThan(0, comparer.Compare("R999999999999999999999999999999", "R1000000000000000000000000000000"));
        Assert.IsGreaterThan(0, comparer.Compare("R0000000000000000000000000000002", "R1"));
    }
}
