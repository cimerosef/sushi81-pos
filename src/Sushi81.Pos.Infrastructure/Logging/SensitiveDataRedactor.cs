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
        var withoutEmails = EmailPattern().Replace(withoutPhones, "[redacted-email]");
        var withoutUrlCredentials = UrlCredentialsPattern().Replace(withoutEmails, "$1[redacted-userinfo]@");
        var withoutAuthorization = AuthorizationHeaderPattern().Replace(withoutUrlCredentials, "$1[redacted-authorization]");
        var withoutBearer = BearerPattern().Replace(withoutAuthorization, "Bearer [redacted-token]");
        var withoutGithubTokens = GithubTokenPattern().Replace(withoutBearer, "[redacted-github-token]");
        var withoutQuerySecrets = QuerySecretPattern().Replace(withoutGithubTokens, "$1[redacted-secret]");
        return SecretFieldPattern().Replace(withoutQuerySecrets, "$1[redacted-secret]");
    }

    [GeneratedRegex(@"(?<!\d)(?:0\d(?:[ .-]?\d{2}){4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex FrenchTelephonePattern();

    [GeneratedRegex(@"\b[\w.+-]+@[\w-]+(?:\.[\w-]+)+\b", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex(@"(?<prefix>https?://)(?:[^/\s:@]+(?::[^/\s@]*)?@)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentialsPattern();

    [GeneratedRegex(@"(?<prefix>\bAuthorization\s*:\s*)[^\r\n]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,})\b", RegexOptions.CultureInvariant)]
    private static partial Regex GithubTokenPattern();

    [GeneratedRegex(@"(?<prefix>[?&](?:access[_-]?token|refresh[_-]?token|token|secret|password|client[_-]?secret|authorization)=)[^&#\s]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex QuerySecretPattern();

    [GeneratedRegex(@"(?<prefix>\b(?:access[_-]?token|refresh[_-]?token|token|secret|password|client[_-]?secret|authorization)\b\s*[:=]\s*[""']?)[^,\s;""']+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SecretFieldPattern();
}
