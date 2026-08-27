using System.Text.RegularExpressions;

namespace Sushi81.Pos.Infrastructure.Logging;

/// <summary>Last-line diagnostic protection for foundation logs; business payloads must never be supplied to logs.</summary>
public static partial class SensitiveDataRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var withoutPhones = FrenchTelephonePattern().Replace(value, "[redacted-phone]");
        return EmailPattern().Replace(withoutPhones, "[redacted-email]");
    }

    [GeneratedRegex(@"(?<!\d)(?:0\d(?:[ .-]?\d{2}){4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex FrenchTelephonePattern();

    [GeneratedRegex(@"\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+\b", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();
}
