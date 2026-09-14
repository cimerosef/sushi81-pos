using System.Collections.Immutable;
using System.Text;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Application.OrderEntry;

/// <summary>Pure parser outcomes for one non-blank Hiboutik source line.</summary>
public enum HiboutikPasteLineKind
{
    ProductCandidate,
    KnownIgnoredLine,
    UnresolvedLine
}

/// <summary>Specific source lines that are safely understood but never become cart products.</summary>
public enum HiboutikPasteIgnoredReason
{
    PerItemTotal,
    FinalTotal,
    DeliveryServiceLine
}

/// <summary>Transient, immutable evidence for one normalized non-blank source line.</summary>
public sealed record HiboutikPasteLine(
    int SourceLineNumber,
    string SourceText,
    HiboutikPasteLineKind Kind,
    int? Quantity,
    string? CandidateCode,
    string? SourceDescription,
    Money? SourceAmountTtc,
    HiboutikPasteIgnoredReason? IgnoredReason,
    bool IsRecognizedChargeRow)
{
    public bool IsProductCandidate => Kind == HiboutikPasteLineKind.ProductCandidate;
    public bool IsKnownIgnoredLine => Kind == HiboutikPasteLineKind.KnownIgnoredLine;
    public bool IsUnresolvedLine => Kind == HiboutikPasteLineKind.UnresolvedLine;
}

/// <summary>Pure parser output; it contains no persistence or catalogue-resolution behavior.</summary>
public sealed record HiboutikPasteParseResult(
    ImmutableArray<HiboutikPasteLine> Lines,
    Money? SourceTotalTtc)
{
    public ImmutableArray<HiboutikPasteLine> ProductCandidates =>
        Lines.Where(line => line.IsProductCandidate).ToImmutableArray();

    public ImmutableArray<HiboutikPasteLine> UnresolvedLines =>
        Lines.Where(line => line.IsUnresolvedLine).ToImmutableArray();

    public bool HasUsefulLines => Lines.Length > 0;
    public bool HasUnresolvedLines => UnresolvedLines.Length > 0;
}

