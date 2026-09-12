using System.Net;
using System.Text.RegularExpressions;

namespace DueGooder.Application.Discovery;

/// <summary>
/// Pulls links out of HTML and ranks the ones likely to lead to a class schedule. A regular expression is enough here:
/// discovery needs hrefs and link text, not a DOM, and a missed link only costs a fallback probe.
/// </summary>
public static partial class HtmlLinks
{
    #region State

    private static readonly string[] ScheduleTerms =
    [
        "class schedule", "schedule of classes", "course schedule", "class search", "course search", "search for classes",
        "browse classes", "course offerings", "class-schedule", "course-schedule", "courses-schedules",
        "schedule-of-classes", "class-search", "course-search",
    ];

    private static readonly string[] RegistrarTerms = ["registrar", "registration"];

    private static readonly string[] SkippedExtensions =
        [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".jpg", ".jpeg", ".png", ".gif", ".svg", ".zip", ".ics", ".mp4"];

    #endregion State

    #region Methods

    public static IReadOnlyList<PageLink> Extract(Uri pageUrl, string html)
    {
        var links = new List<PageLink>();
        foreach (Match match in AnchorPattern().Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
            if (href is "" || href.StartsWith('#')
                || Uri.TryCreate(pageUrl, href, out var url) is false
                || url.Scheme is not ("http" or "https"))
            {
                continue;
            }

            var withoutFragment = new UriBuilder(url) { Fragment = "" }.Uri;
            var text = WhitespacePattern().Replace(WebUtility.HtmlDecode(TagPattern().Replace(match.Groups["text"].Value, " ")), " ");
            links.Add(new PageLink(withoutFragment, text.Trim()));
        }

        return links;
    }

    /// <summary>3 for a schedule or class-search link, 2 for registrar or registration, 1 for anything "schedule", else 0.</summary>
    public static int ScheduleScore(PageLink link)
    {
        var path = link.Url.AbsolutePath.ToLowerInvariant();
        if (SkippedExtensions.Any(path.EndsWith))
        {
            return 0;
        }

        var haystack = $"{link.Text} {link.Url.Host}{path}".ToLowerInvariant();
        return ScheduleTerms.Any(haystack.Contains) ? 3
               : RegistrarTerms.Any(haystack.Contains) ? 2
               : haystack.Contains("schedule") ? 1
               : 0;
    }

    [GeneratedRegex("""<a\b[^>]*?\bhref\s*=\s*(["'])(?<href>.*?)\1[^>]*>(?<text>.*?)</a>""",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorPattern();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    #endregion Methods
}
