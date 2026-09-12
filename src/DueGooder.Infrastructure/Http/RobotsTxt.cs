using System.Globalization;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// One host's robots.txt (RFC 9309), reduced to the rules for our product token: the groups that name the
/// token, or the <c>*</c> groups when none does. The longest matching pattern wins, and Allow wins a tie.
/// </summary>
internal sealed class RobotsTxt
{
    #region State

    private readonly IReadOnlyList<RobotsRule> _rules;

    private readonly string _productToken;

    /// <summary>Where the file was, or would have been, read from.</summary>
    public Uri SourceUrl { get; }

    /// <summary>Crawl-delay from our group, when the file sets one.</summary>
    public TimeSpan? CrawlDelay { get; }

    /// <summary>Why the file couldn't be read. When set, every path is disallowed.</summary>
    public string? UnreachableReason { get; }

    #endregion State

    #region Methods

    private RobotsTxt(Uri sourceUrl,
                      string productToken,
                      IReadOnlyList<RobotsRule> rules,
                      TimeSpan? crawlDelay,
                      string? unreachableReason)
    {
        SourceUrl = sourceUrl;
        _productToken = productToken;
        _rules = rules;
        CrawlDelay = crawlDelay;
        UnreachableReason = unreachableReason;
    }

    /// <summary>The robots.txt product token of a User-Agent: the text before the first <c>/</c> or space.</summary>
    public static string ProductTokenOf(string userAgent) => userAgent.Split('/', ' ')[0];

    /// <summary>No usable file (4xx, or too many redirects): RFC 9309 allows crawling everything.</summary>
    public static RobotsTxt Unavailable(Uri sourceUrl, string productToken) => new(sourceUrl, productToken, [], null, null);

    /// <summary>A server or network error: RFC 9309 requires assuming everything is disallowed.</summary>
    public static RobotsTxt Unreachable(Uri sourceUrl, string productToken, string reason) =>
        new(sourceUrl, productToken, [], null, reason);

    public static RobotsTxt Parse(Uri sourceUrl, string content, string productToken)
    {
        var groups = new List<RobotsGroup>();
        RobotsGroup? current = null;
        var previousLineWasUserAgent = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Split('#')[0];
            var colon = line.IndexOf(':');
            if (colon < 0)
            {
                continue;
            }

            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();
            if (field is "user-agent")
            {
                // Consecutive user-agent lines share one group; a user-agent after rules starts a new one.
                if (current is null || previousLineWasUserAgent is false)
                {
                    current = new RobotsGroup();
                    groups.Add(current);
                }

                current.UserAgents.Add(value.ToLowerInvariant());
                previousLineWasUserAgent = true;
                continue;
            }

            previousLineWasUserAgent = false;
            if (current is null)
            {
                continue;
            }

            switch (field)
            {
                case "allow" or "disallow" when value is not "":
                    current.Rules.Add(new RobotsRule(value, field is "allow"));
                    break;
                case "crawl-delay" when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                                        && seconds >= 0:
                    current.CrawlDelay = TimeSpan.FromSeconds(seconds);
                    break;
            }
        }

        var token = productToken.ToLowerInvariant();
        var selected = groups.Where(group => group.UserAgents.Contains(token)).ToList();
        if (selected.Count is 0)
        {
            selected = groups.Where(group => group.UserAgents.Contains("*")).ToList();
        }

        var crawlDelays = selected.Select(group => group.CrawlDelay).OfType<TimeSpan>().ToList();
        return new RobotsTxt(sourceUrl,
                             productToken,
                             selected.SelectMany(group => group.Rules).ToList(),
                             crawlDelays.Count is 0 ? null : crawlDelays.Max(),
                             null);
    }

    public bool IsAllowed(string pathAndQuery)
    {
        if (UnreachableReason is not null)
        {
            return false;
        }

        RobotsRule? winner = null;
        foreach (var rule in _rules.Where(rule => rule.Matches(pathAndQuery)))
        {
            if (winner is null
                || rule.Pattern.Length > winner.Pattern.Length
                || (rule.Pattern.Length == winner.Pattern.Length && rule.Allow))
            {
                winner = rule;
            }
        }

        return winner is null || winner.Allow;
    }

    public string ExplainRefusal(string pathAndQuery) => UnreachableReason is null
        ? $"robots.txt ({SourceUrl}) disallows {pathAndQuery} for {_productToken}, so the request was not sent"
        : $"robots.txt ({SourceUrl}) could not be read ({UnreachableReason}); RFC 9309 treats that as disallow-all, "
          + "so the request was not sent";

    #endregion Methods
}
