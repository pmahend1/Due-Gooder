using DueGooder.Application;
using DueGooder.Application.Discovery;
using DueGooder.Connectors.Banner9;

namespace DueGooder.Tests;

public sealed class HomepageDiscoveryTests
{
    #region State

    private static readonly string BannerTermsJson =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "banner9", "eku", "getTerms.json"));

    #endregion State

    #region Methods

    [Fact]
    public async Task Follows_registrar_and_schedule_links_to_a_banner_link_and_confirms_it_with_a_probe()
    {
        var fetcher = Site(["ssb.samford.edu"],
                           ("https://www.samford.edu/",
                            """
                            <a href="/visit">Schedule a tour</a>
                            <a href="https://news.other.org/class-schedule">Class schedule elsewhere</a>
                            <a href="/departments/registrar/default">Registrar</a>
                            """),
                           ("https://www.samford.edu/departments/registrar/default",
                            """<a href="/departments/registrar/class-schedule">Class Schedule</a>"""),
                           ("https://www.samford.edu/departments/registrar/class-schedule",
                            """<a href="https://ssb.samford.edu/StudentRegistrationSsb/ssb/term/termSelection?mode=search">Search</a>"""));

        var result = await DiscoverAsync(fetcher, "https://www.samford.edu/");

        Assert.True(result.Identified);
        Assert.Equal(DiscoveryMethod.CrawledLink, result.Method);
        Assert.Equal("banner9", result.Platform);
        Assert.Equal(new Uri("https://ssb.samford.edu/StudentRegistrationSsb/"), result.BaseUrl);
        Assert.True(result.Confidence >= 0.9);
        Assert.Contains(result.Evidence, evidence => evidence.Contains("\"Class Schedule\""));
        Assert.Contains(result.Evidence, evidence => evidence.Contains("returned Banner term JSON"));
        Assert.Equal(3, result.PagesFetched);
        Assert.DoesNotContain(fetcher.Requests, request => request.Url.Host is "news.other.org" || request.Url.AbsolutePath is "/visit");
    }

    [Fact]
    public async Task A_blocked_homepage_falls_back_to_well_known_banner_hosts()
    {
        var fetcher = Site(["sis.ou.edu"], ("https://www.ou.edu/", null));

        var result = await DiscoverAsync(fetcher, "https://www.ou.edu/");

        Assert.Equal(DiscoveryMethod.GuessedHost, result.Method);
        Assert.Equal(new Uri("https://sis.ou.edu/StudentRegistrationSsb/"), result.BaseUrl);
        Assert.Contains(result.Evidence, evidence => evidence.Contains("https://www.ou.edu/") && evidence.Contains("HTTP 403"));
    }

    [Fact]
    public async Task Follows_a_homepage_redirect_to_another_domain_and_finds_the_link_there()
    {
        var fetcher = new RoutedHttpFetcher((_, url) => url.AbsoluteUri switch
        {
            "https://www.cofc.edu/" => RoutedHttpFetcher.Redirect(url, "https://charleston.edu/"),
            "https://charleston.edu/" => RoutedHttpFetcher.Ok(url, """<a href="https://ssb.cofc.edu/StudentRegistrationSsb/ssb/term/termSelection?mode=search">Class search</a>"""),
            _ when url.Host is "ssb.cofc.edu" => RoutedHttpFetcher.Ok(url, BannerTermsJson),
            _ => RoutedHttpFetcher.Status(url, 404),
        });

        var result = await DiscoverAsync(fetcher, "https://www.cofc.edu/");

        Assert.Equal(DiscoveryMethod.HomepageLink, result.Method);
        Assert.Equal(new Uri("https://ssb.cofc.edu/StudentRegistrationSsb/"), result.BaseUrl);
    }

    [Fact]
    public async Task A_school_on_a_platform_without_a_connector_goes_to_review_with_what_was_seen()
    {
        var fetcher = Site([],
                           ("https://louisville.edu/",
                            """<a href="https://csprd.louisville.edu/psc/ps_class/EMPLOYEE/PSFT_CS/c/COMMUNITY_ACCESS.CLASS_SEARCH.GBL">Class Search</a>"""));

        var result = await DiscoverAsync(fetcher, "https://louisville.edu/");

        Assert.False(result.Identified);
        Assert.Equal(DiscoveryMethod.NotFound, result.Method);
        Assert.Equal("not identified from the homepage: no connector recognized the site; its pages point to peoplesoft", result.ReviewReason);
        Assert.StartsWith("peoplesoft: https://csprd.louisville.edu/psc/", Assert.Single(result.PlatformHints));
        Assert.Contains(result.Evidence, evidence => evidence.StartsWith("guessed hosts that didn't confirm: ssb.louisville.edu"));
    }

    [Fact]
    public async Task A_banner_link_whose_probe_robots_txt_refuses_goes_to_review_with_that_reason()
    {
        var fetcher = new RoutedHttpFetcher((_, url) => url.Host switch
        {
            "www.eku.edu" => RoutedHttpFetcher.Ok(url, """<a href="https://registrationss.eku.edu/StudentRegistrationSsb/ssb/term/termSelection?mode=search">Class schedule</a>"""),
            "registrationss.eku.edu" => throw new ConnectorException("robots.txt (https://registrationss.eku.edu/robots.txt) disallows "
                                                                     + url.PathAndQuery + " for DueGooderBot, so the request was not sent",
                                                                     url),
            _ => throw new ConnectorException("network error: nodename nor servname provided", url),
        });

        var result = await DiscoverAsync(fetcher, "https://www.eku.edu/");

        Assert.False(result.Identified);
        Assert.Contains("https://registrationss.eku.edu/StudentRegistrationSsb/", result.ReviewReason);
        Assert.Contains("robots.txt (https://registrationss.eku.edu/robots.txt) disallows", result.ReviewReason);
    }

    [Fact]
    public async Task A_guessed_host_that_robots_txt_refuses_is_named_in_the_review_reason_and_a_404_one_is_not()
    {
        var fetcher = new RoutedHttpFetcher((_, url) => url.Host switch
        {
            "www.kccd.edu" => RoutedHttpFetcher.Ok(url, "<p>No links here.</p>"),
            "ssb.kccd.edu" => RoutedHttpFetcher.Status(url, 404),
            "reg-prod.ec.kccd.edu" => throw new ConnectorException("robots.txt (https://reg-prod.ec.kccd.edu/robots.txt) could not be read "
                                                                   + "(HTTP 503); RFC 9309 treats that as disallow-all, so the request was not sent",
                                                                   url),
            _ => throw new ConnectorException($"robots.txt could not be read ({ConnectorException.UnresolvedHostReason})", url),
        });

        var result = await DiscoverAsync(fetcher, "https://www.kccd.edu/");

        Assert.StartsWith("not identified from the homepage: the well-known banner9 host reg-prod.ec.kccd.edu exists", result.ReviewReason);
        Assert.Contains("(HTTP 503)", result.ReviewReason);
    }

    [Theory]
    [InlineData("Class Schedule", "/x", 3)]
    [InlineData("", "/academics/courses-schedules", 3)]
    [InlineData("Office of the Registrar", "/x", 2)]
    [InlineData("Schedule a tour", "/visit", 1)]
    [InlineData("Class Schedule (PDF)", "/files/schedule.pdf", 0)]
    [InlineData("Athletics", "/sports", 0)]
    public void Ranks_links_by_how_likely_they_lead_to_a_class_schedule(string text, string path, int expected)
    {
        Assert.Equal(expected, HtmlLinks.ScheduleScore(new PageLink(new Uri(new Uri("https://www.example.edu/"), path), text)));
    }

    [Fact]
    public void Extracts_links_with_their_text_resolved_against_the_page()
    {
        var links = HtmlLinks.Extract(new Uri("https://www.example.edu/a/"),
                                      """<a class="nav" href="../registrar/?x=1&amp;y=2#top"><span>Office of the</span> Registrar</a><a href="mailto:x@y">Mail</a>""");

        var link = Assert.Single(links);
        Assert.Equal(new Uri("https://www.example.edu/registrar/?x=1&y=2"), link.Url);
        Assert.Equal("Office of the Registrar", link.Text);
    }

    private static Task<DiscoveryResult> DiscoverAsync(RoutedHttpFetcher fetcher, string homepage) =>
        new HomepageDiscovery([new Banner9Connector(fetcher)], fetcher, new DiscoveryOptions()).DiscoverAsync(new Uri(homepage),
                                                                                                             CancellationToken.None);

    /// <summary>
    /// Pages by URL (a null body answers HTTP 403, like a bot-protected homepage), Banner term JSON on the given hosts,
    /// and a DNS failure anywhere else, the way a guessed host name that doesn't exist fails.
    /// </summary>
    private static RoutedHttpFetcher Site(string[] bannerHosts, params (string Url, string? Html)[] pages) =>
        new((_, url) =>
        {
            foreach (var (pageUrl, html) in pages)
            {
                if (url.AbsoluteUri == pageUrl)
                {
                    return html is null ? RoutedHttpFetcher.Status(url, 403) : RoutedHttpFetcher.Ok(url, html);
                }
            }

            if (bannerHosts.Contains(url.Host) && url.AbsolutePath.EndsWith("/ssb/classSearch/getTerms"))
            {
                return RoutedHttpFetcher.Ok(url, BannerTermsJson);
            }

            throw new ConnectorException($"robots.txt (https://{url.Host}/robots.txt) could not be read "
                                         + $"({ConnectorException.UnresolvedHostReason}); RFC 9309 treats that as disallow-all, so the "
                                         + "request was not sent",
                                         url);
        });

    #endregion Methods
}
