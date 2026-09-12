using DueGooder.Application;
using DueGooder.Connectors.Banner9;
using DueGooder.Domain;

namespace DueGooder.Tests;

public sealed class Banner9ConnectorTests
{
    #region Methods

    [Theory]
    [InlineData("eku", 79, "202750", "Summer 2027 (View Only)")]
    [InlineData("sunyempire", 25, "202680", "Fall 2026")]
    [InlineData("uiuc", 87, "120268", "Fall 2026 - Urbana-Champaign")]
    [InlineData("kccd", 86, "202670", "Fall 2026")]
    [InlineData("oakland", 22, "202733", "Continuing Education 2027-2028")]
    [InlineData("odu", 24, "202617", "Fall 2026 Second Eight Weeks")]
    public async Task Lists_every_term_the_school_publishes(string schoolId,
                                                            int termCount,
                                                            string firstTermCode,
                                                            string firstTermName)
    {
        var connector = new Banner9Connector(new FixtureHttpFetcher(schoolId));

        var terms = await connector.ListTermsAsync(Banner9Fixtures.Target(schoolId), CancellationToken.None);

        Assert.Equal(termCount, terms.Count);
        Assert.Equal(termCount, terms.Select(term => term.Key).Distinct().Count());
        Assert.Equal(new TermKey(schoolId, firstTermCode), terms[0].Key);
        Assert.Equal(firstTermName, terms[0].Name);
        Assert.All(terms, term => Assert.Equal(FixtureHttpFetcher.RetrievedAt, term.RetrievedAt));
    }

    [Fact]
    public async Task A_multi_institution_server_gets_its_mep_code_and_path_casing_from_config()
    {
        var fetcher = new FixtureHttpFetcher("uiuc");

        await new Banner9Connector(fetcher).ListTermsAsync(Banner9Fixtures.Target("uiuc"), CancellationToken.None);

        var request = Assert.Single(fetcher.Requests);
        Assert.Equal("/StudentRegistrationSSB/ssb/classSearch/getTerms", request.Url.AbsolutePath);
        Assert.Contains("mepCode=1UIUC", request.Url.Query);
    }

    [Theory]
    [InlineData("eku")]
    [InlineData("sunyempire")]
    [InlineData("kccd")]
    [InlineData("oakland")]
    [InlineData("odu")]
    public async Task Binds_the_term_then_pages_through_results_and_resets_in_one_session(string schoolId)
    {
        var fetcher = new FixtureHttpFetcher(schoolId);
        var term = Banner9Fixtures.CapturedTerm(schoolId);

        var sections = await new Banner9Connector(fetcher).CollectSectionsAsync(Banner9Fixtures.Target(schoolId),
                                                                                term,
                                                                                new List<CollectionGap>(),
                                                                                CancellationToken.None)
                                                          .Take(Banner9Fixtures.CapturedSections)
                                                          .ToListAsync();

        Assert.Equal(Banner9Fixtures.CapturedSections, sections.Count);
        Assert.All(sections, section => Assert.Equal(term.Key, section.Term));
        Assert.Collection(fetcher.Requests,
                          request =>
                          {
                              Assert.Equal("POST", request.Method);
                              Assert.EndsWith("/ssb/term/search", request.Url.AbsolutePath);
                              Assert.Equal(term.Key.TermCode, request.Form?["term"]);
                          },
                          request => Assert.Contains("pageOffset=0&", request.Url.Query),
                          request => Assert.Contains("pageOffset=10&", request.Url.Query),
                          request =>
                          {
                              Assert.Equal("POST", request.Method);
                              Assert.EndsWith("/ssb/classSearch/resetDataForm", request.Url.AbsolutePath);
                          });
        Assert.Single(fetcher.Requests.Select(request => request.Session).Distinct());
    }

