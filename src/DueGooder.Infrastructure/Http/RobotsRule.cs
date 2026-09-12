using System.Text.RegularExpressions;

namespace DueGooder.Infrastructure.Http;

/// <summary>One Allow or Disallow line. <c>*</c> matches any run of characters and a trailing <c>$</c> anchors the end.</summary>
internal sealed record RobotsRule(string Pattern, bool Allow)
{
    #region State

    private readonly Regex _matcher = ToRegex(Pattern);

    #endregion State

    #region Methods

    public bool Matches(string pathAndQuery) => _matcher.IsMatch(pathAndQuery);

    private static Regex ToRegex(string pattern)
    {
        var anchored = pattern.EndsWith('$');
        var body = anchored ? pattern[..^1] : pattern;
        var expression = "^" + Regex.Escape(body).Replace(@"\*", ".*") + (anchored ? "$" : "");
        return new Regex(expression, RegexOptions.CultureInvariant);
    }

    #endregion Methods
}
