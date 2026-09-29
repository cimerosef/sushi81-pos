namespace Sushi81.Pos.Application.Catalogue;

/// <summary>Culture-independent natural ordering for current operator-facing product codes.</summary>
public sealed class NaturalProductCodeComparer : IComparer<string>
{
    public static NaturalProductCodeComparer Instance { get; } = new();

    private NaturalProductCodeComparer() { }

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (IsAsciiDigit(left[leftIndex]) && IsAsciiDigit(right[rightIndex]))
            {
                var leftEnd = leftIndex;
                while (leftEnd < left.Length && IsAsciiDigit(left[leftEnd])) leftEnd++;
                var rightEnd = rightIndex;
                while (rightEnd < right.Length && IsAsciiDigit(right[rightEnd])) rightEnd++;

                var leftSignificant = leftIndex;
                while (leftSignificant < leftEnd && left[leftSignificant] == '0') leftSignificant++;
                var rightSignificant = rightIndex;
                while (rightSignificant < rightEnd && right[rightSignificant] == '0') rightSignificant++;

                var magnitude = (leftEnd - leftSignificant).CompareTo(rightEnd - rightSignificant);
                if (magnitude != 0) return magnitude;
                for (var index = 0; index < leftEnd - leftSignificant; index++)
                {
                    var digit = left[leftSignificant + index].CompareTo(right[rightSignificant + index]);
                    if (digit != 0) return digit;
                }

                leftIndex = leftEnd;
                rightIndex = rightEnd;
                continue;
            }

            var text = char.ToUpperInvariant(left[leftIndex]).CompareTo(char.ToUpperInvariant(right[rightIndex]));
            if (text != 0) return text;
            leftIndex++;
            rightIndex++;
        }

        if (leftIndex != left.Length || rightIndex != right.Length)
            return (left.Length - leftIndex).CompareTo(right.Length - rightIndex);

        // Equivalent natural keys retain a total, repeatable order: raw code ignoring case,
        // then raw ordinal code. Product-row callers add ProductId as the final tie-breaker.
        var folded = StringComparer.OrdinalIgnoreCase.Compare(left, right);
        return folded != 0 ? folded : StringComparer.Ordinal.Compare(left, right);
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';
}
