using DueGooder.Application;
using DueGooder.Application.Discovery;
using DueGooder.Application.Pipeline;
using DueGooder.Domain;

namespace DueGooder.Tests;

public sealed class CollectionPipelineTests
{
    #region Methods

    [Fact]
    public async Task A_school_that_fails_is_recorded_with_its_reason_and_the_others_still_run()
    {
        var connector = new ScriptedConnector { SchoolsFailingToListTerms = ["broken"] };
        var doubles = new PipelineTestDoubles();

        var run = await RunAsync(connector, doubles, new PipelineOptions(), School("broken", "a.edu"), School("fine", "b.edu"));

        Assert.True(run.Completed);
        var broken = run.Schools.Single(school => school.SchoolId is "broken");
        Assert.Equal(SchoolRunStatus.Failed, broken.Status);
        Assert.Equal("getTerms returned HTTP 500", broken.FailureReason);
        Assert.Equal(new Uri("https://a.edu/StudentRegistrationSsb/getTerms"), broken.FailureSourceUrl);
        var fine = run.Schools.Single(school => school.SchoolId is "fine");
        Assert.Equal(SchoolRunStatus.Collected, fine.Status);
        Assert.Equal(2, fine.Sections);
        Assert.Single(doubles.Upserts);
        Assert.Equal(2, doubles.Finished.Count);
    }

    [Fact]
    public async Task Schools_on_one_host_run_one_at_a_time_while_different_hosts_run_together()
    {
        var connector = new ScriptedConnector { ListingDelay = TimeSpan.FromMilliseconds(100) };

        var run = await RunAsync(connector,
                                 new PipelineTestDoubles(),
                                 new PipelineOptions(),
                                 School("a1", "a.edu"),
                                 School("a2", "a.edu"),
                                 School("b1", "b.edu"),
                                 School("b2", "b.edu"));

        Assert.All(run.Schools, school => Assert.Equal(SchoolRunStatus.Collected, school.Status));
        Assert.Equal(1, connector.MaxActivePerHost);
        Assert.Equal(2, connector.MaxActiveOverall);
        Assert.Equal(["a1", "a2", "b1", "b2"], run.Schools.Select(school => school.SchoolId));
    }

    [Fact]
    public async Task A_school_stops_after_too_many_terms_fail_in_a_row()
    {
        var codes = Enumerable.Range(1, 10).Select(number => $"t{number}").ToList();
        var connector = new ScriptedConnector { TermCodes = codes, FailingTermCodes = [.. codes] };

        var run = await RunAsync(connector,
                                 new PipelineTestDoubles(),
                                 new PipelineOptions { MaxConsecutiveTermFailures = 3 },
                                 School("signin", "a.edu"));

        var school = Assert.Single(run.Schools);
        Assert.Equal(SchoolRunStatus.Failed, school.Status);
        Assert.Equal(10, school.TermsListed);
        Assert.Equal(3, school.Terms.Count);
        Assert.StartsWith("stopped after 3 terms failed in a row; last: term/search redirected", school.FailureReason);
        Assert.Equal(new Uri("https://a.edu/StudentRegistrationSsb/term/search"), school.FailureSourceUrl);
    }

    [Fact]
    public async Task A_failed_term_makes_the_school_partial_and_stores_nothing_for_that_term()
    {
        var connector = new ScriptedConnector { TermCodes = ["t1", "t2"], FailingTermCodes = ["t2"] };
        var doubles = new PipelineTestDoubles();

        var run = await RunAsync(connector, doubles, new PipelineOptions(), School("half", "a.edu"));

        var school = Assert.Single(run.Schools);
        Assert.Equal(SchoolRunStatus.Partial, school.Status);
        Assert.Null(school.FailureReason);
        Assert.Equal(1, school.TermsCollected);
        Assert.Equal("t1", Assert.Single(doubles.Upserts).Term.Key.TermCode);
        Assert.NotNull(school.Terms.Single(term => term.TermCode is "t2").FailureReason);
    }

