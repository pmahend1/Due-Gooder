using DueGooder.Infrastructure.Http;

namespace DueGooder.Tests;

public sealed class RobotsTxtTests
{
    #region State

    private const string Token = "DueGooderBot";

    private const string GetTermsPath = "/StudentRegistrationSsb/ssb/classSearch/getTerms?searchTerm=&offset=1&max=100";

    private static readonly Uri RobotsUrl = new("https://registrar.example.edu/robots.txt");

    #endregion State

    #region Methods

    [Fact]
    public void The_product_token_is_the_user_agent_up_to_the_first_slash() =>
        Assert.Equal(Token, RobotsTxt.ProductTokenOf(HttpFetcherOptions.DefaultUserAgent));

    [Fact]
    public void Disallow_all_for_every_agent_blocks_the_registration_paths()
    {
        var robots = RobotsTxt.Parse(RobotsUrl, "User-agent: *\nDisallow: /\n", Token);

        Assert.False(robots.IsAllowed(GetTermsPath));
        Assert.Contains("disallows", robots.ExplainRefusal(GetTermsPath));
    }

    [Fact]
    public void A_group_naming_our_token_replaces_the_star_group()
    {
        var robots = RobotsTxt.Parse(RobotsUrl,
                                     "User-agent: *\nDisallow: /\n\nUser-agent: duegooderbot\nDisallow: /private\n",
                                     Token);

        Assert.True(robots.IsAllowed(GetTermsPath));
        Assert.False(robots.IsAllowed("/private/data"));
    }

    [Fact]
    public void The_longest_matching_rule_wins_and_allow_wins_a_tie()
    {
        var robots = RobotsTxt.Parse(RobotsUrl,
                                     """
                                     User-agent: *
                                     Disallow: /StudentRegistrationSsb/
                                     Allow: /StudentRegistrationSsb/ssb/classSearch/
                                     Disallow: /same
                                     Allow: /same
                                     """,
                                     Token);

        Assert.True(robots.IsAllowed(GetTermsPath));
        Assert.False(robots.IsAllowed("/StudentRegistrationSsb/ssb/term/search?mode=search"));
        Assert.True(robots.IsAllowed("/same"));
    }

    [Fact]
    public void Wildcards_and_end_anchors_match_like_rfc_9309()
    {
        var robots = RobotsTxt.Parse(RobotsUrl, "User-agent: *\nDisallow: /*.pdf$\nDisallow: /a*/b\n", Token);

        Assert.False(robots.IsAllowed("/docs/catalog.pdf"));
        Assert.True(robots.IsAllowed("/docs/catalog.pdf?download=1"));
        Assert.False(robots.IsAllowed("/aaa/b/c"));
        Assert.True(robots.IsAllowed("/b"));
    }

    [Fact]
    public void Consecutive_user_agent_lines_share_one_group_and_its_crawl_delay()
    {
        var robots = RobotsTxt.Parse(RobotsUrl,
                                     "User-agent: somebot # comment\nUser-agent: *\nCrawl-delay: 7.5\nDisallow: /x\n",
                                     Token);

        Assert.False(robots.IsAllowed("/x"));
        Assert.Equal(TimeSpan.FromSeconds(7.5), robots.CrawlDelay);
    }

    [Fact]
    public void An_unreachable_file_disallows_everything_and_a_missing_file_allows_everything()
    {
        var unreachable = RobotsTxt.Unreachable(RobotsUrl, Token, "HTTP 503");
        var missing = RobotsTxt.Unavailable(RobotsUrl, Token);

        Assert.False(unreachable.IsAllowed(GetTermsPath));
        Assert.Contains("HTTP 503", unreachable.ExplainRefusal(GetTermsPath));
        Assert.True(missing.IsAllowed(GetTermsPath));
    }

    #endregion Methods
}