    [Fact]
    public async Task Class_search_behind_a_sign_in_is_reported_not_skipped()
    {
        var fetcher = new FixtureHttpFetcher("uiuc");
        // What UIUC's server answered on 2026-09-11: a redirect to its SAML sign-in page.
        fetcher.StatusOverrides["term-search.json"] = 302;
        var connector = new Banner9Connector(fetcher);

        var exception = await Assert.ThrowsAsync<ConnectorException>(
            async () => await connector.CollectSectionsAsync(Banner9Fixtures.Target("uiuc"),
                                                             Banner9Fixtures.CapturedTerm("uiuc"),
                                                             new List<CollectionGap>(),
                                                             CancellationToken.None)
                                       .ToListAsync());

        Assert.Contains("sign-in", exception.Message);
        Assert.Contains("mepCode=1UIUC", exception.SourceUrl?.Query);
        Assert.DoesNotContain(fetcher.Requests, request => request.Url.AbsolutePath.EndsWith("/searchResults"));
    }

    [Fact]
    public async Task A_window_banner_refuses_is_narrowed_so_only_the_unreturnable_record_is_missing()
    {
        // 684 sections at MSU Denver's page size of 100; record 350 is the one Banner can't return.
        var fetcher = SimulatedBanner.Fetcher(684, 350);
        var gaps = new List<CollectionGap>();

        var sections = await new Banner9Connector(fetcher).CollectSectionsAsync(PagedTarget(100),
                                                                                Banner9Fixtures.CapturedTerm("eku"),
                                                                                gaps,
                                                                                CancellationToken.None)
                                                          .ToListAsync();

        Assert.Equal(683, sections.Count);
        Assert.Equal(683, sections.Select(section => section.Payload).Distinct().Count());
        var gap = Assert.Single(gaps);
        Assert.Equal((350, 1, 684), (gap.FirstPosition, gap.Count, gap.TotalCount));
        Assert.Contains("success:false", gap.Reason);
        Assert.Contains("pageMaxSize=1&", gap.SourceUrl.Query);
        Assert.InRange(fetcher.Requests.Count(request => request.Url.AbsolutePath.EndsWith("/searchResults")), 7, 7 + 2 * 7);
        Assert.EndsWith("/resetDataForm", fetcher.Requests[^1].Url.AbsolutePath);
    }

    [Fact]
    public async Task Adjacent_unreturnable_records_merge_into_one_gap()
    {
        var gaps = new List<CollectionGap>();

        var sections = await new Banner9Connector(SimulatedBanner.Fetcher(40, 11, 12, 13)).CollectSectionsAsync(PagedTarget(20),
                                                                                                             Banner9Fixtures.CapturedTerm("eku"),
                                                                                                             gaps,
                                                                                                             CancellationToken.None)
                                                                                         .ToListAsync();

        Assert.Equal(37, sections.Count);
        var gap = Assert.Single(gaps);
        Assert.Equal((11, 3), (gap.FirstPosition, gap.Count));
    }

    [Fact]
    public async Task Recovery_stops_at_its_request_limit_and_records_the_rest_as_a_gap()
    {
        // Every record refused: narrowing can't find anything, so it must give up instead of asking for each one.
        var fetcher = SimulatedBanner.Fetcher(500, [.. Enumerable.Range(1, 500)]);
        var gaps = new List<CollectionGap>();

        var sections = await new Banner9Connector(fetcher).CollectSectionsAsync(PagedTarget(500),
                                                                                Banner9Fixtures.CapturedTerm("eku"),
                                                                                gaps,
                                                                                CancellationToken.None)
                                                          .ToListAsync();

        Assert.Empty(sections);
        Assert.Equal(500, gaps.Sum(gap => gap.Count));
        Assert.Contains(gaps, gap => gap.Reason.Contains("limit"));
        Assert.True(fetcher.Requests.Count < 50, $"{fetcher.Requests.Count} requests");
    }

