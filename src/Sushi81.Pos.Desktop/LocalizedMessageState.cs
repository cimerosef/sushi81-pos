using System.Globalization;
using Sushi81.Pos.Application.Catalogue;
using Sushi81.Pos.Domain;

namespace Sushi81.Pos.Desktop;

/// <summary>
/// Retains semantic message inputs so language changes can re-render operator text.
/// </summary>
internal sealed class LocalizedMessageState(Func<IReadOnlyDictionary<string, string>, string> render)
{
    private readonly Func<IReadOnlyDictionary<string, string>, string> render = render ?? throw new ArgumentNullException(nameof(render));
    public string Render(IReadOnlyDictionary<string, string> localized) => render(localized);
    public static LocalizedMessageState Resource(string key, string fallback, params object?[] arguments)
    {
        var capturedArguments = (object?[])arguments.Clone();
        return new(localized =>
        {
            var template = localized.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;
            return capturedArguments.Length == 0 ? template : string.Format(CultureInfo.CurrentCulture, template, capturedArguments);
        });
    }
    public static LocalizedMessageState Issue(ValidationIssue issue) => new(localized => M03Presentation.Message(issue, localized));
    public static LocalizedMessageState Issues(IEnumerable<ValidationIssue> issues, string separator)
    {
        var capturedIssues = issues.ToArray();
        return new(localized => string.Join(separator, capturedIssues.Select(issue => M03Presentation.Message(issue, localized))));
    }
    public static LocalizedMessageState Custom(Func<IReadOnlyDictionary<string, string>, string> render) => new(render);
}
