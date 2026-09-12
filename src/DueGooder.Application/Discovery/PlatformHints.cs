using System.Text.RegularExpressions;

namespace DueGooder.Application.Discovery;

/// <summary>
/// URL patterns of registration platforms that have no connector yet. They never identify a school; they only tell the
/// person working the review queue what was seen, and show which connector to build next.
/// </summary>
public static partial class PlatformHints
{
    #region State

    private static readonly (string Platform, string Marker)[] Markers =
    [
        ("peoplesoft", "/psc/"),
        ("peoplesoft", "/psp/"),
        ("peoplesoft", "CLASS_SEARCH.GBL"),
        ("workday", "myworkday.com"),
        ("workday", "myworkdaysite.com"),
        ("colleague", "/Student/Courses"),
        ("colleague", "colleague.elluciancloud.com"),
        ("banner8", "bwckschd"),
        ("banner8", "bwckgens"),
    ];

    #endregion State

    #region Methods

    /// <returns>One <c>platform: url</c> line per platform seen, first match only.</returns>
    public static IReadOnlyList<string> Find(string html)
    {
        var hints = new Dictionary<string, string>();
        foreach (Match match in UrlPattern().Matches(html))
        {
            foreach (var (platform, marker) in Markers)
            {
                if (hints.ContainsKey(platform) is false && match.Value.Contains(marker, StringComparison.OrdinalIgnoreCase))
                {
                    hints[platform] = $"{platform}: {match.Value}";
                }
            }
        }

        return [.. hints.Values];
    }

    [GeneratedRegex("""https?://[^\s"'<>]+""", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    #endregion Methods
}