    [Fact]
    public async Task Sections_returned_twice_are_stored_once_and_counted_as_duplicates()
    {
        var connector = new ScriptedConnector { SectionsPerTerm = 3, RepeatFirstSection = true };
        var doubles = new PipelineTestDoubles();

        var run = await RunAsync(connector, doubles, new PipelineOptions(), School("dup", "a.edu"));

        var term = Assert.Single(Assert.Single(run.Schools).Terms);
        Assert.Equal(3, term.Sections);
        Assert.Equal(1, term.DuplicateSections);
        Assert.Equal(3, Assert.Single(doubles.Upserts).Sections.Count);
    }

    [Fact]
    public async Task Term_filters_limit_what_is_collected()
    {
        var connector = new ScriptedConnector { TermCodes = ["t1", "t2", "t3"] };

        var onlyNewest = await RunAsync(connector,
                                        new PipelineTestDoubles(),
                                        new PipelineOptions { MaxTermsPerSchool = 2 },
                                        School("s", "a.edu"));
        var onlyRequested = await RunAsync(connector,
                                           new PipelineTestDoubles(),
                                           new PipelineOptions { TermCodes = new HashSet<string> { "t3", "t9" } },
                                           School("s", "a.edu"));
        var noneListed = await RunAsync(connector,
                                        new PipelineTestDoubles(),
                                        new PipelineOptions { TermCodes = new HashSet<string> { "t9" } },
                                        School("s", "a.edu"));

        Assert.Equal(["t1", "t2"], Assert.Single(onlyNewest.Schools).Terms.Select(term => term.TermCode));
        Assert.Equal(["t3"], Assert.Single(onlyRequested.Schools).Terms.Select(term => term.TermCode));
        Assert.Equal(SchoolRunStatus.Failed, Assert.Single(noneListed.Schools).Status);
        Assert.Equal("none of the requested terms (t9) is listed", noneListed.Schools[0].FailureReason);
    }

    [Fact]
    public async Task A_school_on_a_platform_without_a_connector_fails_with_that_reason()
    {
        var doubles = new PipelineTestDoubles();
        var school = School("wd", "a.edu") with { Platform = "workday" };

        var run = await RunAsync(new ScriptedConnector(), doubles, new PipelineOptions(), school);

        Assert.Equal("no connector for platform 'workday'", Assert.Single(run.Schools).FailureReason);
    }

    [Fact]
    public async Task New_terms_are_marked_and_known_terms_are_compared_with_what_was_stored()
    {
        var connector = new ScriptedConnector { TermCodes = ["t0", "t1"] };
        var doubles = new PipelineTestDoubles();
        doubles.Stored["s"] = [new StoredTerm("t1", "Term t1", SectionCount: 2, DateTimeOffset.UnixEpoch)];

        var run = await RunAsync(connector, doubles, new PipelineOptions(), School("s", "a.edu"));

        var school = Assert.Single(run.Schools);
        Assert.Equal(SchoolRunStatus.Collected, school.Status);
        Assert.True(school.Terms.Single(term => term.TermCode is "t0").IsNew);
        var known = school.Terms.Single(term => term.TermCode is "t1");
        Assert.False(known.IsNew);
        Assert.Equal(2, known.PreviousSections);
        Assert.Equal(1, school.NewTerms);
        Assert.Empty(school.IntegrationFlags);
    }

    [Fact]
    public async Task A_collapsed_section_count_flags_a_broken_integration_and_writes_nothing()
    {
        var doubles = new PipelineTestDoubles();
        doubles.Stored["s"] = [new StoredTerm("t1", "Term t1", SectionCount: 100, DateTimeOffset.UnixEpoch)];

        var run = await RunAsync(new ScriptedConnector(), doubles, new PipelineOptions(), School("s", "a.edu"));

        var school = Assert.Single(run.Schools);
        var term = Assert.Single(school.Terms);
        Assert.True(term.IntegrationFlag);
        Assert.StartsWith("broken integration suspected: 2 sections, down 98% from 100", term.FailureReason);
        Assert.Empty(doubles.Upserts);
        Assert.Contains(school.IntegrationFlags, flag => flag.StartsWith("term t1: broken integration suspected"));
        Assert.Contains(school.IntegrationFlags, flag => flag.Contains("the stored data is kept"));
    }