/// <summary>
/// Parses the small, copied Hiboutik product-detail block without catalogue access,
/// network access, clipboard access or business writes.
/// </summary>
public static class HiboutikProductBlockParser
{
    public static HiboutikPasteParseResult Parse(string? sourceText)
    {
        var lines = new List<HiboutikPasteLine>();
        var chargeRows = new List<ChargeRow>();
        var finalTotals = new List<Money>();
        var hasMalformedFinalTotal = false;
        var hasMalformedTotalLikeLine = false;
        var hasUnresolvedMaterial = false;
        var hasUnpairedPerItemTotal = false;
        var finalTotalBoundarySeen = false;
        ChargeRow? currentCharge = null;

        var normalizedSource = (sourceText ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var physicalLines = normalizedSource.Split('\n', StringSplitOptions.None);
        for (var physicalLineIndex = 0; physicalLineIndex < physicalLines.Length; physicalLineIndex++)
        {
            var normalizedLine = NormalizeLine(physicalLines[physicalLineIndex]);
            if (normalizedLine.Length == 0) continue;

            var sourceLineNumber = physicalLineIndex + 1;
            if (TryParseTotalLikeLine(normalizedLine, out var totalLineKind, out var parsedAmount, out var malformed))
            {
                if (malformed)
                {
                    hasMalformedTotalLikeLine = true;
                    hasUnresolvedMaterial = true;
                    hasMalformedFinalTotal |= totalLineKind == TotalLineKind.Final;
                    currentCharge?.BlockAssociation = true;
                    currentCharge = null;
                    lines.Add(new HiboutikPasteLine(
                        sourceLineNumber, normalizedLine, HiboutikPasteLineKind.UnresolvedLine,
                        null, null, null, null, null, false));
                    continue;
                }

                if (totalLineKind == TotalLineKind.Final)
                {
                    finalTotals.Add(parsedAmount!.Value);
                    finalTotalBoundarySeen = true;
                    currentCharge = null;
                    lines.Add(new HiboutikPasteLine(
                        sourceLineNumber, normalizedLine, HiboutikPasteLineKind.KnownIgnoredLine,
                        null, null, null, parsedAmount, HiboutikPasteIgnoredReason.FinalTotal, false));
                    continue;
                }

                if (currentCharge is null || finalTotalBoundarySeen || currentCharge.AssociatedTotalTtc.HasValue || currentCharge.BlockAssociation)
                {
                    hasUnpairedPerItemTotal = true;
                }
                else
                {
                    currentCharge.AssociatedTotalTtc = parsedAmount;
                    currentCharge = null;
                }

                lines.Add(new HiboutikPasteLine(
                    sourceLineNumber, normalizedLine, HiboutikPasteLineKind.KnownIgnoredLine,
                    null, null, null, parsedAmount, HiboutikPasteIgnoredReason.PerItemTotal, false));
                continue;
            }

            if (IsDeliveryServiceLine(normalizedLine))
            {
                var delivery = new ChargeRow();
                chargeRows.Add(delivery);
                currentCharge = delivery;
                lines.Add(new HiboutikPasteLine(
                    sourceLineNumber, normalizedLine, HiboutikPasteLineKind.KnownIgnoredLine,
                    1, "Livraison", "(0)", null, HiboutikPasteIgnoredReason.DeliveryServiceLine, true));
                continue;
            }

            var product = ReadProductLine(normalizedLine);
            if (product.IsValid)
            {
                var productCharge = new ChargeRow();
                chargeRows.Add(productCharge);
                currentCharge = productCharge;
                lines.Add(new HiboutikPasteLine(
                    sourceLineNumber, normalizedLine, HiboutikPasteLineKind.ProductCandidate,
                    product.Quantity, product.CandidateCode, product.SourceDescription,
                    null, null, true));
                continue;
            }

            hasUnresolvedMaterial = true;
            currentCharge?.BlockAssociation = true;
            currentCharge = null;
            lines.Add(new HiboutikPasteLine(
                sourceLineNumber, normalizedLine, HiboutikPasteLineKind.UnresolvedLine,
                product.Quantity, product.CandidateCode, product.SourceDescription,
                null, null, false));
        }

        var sourceTotal = DetermineReliableSourceTotal(
            chargeRows, finalTotals, hasMalformedFinalTotal, hasMalformedTotalLikeLine,
            hasUnresolvedMaterial, hasUnpairedPerItemTotal);
        return new HiboutikPasteParseResult(lines.ToImmutableArray(), sourceTotal);
    }

    private static Money? DetermineReliableSourceTotal(
        IReadOnlyList<ChargeRow> chargeRows,
        List<Money> finalTotals,
        bool hasMalformedFinalTotal,
        bool hasMalformedTotalLikeLine,
        bool hasUnresolvedMaterial,
        bool hasUnpairedPerItemTotal)
    {
        if (finalTotals.Count == 1 && !hasMalformedFinalTotal)
            return finalTotals[0];

        if (finalTotals.Count != 0 || chargeRows.Count == 0 || hasMalformedTotalLikeLine ||
            hasUnresolvedMaterial || hasUnpairedPerItemTotal || chargeRows.Any(row =>
                row.BlockAssociation || !row.AssociatedTotalTtc.HasValue))
            return null;

        var cents = 0L;
        foreach (var chargeRow in chargeRows)
        {
            var chargeCents = chargeRow.AssociatedTotalTtc!.Value.Cents;
            if (chargeCents > long.MaxValue - cents) return null;
            cents += chargeCents;
        }
        return Money.FromCents(cents);
    }

    private static bool IsDeliveryServiceLine(string line) =>
        string.Equals(line, "1 x Livraison (0)", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeLine(string line)
    {
        var builder = new StringBuilder(line.Length);
        var pendingSpace = false;
        foreach (var character in line)
        {
            var normalizedCharacter = character == '\u00A0' ? ' ' : character;
            if (char.IsWhiteSpace(normalizedCharacter))
            {
                if (builder.Length > 0) pendingSpace = true;
                continue;
            }

            if (pendingSpace) builder.Append(' ');
            builder.Append(normalizedCharacter);
            pendingSpace = false;
        }

        return builder.ToString().Trim();
    }

    private static bool TryParseTotalLikeLine(
        string line,
        out TotalLineKind kind,
        out Money? amount,
        out bool malformed)
    {
        kind = default;
        amount = null;
        malformed = false;
        const string keyword = "total";
        if (!line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) return false;
        if (line.Length > keyword.Length && line[keyword.Length] is not (' ' or ':')) return false;

        var remainder = line[keyword.Length..].TrimStart();
        if (remainder.StartsWith(':'))
        {
            kind = TotalLineKind.PerItem;
            remainder = remainder[1..].Trim();
        }
        else
        {
            kind = TotalLineKind.Final;
            remainder = remainder.Trim();
        }

        if (!TryParseMoney(remainder, out var parsed))
        {
            malformed = true;
            return true;
        }

        amount = parsed;
        return true;
    }

    private static bool TryParseMoney(string text, out Money money)
    {
        money = default;
        if (text.Length == 0) return false;

        var decimalSeparator = text.IndexOf('.');
        var wholeEnd = decimalSeparator < 0 ? text.Length : decimalSeparator;
        if (decimalSeparator >= 0 && text.IndexOf('.', decimalSeparator + 1) >= 0) return false;
        var fractionLength = decimalSeparator < 0 ? 0 : text.Length - decimalSeparator - 1;
        if (fractionLength is < 0 or > 2 || wholeEnd == 0) return false;

        long whole = 0;
        for (var index = 0; index < wholeEnd; index++)
        {
            var character = text[index];
            if (character is < '0' or > '9') return false;
            var digit = character - '0';
            if (whole > (long.MaxValue - digit) / 10) return false;
            whole = whole * 10 + digit;
        }

        var fraction = fractionLength switch
        {
            0 => 0,
            1 when text[^1] is >= '0' and <= '9' => (text[^1] - '0') * 10,
            2 when text[^2] is >= '0' and <= '9' && text[^1] is >= '0' and <= '9' =>
                (text[^2] - '0') * 10 + (text[^1] - '0'),
            _ => -1
        };
        if (fraction < 0) return false;

        if (whole > (long.MaxValue - fraction) / 100) return false;
        money = Money.FromCents(whole * 100 + fraction);
        return true;
    }

    private static ProductReadResult ReadProductLine(string line)
    {
        var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return new ProductReadResult(false, null, null, null);

        int? quantity = null;
        if (TryParseInteger(tokens[0], out var parsedQuantity) && parsedQuantity > 0)
            quantity = parsedQuantity;

        string? candidateCode = null;
        string? description = null;
        var hasSeparator = tokens.Length > 1 && tokens[1] == "x";
        if (hasSeparator && tokens.Length > 2)
        {
            candidateCode = IsCandidateCodeToken(tokens[2]) ? tokens[2] : null;
            if (candidateCode is not null && tokens.Length > 3)
                description = string.Join(' ', tokens, 3, tokens.Length - 3);
        }

        var valid = quantity.HasValue && hasSeparator && candidateCode is not null;
        return new ProductReadResult(valid, quantity, candidateCode, description);
    }

    private static bool IsCandidateCodeToken(string token) =>
        token.Length > 0 && !token.StartsWith('(') && token.Any(character =>
            (character >= '0' && character <= '9') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= 'a' && character <= 'z'));

    private static bool TryParseInteger(string text, out int value)
    {
        value = 0;
        if (text.Length == 0) return false;
        long parsed = 0;
        foreach (var character in text)
        {
            if (character is < '0' or > '9') return false;
            var digit = character - '0';
            if (parsed > (int.MaxValue - digit) / 10) return false;
            parsed = parsed * 10 + digit;
        }

        value = (int)parsed;
        return true;
    }

    private enum TotalLineKind
    {
        PerItem,
        Final
    }

    private sealed class ChargeRow
    {
        public Money? AssociatedTotalTtc { get; set; }
        public bool BlockAssociation { get; set; }
    }

    private sealed record ProductReadResult(
        bool IsValid,
        int? Quantity,
        string? CandidateCode,
        string? SourceDescription);
}