    [Theory]
    [InlineData("""<a href="https://ssb.samford.edu/StudentRegistrationSsb/ssb/term/termSelection?mode=search">Class Schedule</a>""",
                "https://ssb.samford.edu/StudentRegistrationSsb/")]
    [InlineData("""<iframe src='//banner.apps.uillinois.edu/StudentRegistrationSSB/ssb/classSearch/classSearch'>""",
                "https://banner.apps.uillinois.edu/StudentRegistrationSSB/")]
    [InlineData("""<a href="/StudentRegistrationSsb/ssb/registration">Register</a>""", "https://www.example.edu/StudentRegistrationSsb/")]
    [InlineData("""var url = "https:\/\/ssb1.ccsf.edu:8105\/StudentRegistrationSsb\/ssb\/term";""",
                "https://ssb1.ccsf.edu:8105/StudentRegistrationSsb/")]
    public void Finds_banner_entry_points_in_links_iframes_and_scripts(string html, string expected)
    {
        var connector = new Banner9Connector(new FixtureHttpFetcher("eku"));

        var found = connector.FindEntryPoints(new Uri("https://www.example.edu/registrar/"), html);

        Assert.Equal(new Uri(expected), Assert.Single(found));
    }

    [Fact]
    public void Ignores_pages_that_only_mention_banner_in_text()
    {
        var connector = new Banner9Connector(new FixtureHttpFetcher("eku"));

        var found = connector.FindEntryPoints(new Uri("https://www.example.edu/"), "<p>We moved to StudentRegistrationSsb last year.</p>");

        Assert.Empty(found);
    }

    [Fact]
    public void Guesses_well_known_banner_hosts_on_the_school_domain()
    {
        var guesses = new Banner9Connector(new FixtureHttpFetcher("eku")).GuessEntryPoints(new Uri("https://go.okstate.edu/"));

        Assert.Contains(new Uri("https://ssb.okstate.edu/StudentRegistrationSsb/"), guesses);
        Assert.Contains(new Uri("https://studentregistrationssb.okstate.edu/StudentRegistrationSsb/"), guesses);
        Assert.All(guesses, guess => Assert.EndsWith(".okstate.edu", guess.Host));
    }

    [Fact]
    public async Task A_bad_page_size_in_config_is_reported()
    {
        var target = Banner9Fixtures.Target("eku");
        var misconfigured = target with
        {
            Options = new Dictionary<string, string> { [Banner9Connector.PageSizeOption] = "lots" },
        };
        var connector = new Banner9Connector(new FixtureHttpFetcher("eku"));

        await Assert.ThrowsAsync<ConnectorException>(
            async () => await connector.CollectSectionsAsync(misconfigured,
                                                             Banner9Fixtures.CapturedTerm("eku"),
                                                             new List<CollectionGap>(),
                                                             CancellationToken.None)
                                       .ToListAsync());
    }

    [Theory]
    [InlineData("https://registrationss.eku.edu/StudentRegistrationSsb/ssb/registration")]
    [InlineData("https://registrationss.eku.edu/")]
    public async Task Fingerprint_confirms_banner_9_with_a_term_probe(string candidate)
    {
        var connector = new Banner9Connector(new FixtureHttpFetcher("eku"));

        var fingerprint = await connector.FingerprintAsync(new Uri(candidate), CancellationToken.None);

        Assert.True(fingerprint.Confidence >= 0.9, $"confidence was {fingerprint.Confidence}");
        Assert.Equal(new Uri("https://registrationss.eku.edu/StudentRegistrationSsb/"), fingerprint.BaseUrl);
        Assert.Contains(fingerprint.Evidence, evidence => evidence.Contains("getTerms"));
    }

    [Fact]
    public async Task Fingerprint_rejects_a_site_without_banner_9_and_says_why()
    {
        // No fixtures exist for this school, so the probe gets HTTP 404.
        var connector = new Banner9Connector(new FixtureHttpFetcher("not-banner"));

        var fingerprint = await connector.FingerprintAsync(new Uri("https://www.example.edu/"), CancellationToken.None);

        Assert.Equal(0, fingerprint.Confidence);
        Assert.Null(fingerprint.BaseUrl);
        Assert.Contains(fingerprint.Evidence, evidence => evidence.Contains("HTTP 404"));
    }

    private static ConnectorTarget PagedTarget(int pageSize) =>
        Banner9Fixtures.Target("eku") with
        {
            Options = new Dictionary<string, string> { [Banner9Connector.PageSizeOption] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };

    #endregion Methods
}