    [Fact]
    public async Task A_term_that_vanished_from_the_listing_is_flagged_and_its_data_kept()
    {
        var doubles = new PipelineTestDoubles();
        doubles.Stored["s"] = [new StoredTerm("old", "Old term", SectionCount: 50, DateTimeOffset.UnixEpoch)];

        var run = await RunAsync(new ScriptedConnector(), doubles, new PipelineOptions(), School("s", "a.edu"));

        var school = Assert.Single(run.Schools);
        Assert.Equal(SchoolRunStatus.Collected, school.Status);
        Assert.Equal("term old \"Old term\" is no longer listed; its 50 stored sections are kept", Assert.Single(school.IntegrationFlags));
    }

    [Fact]
    public async Task A_term_with_a_small_recorded_gap_is_stored_with_it_and_the_school_is_partial()
    {
        var connector = new ScriptedConnector { SectionsPerTerm = 40, MissingPerTerm = 1 };
        var doubles = new PipelineTestDoubles();

        var run = await RunAsync(connector, doubles, new PipelineOptions(), School("s", "a.edu"));

        var school = Assert.Single(run.Schools);
        Assert.Equal(SchoolRunStatus.Partial, school.Status);
        var term = Assert.Single(school.Terms);
        Assert.Null(term.FailureReason);
        Assert.Equal(40, term.Sections);
        Assert.Equal(1, term.MissingSections);
        Assert.Single(Assert.Single(doubles.Upserts).Gaps);
    }

    [Fact]
    public async Task A_term_with_a_large_gap_is_not_stored()
    {
        var connector = new ScriptedConnector { SectionsPerTerm = 10, MissingPerTerm = 5 };
        var doubles = new PipelineTestDoubles();

        var run = await RunAsync(connector, doubles, new PipelineOptions(), School("s", "a.edu"));

        var term = Assert.Single(Assert.Single(run.Schools).Terms);
        Assert.StartsWith("5 of 15 sections couldn't be returned (#11-15 of 15: refused)", term.FailureReason);
        Assert.Empty(doubles.Upserts);
    }

    [Fact]
    public async Task A_homepage_only_school_no_connector_confirms_goes_to_the_review_queue_while_others_are_collected()
    {
        var doubles = new PipelineTestDoubles { Http = new RoutedHttpFetcher((_, url) => RoutedHttpFetcher.Status(url, 404)) };
        var unknown = School("unknown", "b.edu") with { Platform = null, BaseUrl = null };

        var run = await RunAsync(new ScriptedConnector(), doubles, new PipelineOptions(), School("known", "a.edu"), unknown);

        var review = run.Schools.Single(school => school.SchoolId is "unknown");
        Assert.Equal(SchoolRunStatus.NotIdentified, review.Status);
        Assert.Equal(DiscoveryMethod.NotFound, review.Discovery?.Method);
        Assert.StartsWith("not identified from the homepage: no connector recognized the site", review.FailureReason);
        Assert.Contains(review.Discovery!.Evidence, evidence => evidence.Contains("https://www.b.edu/") && evidence.Contains("HTTP 404"));
        Assert.Equal(DiscoveryMethod.Configured, run.Schools.Single(school => school.SchoolId is "known").Discovery?.Method);
        Assert.Equal("known", Assert.Single(doubles.Upserts).Term.Key.SchoolId);
    }

    private static Task<RunResult> RunAsync(ScriptedConnector connector,
                                            PipelineTestDoubles doubles,
                                            PipelineOptions options,
                                            params ListedSchool[] schools)
    {
        var connectors = new Dictionary<string, Func<IHttpFetcher, IConnector>> { [connector.Platform] = _ => connector };
        var pipeline = new CollectionPipeline(connectors,
                                              doubles,
                                              doubles,
                                              doubles,
                                              options,
                                              TimeProvider.System);
        return pipeline.RunAsync(schools, CancellationToken.None);
    }

    private static ListedSchool School(string id, string host)
    {
        var school = new School
        {
            Id = id,
            Name = id,
            Homepage = new Uri($"https://www.{host}/"),
            TimeZoneId = "America/New_York",
        };
        return new ListedSchool(school, "scripted", new Uri($"https://{host}/StudentRegistrationSsb/"), new Dictionary<string, string>());
    }

    #endregion Methods
}
