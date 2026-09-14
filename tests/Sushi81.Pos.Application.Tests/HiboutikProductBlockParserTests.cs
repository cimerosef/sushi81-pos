using Sushi81.Pos.Application.OrderEntry;

namespace Sushi81.Pos.Application.Tests;

[TestClass]
public sealed class HiboutikProductBlockParserTests
{
    private static readonly string[] ExpectedCandidateCodes = ["AA1", "BB2", "CC3"];
    private static readonly int?[] ExpectedCandidateQuantities = [1, 2, 3];

    [TestMethod]
    public void CommittedSyntheticFixtureCapturesFinalTotalAndIgnoresDelivery()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "samples", "pasted-orders", "hiboutik-product-block-synthetic.txt");
        var result = HiboutikProductBlockParser.Parse(File.ReadAllText(fixturePath));

        Assert.HasCount(3, result.ProductCandidates);
        Assert.AreEqual(2430, result.SourceTotalTtc!.Value.Cents);
        Assert.AreEqual(6, result.Lines.Count(line => line.Kind == HiboutikPasteLineKind.KnownIgnoredLine));
        Assert.IsTrue(result.Lines.Any(line => line.IgnoredReason == HiboutikPasteIgnoredReason.DeliveryServiceLine));
        Assert.IsFalse(result.HasUnresolvedLines);
    }

    [TestMethod]
    public void LfAndCrLfHaveEquivalentResults()
    {
        const string lf = "1 x AA1 Produit (5.50)\nTotal : 5.5\nTOTAL 5.5";

        var lfResult = HiboutikProductBlockParser.Parse(lf);
        var crlfResult = HiboutikProductBlockParser.Parse(lf.Replace("\n", "\r\n", StringComparison.Ordinal));

        CollectionAssert.AreEqual(lfResult.Lines.ToArray(), crlfResult.Lines.ToArray());
        Assert.AreEqual(lfResult.SourceTotalTtc, crlfResult.SourceTotalTtc);
    }

    [TestMethod]
    public void NbspAndRepeatedSpacingAreHarmless()
    {
        var result = HiboutikProductBlockParser.Parse("  1\u00a0 x    AA1   Produit   (5.50)  \r\n Total\u00a0:\u00a05.5 ");

        Assert.AreEqual(HiboutikPasteLineKind.ProductCandidate, result.Lines[0].Kind);
        Assert.AreEqual(1, result.Lines[0].Quantity);
        Assert.AreEqual("AA1", result.Lines[0].CandidateCode);
        Assert.AreEqual(550, result.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public void MultipleCandidatesPreserveOrderQuantityAndCode()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Alpha\n2 x BB2 Beta\n3 x CC3 Gamma");

        CollectionAssert.AreEqual(ExpectedCandidateCodes, result.ProductCandidates.Select(line => line.CandidateCode).ToArray());
        CollectionAssert.AreEqual(ExpectedCandidateQuantities, result.ProductCandidates.Select(line => line.Quantity).ToArray());
    }

    [TestMethod]
    public void QuantityGreaterThanOneRemainsAProductCandidate()
    {
        var result = HiboutikProductBlockParser.Parse("12 x AA1 Produit");

        Assert.AreEqual(HiboutikPasteLineKind.ProductCandidate, result.Lines.Single().Kind);
        Assert.AreEqual(12, result.Lines.Single().Quantity);
    }

    [TestMethod]
    public void RepeatedCodesRemainSeparateCandidates()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 First\n2 x AA1 Second");

        Assert.HasCount(2, result.ProductCandidates);
        Assert.AreEqual(1, result.ProductCandidates[0].Quantity);
        Assert.AreEqual(2, result.ProductCandidates[1].Quantity);
    }

    [TestMethod]
    public void PerItemTotalsAreKnownIgnoredLinesAndNeverCartProducts()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTotal : 5.50\n2 x BB2 Produit\nTOTAL 10.50");

        Assert.HasCount(2, result.ProductCandidates);
        Assert.AreEqual(1, result.Lines.Count(line => line.IgnoredReason == HiboutikPasteIgnoredReason.PerItemTotal));
        Assert.IsTrue(result.Lines.Where(line => line.IgnoredReason == HiboutikPasteIgnoredReason.PerItemTotal)
            .All(line => line.SourceAmountTtc.HasValue));
    }

    [TestMethod]
    public void FinalTotalIsKnownIgnoredAndCaptured()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTOTAL 24.30");

        Assert.AreEqual(HiboutikPasteLineKind.KnownIgnoredLine, result.Lines[1].Kind);
        Assert.AreEqual(HiboutikPasteIgnoredReason.FinalTotal, result.Lines[1].IgnoredReason);
        Assert.AreEqual(2430, result.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public void CompletePerItemTotalsWithoutFinalTotalAreSummedInCents()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTotal : 5.5\n2 x BB2 Produit\nTotal : 10");

        Assert.AreEqual(1550, result.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public void MissingPerItemTotalWithoutFinalTotalIsNotReliable()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTotal : 5.5\n2 x BB2 Produit");

        Assert.IsNull(result.SourceTotalTtc);
    }

    [TestMethod]
    public void StrayExtraPerItemTotalMakesDerivedTotalNull()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTotal : 5.5\nTotal : 5.5");

        Assert.IsNull(result.SourceTotalTtc);
    }

    [TestMethod]
    public void ParseableFinalTotalTakesPrecedenceOverPerItemValues()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTotal : 1\nTOTAL 24.30");

        Assert.AreEqual(2430, result.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public void AmbiguousFinalTotalsAreNotReliable()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTOTAL 5\nTOTAL 6");

        Assert.IsNull(result.SourceTotalTtc);
        Assert.AreEqual(2, result.Lines.Count(line => line.IgnoredReason == HiboutikPasteIgnoredReason.FinalTotal));
    }

    [TestMethod]
    public void MalformedTotalLikeLinesBecomeUnresolvedAndDoNotProduceATotal()
    {
        var malformedPerItem = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTotal : abc");
        var malformedFinal = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTOTAL 12.345");

        Assert.AreEqual(HiboutikPasteLineKind.UnresolvedLine, malformedPerItem.Lines[1].Kind);
        Assert.AreEqual(HiboutikPasteLineKind.UnresolvedLine, malformedFinal.Lines[1].Kind);
        Assert.IsNull(malformedPerItem.SourceTotalTtc);
        Assert.IsNull(malformedFinal.SourceTotalTtc);
    }

    [TestMethod]
    public void KnownDeliveryServiceLineIsIgnoredAndItsTotalCanCompleteDerivation()
    {
        var result = HiboutikProductBlockParser.Parse("1 x AA1 Produit\nTotal : 5\n1 x Livraison (0)\nTotal : 0");

        Assert.AreEqual(HiboutikPasteLineKind.KnownIgnoredLine, result.Lines[2].Kind);
        Assert.AreEqual(HiboutikPasteIgnoredReason.DeliveryServiceLine, result.Lines[2].IgnoredReason);
        Assert.AreEqual(500, result.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public void InvalidQuantityOrMissingCodeIsUnresolved()
    {
        var result = HiboutikProductBlockParser.Parse("0 x AA1 Produit\n1 x (5.50)");

        Assert.IsTrue(result.Lines.All(line => line.Kind == HiboutikPasteLineKind.UnresolvedLine));
        Assert.HasCount(2, result.UnresolvedLines);
    }

    [TestMethod]
    public void ArbitraryUnknownMaterialLineIsUnresolved()
    {
        var result = HiboutikProductBlockParser.Parse("Order metadata that is not a product");

        Assert.AreEqual(HiboutikPasteLineKind.UnresolvedLine, result.Lines.Single().Kind);
        Assert.AreEqual("Order metadata that is not a product", result.UnresolvedLines.Single().SourceText);
    }

    [TestMethod]
    public void UnknownCodeRemainsSyntacticallyValidCandidateWithoutNameMatching()
    {
        var result = HiboutikProductBlockParser.Parse("1 x FUTURE-42 Sushi Saumon (99.99)\nTotal : 99.99");

        Assert.AreEqual(HiboutikPasteLineKind.ProductCandidate, result.ProductCandidates.Single().Kind);
        Assert.AreEqual("FUTURE-42", result.ProductCandidates.Single().CandidateCode);
        Assert.AreEqual(9999, result.SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public void DescriptionAndParenthesizedSourcePriceDoNotChangeQuantityOrCode()
    {
        var result = HiboutikProductBlockParser.Parse("7 x CODE-7 A completely different source name (1234.567)");

        Assert.AreEqual(7, result.ProductCandidates.Single().Quantity);
        Assert.AreEqual("CODE-7", result.ProductCandidates.Single().CandidateCode);
        Assert.AreEqual("A completely different source name (1234.567)", result.ProductCandidates.Single().SourceDescription);
        Assert.IsNull(result.SourceTotalTtc);
    }

    [TestMethod]
    public void EmptyWhitespaceOnlyInputIsDeterministicAndEmpty()
    {
        var first = HiboutikProductBlockParser.Parse(" \r\n\u00a0 \n");
        var second = HiboutikProductBlockParser.Parse(null);

        Assert.IsEmpty(first.Lines);
        Assert.IsEmpty(second.Lines);
        Assert.AreEqual(first.SourceTotalTtc, second.SourceTotalTtc);
        Assert.IsFalse(first.HasUsefulLines);
        Assert.IsNull(first.SourceTotalTtc);
    }

    [TestMethod]
    public void MoneyGrammarAcceptsZeroOneAndTwoFractionalDigits()
    {
        Assert.AreEqual(0, HiboutikProductBlockParser.Parse("TOTAL 0").SourceTotalTtc!.Value.Cents);
        Assert.AreEqual(550, HiboutikProductBlockParser.Parse("TOTAL 5.5").SourceTotalTtc!.Value.Cents);
        Assert.AreEqual(2430, HiboutikProductBlockParser.Parse("TOTAL 24.30").SourceTotalTtc!.Value.Cents);
    }

    [TestMethod]
    public void MoneyGrammarRejectsMoreThanTwoFractionalDigitsAndNegativeAmounts()
    {
        var tooPrecise = HiboutikProductBlockParser.Parse("TOTAL 12.345");
        var negative = HiboutikProductBlockParser.Parse("TOTAL -1");

        Assert.IsNull(tooPrecise.SourceTotalTtc);
        Assert.IsNull(negative.SourceTotalTtc);
        Assert.IsTrue(tooPrecise.HasUnresolvedLines);
        Assert.IsTrue(negative.HasUnresolvedLines);
    }

    [TestMethod]
    public void UnresolvedProductSyntaxRetainsReliableTransientContext()
    {
        var result = HiboutikProductBlockParser.Parse("2 x (missing-code) source description");

        var unresolved = result.UnresolvedLines.Single();
        Assert.AreEqual(2, unresolved.Quantity);
        Assert.IsNull(unresolved.CandidateCode);
        Assert.AreEqual("2 x (missing-code) source description", unresolved.SourceText);
    }

    [TestMethod]
    public void SyntheticFixtureContainsNoCustomerOrContactData()
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "samples", "pasted-orders", "hiboutik-product-block-synthetic.txt");
        var fixture = File.ReadAllText(fixturePath);

        Assert.IsFalse(fixture.Contains('@', StringComparison.Ordinal));
        Assert.IsFalse(fixture.Contains("06 12 34 56 78", StringComparison.Ordinal));
        Assert.IsFalse(fixture.Contains("rue", StringComparison.OrdinalIgnoreCase));
    }
}
