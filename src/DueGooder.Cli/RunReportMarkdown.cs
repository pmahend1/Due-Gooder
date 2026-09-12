using System.Globalization;
using System.Text;
using DueGooder.Application.Discovery;
using DueGooder.Application.Pipeline;

namespace DueGooder.Cli;

/// <summary>The human-readable run report, <c>reports/&lt;run-id&gt;.md</c>, rendered from the same data as the JSON.</summary>
internal static class RunReportMarkdown
{
    #region State

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    #endregion State

    #region Methods

    /// <summary>Renders the report and writes it next to <paramref name="jsonPath"/> with an <c>.md</c> extension.</summary>
    /// <returns>The path written.</returns>
    public static string WriteFile(string jsonPath,
                                   RunReportDocument document,
                                   IReadOnlyDictionary<string, FieldCompleteness> fieldsBySchool,
                                   string fieldsSource,
                                   string humanStepsPath)
    {
        var humanSteps = File.Exists(humanStepsPath) ? File.ReadAllText(humanStepsPath).Trim() : null;
        var path = Path.ChangeExtension(jsonPath, ".md");
        File.WriteAllText(path, Render(document, fieldsBySchool, fieldsSource, humanSteps, humanStepsPath));
        return path;
    }

    private static string Render(RunReportDocument document,
                                 IReadOnlyDictionary<string, FieldCompleteness> fieldsBySchool,
                                 string fieldsSource,
                                 string? humanSteps,
                                 string humanStepsPath)
    {
        var run = document.Run;
        var schools = run.Schools;
        var terms = schools.SelectMany(school => school.Terms).ToList();
        var cost = document.Cost ?? RunCostEstimate.From(run, concurrentHosts: 1, databaseBytes: null);
        var text = new StringBuilder();

        void Line(string line = "") => text.AppendLine(line);

        Line($"# Run report: {document.RunId}");
        Line();
        Line($"Started {run.StartedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC, ran for {Duration(run.Duration)} wall clock, "
             + (run.Completed ? "and finished every school." : "and was **cancelled** before every school finished.")
             + $" Machine-readable results: `{document.RunId}.json`.");
        Line();
        Line("| Setting | Value |");
        Line("| --- | --- |");
        foreach (var (name, value) in document.Settings)
        {
            Line($"| {name} | `{Cell(value)}` |");
        }

        Line();
        Line("## Summary");
        Line();
        Line("| | Count |");
        Line("| --- | ---: |");
        Line($"| Schools attempted | {schools.Count} |");
        Line($"| Schools identified (platform confirmed by config or by discovery) | {schools.Count(IsIdentified)} |");
        Line($"| … found from the homepage alone | {schools.Count(school => school.Discovery is { Identified: true, Method: not DiscoveryMethod.Configured })} |");
        Line($"| … platform and base_url set in config | {schools.Count(school => school.Discovery?.Method is DiscoveryMethod.Configured)} |");
        Line($"| Schools in the manual review queue (not identified) | {Count(schools, SchoolRunStatus.NotIdentified)} |");
        Line($"| Schools collected (every attempted term, complete) | {Count(schools, SchoolRunStatus.Collected)} |");
        Line($"| Schools partial (some terms failed or stored with a gap) | {Count(schools, SchoolRunStatus.Partial)} |");
        Line($"| Schools failed (identified, nothing collected) | {Count(schools, SchoolRunStatus.Failed)} |");
        Line($"| Terms listed / attempted / collected | {Number(schools.Sum(school => school.TermsListed))} / {Number(terms.Count)} / {Number(terms.Count(term => term.FailureReason is null))} |");
        Line($"| Sections | {Number(terms.Sum(term => term.Sections))} |");
        Line($"| Meetings | {Number(terms.Sum(term => term.Meetings))} |");
        Line($"| Extraction failures (published fields that couldn't be parsed) | {Number(terms.Sum(term => term.ExtractionFailures))} |");
        Line($"| Duplicate section records dropped | {Number(terms.Sum(term => term.DuplicateSections))} |");
        Line($"| Rows written for content changes | {Number(terms.Sum(term => term.RowsWritten))} |");
        Line($"| New terms (not in the database before this run) | {Number(terms.Count(term => term.IsNew && term.FailureReason is null))} |");
        Line($"| Broken-integration flags | {Number(schools.Sum(school => school.IntegrationFlags.Count))} |");

        var collectedSchools = schools.Where(school => school.Status is not SchoolRunStatus.Failed).ToList();
        var statusCounts = schools.SelectMany(school => school.Requests.StatusCounts)
                                  .GroupBy(pair => pair.Key)
                                  .OrderBy(group => group.Key)
                                  .Select(group => $"{group.Key}: {Number(group.Sum(pair => pair.Value))}");
        Line();
        Line("## Runtime and requests");
        Line();
        Line("| | Value |");
        Line("| --- | ---: |");
        Line($"| Wall clock | {Duration(run.Duration)} |");
        Line($"| School time added up (schools on different hosts ran at the same time) | {Duration(TimeSpan.FromTicks(schools.Sum(school => school.Duration.Ticks)))} |");
        if (collectedSchools.Count > 0)
        {
            Line($"| Average per collected school | {Duration(TimeSpan.FromTicks((long)collectedSchools.Average(school => school.Duration.Ticks)))} |");
        }

        Line($"| Requests sent (retries and robots.txt included, cache hits excluded) | {Number(schools.Sum(school => school.Requests.Requests))} |");
        Line($"| Retries / network errors | {Number(schools.Sum(school => school.Requests.Retries))} / {Number(schools.Sum(school => school.Requests.NetworkErrors))} |");
        Line($"| Response bodies received (after decompression) | {Megabytes(schools.Sum(school => school.Requests.BodyBytes))} |");
        Line($"| Requests answered from the dev cache | {Number(schools.Sum(school => school.Requests.CacheHits))} |");
        Line($"| HTTP status counts | {string.Join(", ", statusCounts)} |");
        Line();
        Line($"Politeness: one request in flight per host, at least {document.Settings.GetValueOrDefault("minRequestIntervalSeconds", "?")} s "
             + "between request starts (robots.txt Crawl-delay can raise it), robots.txt honored, identifying User-Agent "
             + $"`{document.Settings.GetValueOrDefault("userAgent", "?")}`.");

        RenderDiscovery(Line, schools);
        RenderRefresh(Line, schools);
        RenderFieldCompleteness(Line, schools, fieldsBySchool, fieldsSource);
        RenderCost(Line, cost);

        Line();
        Line("## Human steps");
        Line();
        Line(humanSteps ?? $"No human-steps file was found at `{humanStepsPath}`.");

        RenderFailures(Line, schools);
        RenderReviewQueue(Line, schools);
        RenderSchools(Line, schools, fieldsBySchool);
        return text.ToString();
    }

    private static void RenderDiscovery(Action<string> line, IReadOnlyList<SchoolRunResult> schools)
    {
        var withDiscovery = schools.Where(school => school.Discovery is not null).ToList();
        if (withDiscovery.Count is 0)
        {
            return;
        }

        line("");
        line("## Discovery");
        line("");
        line("Schools without a `base_url` in config are identified from their homepage alone: the homepage and up to "
             + "a few schedule or registrar pages of the school's site are scanned for each connector's URL patterns and HTML "
             + "markers; failing that, host names the platform commonly uses are probed. A connector's probe request has to "
             + "confirm the candidate. Evidence for every school is in the JSON; unconfirmed schools are listed with theirs "
             + "under *Manual review queue*.");
        line("");
        line("| How | Schools |");
        line("| --- | ---: |");
        foreach (var group in withDiscovery.GroupBy(school => school.Discovery!.Method).OrderBy(group => group.Key))
        {
            line($"| {MethodLabel(group.Key)} | {group.Count()} ({string.Join(", ", group.Select(school => school.SchoolId))}) |");
        }

        var discovered = withDiscovery.Where(school => school.Discovery!.Method is not DiscoveryMethod.Configured).ToList();
        if (discovered.Count is 0)
        {
            return;
        }

        line("");
        line("| School | Found by | Platform | Base URL | Pages | Probes | Time | Confirming evidence |");
        line("| --- | --- | --- | --- | ---: | ---: | ---: | --- |");
        foreach (var school in discovered)
        {
            var discovery = school.Discovery!;
            var confirming = discovery.Identified
                ? discovery.Evidence.LastOrDefault(evidence => evidence.Contains("probe:")) ?? ""
                : "";
            line($"| {school.SchoolId} | {MethodLabel(discovery.Method)} | {discovery.Platform ?? "—"} | "
                 + $"{Cell(discovery.BaseUrl?.ToString() ?? "—")} | {discovery.PagesFetched} | {discovery.Probes} | "
                 + $"{Duration(discovery.Duration)} | {Cell(confirming)} |");
        }
    }

    private static void RenderRefresh(Action<string> line, IReadOnlyList<SchoolRunResult> schools)
    {
        var stored = schools.SelectMany(school => school.Terms.Where(term => term.Changes is not null)
                                                              .Select(term => (school.SchoolId, Term: term)))
                            .ToList();
        var flagged = schools.Where(school => school.IntegrationFlags.Count > 0).ToList();
        if (stored.Count is 0 && flagged.Count is 0)
        {
            return;
        }

        line("");
        line("## Refresh");
        line("");
        line("Every stored term is compared with what earlier runs stored. Sections are upserted by natural key (school + term + "
             + "course + CRN), so a re-run never duplicates. An unchanged section keeps its source URL and `retrieved_at` and "
             + "only its `last_confirmed_at` moves, so a re-run with no source changes writes no content rows. Nothing is "
             + "ever deleted: a section the source no longer lists stops being confirmed and goes stale.");
        line("");
        line("| | Count |");
        line("| --- | ---: |");
        line($"| Terms stored | {Number(stored.Count)} |");
        line($"| … new (not in the database before) | {Number(stored.Count(entry => entry.Term.IsNew))} |");
        line($"| Sections added | {Number(stored.Sum(entry => entry.Term.Changes!.Added))} |");
        line($"| Sections changed | {Number(stored.Sum(entry => entry.Term.Changes!.Changed))} |");
        line($"| Sections unchanged (only `last_confirmed_at` moved) | {Number(stored.Sum(entry => entry.Term.Changes!.Unchanged))} |");
        line($"| Stored sections not seen this run (kept, going stale) | {Number(stored.Sum(entry => entry.Term.Changes!.NotSeen))} |");
        line($"| Rows written for content changes | {Number(stored.Sum(entry => entry.Term.RowsWritten))} |");

        var gapped = stored.Where(entry => entry.Term.MissingSections > 0).ToList();
        if (gapped.Count > 0)
        {
            line("");
            line("Terms stored with a recorded gap (the platform listed these records but wouldn't return them):");
            line("");
            line("| School | Term | Stored | Missing | Which | Reason |");
            line("| --- | --- | ---: | ---: | --- | --- |");
            foreach (var (schoolId, term) in gapped)
            {
                var which = string.Join(", ", term.Gaps!.Select(gap => gap.Count is 1
                                                                           ? $"#{gap.FirstPosition}"
                                                                           : $"#{gap.FirstPosition}-{gap.FirstPosition + gap.Count - 1}"));
                line($"| {schoolId} | {term.TermCode} | {Number(term.Sections)} | {Number(term.MissingSections)} | "
                     + $"{which} of {Number(term.Gaps![0].TotalCount)} | {Cell(term.Gaps[0].Reason)} |");
            }
        }

        line("");
        line("Broken-integration flags (stored data is kept for every one of them):");
        line("");
        if (flagged.Count is 0)
        {
            line("None.");
            return;
        }

        line("| School | Flag |");
        line("| --- | --- |");
        foreach (var school in flagged)
        {
            foreach (var flag in school.IntegrationFlags)
            {
                line($"| {school.SchoolId} | {Cell(flag)} |");
            }
        }
    }

    private static void RenderReviewQueue(Action<string> line, IReadOnlyList<SchoolRunResult> schools)
    {
        var queue = schools.Where(school => school.Status is SchoolRunStatus.NotIdentified).ToList();
        if (queue.Count is 0)
        {
            return;
        }

        line("");
        line("## Manual review queue");
        line("");
        line("No connector confirmed these schools from their homepage. Each one is kept with every step of evidence, "
             + "so a person can add a `base_url` (if the platform is supported but hidden) or pick the next connector to build.");
        foreach (var school in queue)
        {
            var discovery = school.Discovery!;
            line("");
            line($"### {school.SchoolId}");
            line("");
            line($"{school.FailureReason}");
            line("");
            if (discovery.PlatformHints.Count > 0)
            {
                line($"Platforms without a connector seen on its pages: {string.Join("; ", discovery.PlatformHints.Select(hint => $"`{hint}`"))}");
                line("");
            }

            foreach (var evidence in discovery.Evidence)
            {
                line($"- {evidence.ReplaceLineEndings(" ")}");
            }
        }
    }

    private static void RenderFieldCompleteness(Action<string> line,
                                                IReadOnlyList<SchoolRunResult> schools,
                                                IReadOnlyDictionary<string, FieldCompleteness> fieldsBySchool,
                                                string fieldsSource)
    {
        var total = schools.Aggregate(FieldCompleteness.Empty,
                                      (sum, school) => sum + fieldsBySchool.GetValueOrDefault(school.SchoolId, FieldCompleteness.Empty));
        line("");
        line("## Field completeness");
        line("");
        if (total.Sections is 0)
        {
            line("Not recorded: this run's results predate field-completeness counting.");
            return;
        }

        line($"Share of collected sections, and of their meetings, that carry each field ({fieldsSource}). A missing field means "
             + "the school doesn't publish it, or publishes only a placeholder such as TBA or no set days. Published fields "
             + "that couldn't be parsed are counted as extraction failures in the summary instead.");
        line("");
        line("| Field | Present | Of | Share |");
        line("| --- | ---: | ---: | ---: |");
        foreach (var (name, present, of) in new (string, int, int)[]
                 {
                     ("Section: title", total.WithTitle, total.Sections),
                     ("Section: credits (fixed or range)", total.WithCredits, total.Sections),
                     ("Section: instructional method", total.WithInstructionalMethod, total.Sections),
                     ("Section: campus", total.WithCampus, total.Sections),
                     ("Section: capacity", total.WithCapacity, total.Sections),
                     ("Section: enrollment", total.WithEnrollment, total.Sections),
                     ("Section: at least one instructor", total.WithInstructor, total.Sections),
                     ("Section: at least one meeting", total.WithMeeting, total.Sections),
                     ("Section: cross-listed (for information)", total.CrossListed, total.Sections),
                     ("Meeting: set days", total.WithDays, total.Meetings),
                     ("Meeting: start and end time", total.WithTimes, total.Meetings),
                     ("Meeting: building or room (not a placeholder)", total.WithLocation, total.Meetings),
                     ("Meeting: start and end date", total.WithDates, total.Meetings),
                 })
        {
            line($"| {name} | {Number(present)} | {Number(of)} | {Percent(present, of)} |");
        }
    }

    private static void RenderCost(Action<string> line, RunCostEstimate cost)
    {
        line("");
        line("## Cost");
        line("");
        line("Inputs marked **measured** come from this run. Every price, the machine choice and the 1,000-school "
             + "extrapolation are **estimates**. Prices are public list prices checked in September 2026.");
        line("");
        line("```text");
        line("run cost          = VM $/hour × wall-clock hours");
        line("                  + inbound $/GB × response GB");
        line("                  + (input tokens × input $/Mtok + output tokens × output $/Mtok) ÷ 1,000,000");
        line("cost per school   = run cost ÷ schools collected");
        line("hours per 1,000   = (collected-school hours ÷ schools collected) × 1,000 ÷ hosts at once       [estimate]");
        line("cost per 1,000    = hours per 1,000 × VM $/hour + (transfer + LLM $ ÷ schools collected) × 1,000  [estimate]");
        line("storage per month = database GB × storage $/GB-month (kept separate: it's per month, not per run)");
        line("```");
        line("");
        line("| Input | Value | Kind | Source |");
        line("| --- | ---: | --- | --- |");
        line($"| Wall-clock hours | {cost.WallClockHours:0.000} | measured | this run |");
        line($"| Collected-school hours (added up) | {cost.CollectedSchoolHours:0.000} | measured | this run |");
        line($"| Schools collected / attempted | {cost.SchoolsCollected} / {cost.SchoolsAttempted} | measured | this run |");
        line($"| Hosts at once | {cost.ConcurrentHosts} | measured (setting) | this run |");
        line($"| Response GB (10^9 bytes) | {cost.BodyGb:0.000} | measured | this run |");
        line($"| Database GB | {(cost.DatabaseGb is { } gb ? gb.ToString("0.000", Invariant) : "not found")} | measured | database file size when the report was written |");
        line($"| LLM input / output tokens | {cost.LlmInputTokens} / {cost.LlmOutputTokens} | measured | no LLM fallback exists yet, so none were spent |");
        line($"| VM $/hour | {cost.VmDollarsPerHour} | estimate | [AWS EC2 on-demand, t4g.small, us-east-1](https://aws.amazon.com/ec2/pricing/on-demand/) |");
        line($"| Inbound $/GB | {cost.InboundDollarsPerGb} | estimate | [AWS: data transfer in from the internet is free](https://aws.amazon.com/ec2/pricing/on-demand/) |");
        line($"| Storage $/GB-month | {cost.StorageDollarsPerGbMonth} | estimate | [Amazon EBS gp3, us-east-1](https://aws.amazon.com/ebs/pricing/) |");
        line($"| LLM $/Mtok in / out | {cost.LlmInputDollarsPerMillionTokens} / {cost.LlmOutputDollarsPerMillionTokens} | estimate | [Claude Haiku 4.5](https://platform.claude.com/docs/en/about-claude/pricing) |");
        line("");
        line("| Result | Value |");
        line("| --- | ---: |");
        line($"| Compute | {Dollars(cost.ComputeDollars)} |");
        line($"| Transfer | {Dollars(cost.TransferDollars)} |");
        line($"| LLM | {Dollars(cost.LlmDollars)} |");
        line($"| **This run** | **{Dollars(cost.RunDollars)}** |");
        line($"| Per collected school | {Dollars(cost.DollarsPerCollectedSchool)} |");
        line($"| Storage per month | {Dollars(cost.StorageDollarsPerMonth)} |");
        line($"| 1,000 schools: wall-clock hours (estimate) | {(cost.HoursPerThousandSchools is { } hours ? hours.ToString("0.0", Invariant) : "n/a")} |");
        line($"| 1,000 schools: cost per run (estimate) | {Dollars(cost.DollarsPerThousandSchools)} |");
        line($"| 1,000 schools: storage per month (estimate) | {Dollars(cost.StorageDollarsPerMonthPerThousandSchools)} |");
        line("");
        line("Runtime is set by the per-host politeness delay, not by CPU, so the wall-clock time measured here stands in for "
             + "the VM's. That a t4g.small keeps up with this many hosts at once is an estimate; it wasn't measured on one.");
    }

    private static void RenderFailures(Action<string> line, IReadOnlyList<SchoolRunResult> schools)
    {
        var troubled = schools.Where(school => school.Status is not SchoolRunStatus.Collected).ToList();
        var failedTerms = schools.SelectMany(school => school.Terms
                                                             .Where(term => term.FailureReason is not null)
                                                             .Select(term => (school.SchoolId, Term: term)))
                                 .ToList();
        line("");
        line("## Failures and their reasons");
        line("");
        if (troubled.Count is 0 && failedTerms.Count is 0)
        {
            line("None.");
            return;
        }

        line("| Cause | Schools |");
        line("| --- | ---: |");
        foreach (var cause in troubled.GroupBy(school => Cause(RepresentativeReason(school))).OrderByDescending(group => group.Count()))
        {
            line($"| {cause.Key} | {cause.Count()} ({string.Join(", ", cause.Select(school => school.SchoolId))}) |");
        }

        line("");
        line("| School | Status | Terms collected | Reason | Request |");
        line("| --- | --- | ---: | --- | --- |");
        foreach (var school in troubled)
        {
            line($"| {school.SchoolId} | {school.Status} | {school.TermsCollected}/{school.Terms.Count} | "
                 + $"{Cell(RepresentativeReason(school))} | {Cell(school.FailureSourceUrl?.ToString() ?? "")} |");
        }

        if (failedTerms.Count is 0)
        {
            return;
        }

        line("");
        line("Terms that failed (nothing is stored for a failed term):");
        line("");
        line("| School | Term | Name | Reason | Request |");
        line("| --- | --- | --- | --- | --- |");
        foreach (var (schoolId, term) in failedTerms)
        {
            line($"| {schoolId} | {term.TermCode} | {Cell(term.Name ?? "")} | {Cell(term.FailureReason!)} | "
                 + $"{Cell(term.FailureSourceUrl?.ToString() ?? "")} |");
        }
    }

    private static void RenderSchools(Action<string> line,
                                      IReadOnlyList<SchoolRunResult> schools,
                                      IReadOnlyDictionary<string, FieldCompleteness> fieldsBySchool)
    {
        line("");
        line("## Per school");
        line("");
        line("| School | Status | Terms (collected/attempted/listed) | Sections | Meetings | Extraction failures | Requests | MB | Time | Credits | Times |");
        line("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var school in schools)
        {
            var fields = fieldsBySchool.GetValueOrDefault(school.SchoolId, FieldCompleteness.Empty);
            line($"| {school.SchoolId} | {school.Status} | {school.TermsCollected}/{school.Terms.Count}/{school.TermsListed} | "
                 + $"{Number(school.Sections)} | {Number(school.Meetings)} | {Number(school.ExtractionFailures)} | "
                 + $"{Number(school.Requests.Requests)} | {school.Requests.BodyBytes / 1_048_576.0:0.0} | {Duration(school.Duration)} | "
                 + $"{Percent(fields.WithCredits, fields.Sections)} | {Percent(fields.WithTimes, fields.Meetings)} |");
        }
    }

    private static string RepresentativeReason(SchoolRunResult school) =>
        school.FailureReason
        ?? school.Terms.FirstOrDefault(term => term.FailureReason is not null)?.FailureReason
        ?? "";

    // Groups reasons for the summary table only; the full reason is always printed next to it.
    private static string Cause(string reason) => reason switch
    {
        _ when reason.Contains("robots.txt") && reason.Contains("disallows") => "robots.txt disallows crawling",
        _ when reason.Contains("robots.txt") && reason.Contains("could not be read") =>
            "robots.txt unreadable (5xx or dropped connection), which RFC 9309 treats as disallow-all",
        _ when reason.Contains("sign-in") => "class search needs a sign-in",
        _ when reason.Contains("broken integration") => "broken integration suspected (section count collapsed)",
        _ when reason.Contains("success:false") || reason.Contains("no success") || reason.Contains("did not report success") =>
            "Banner answered a results page with success:false",
        _ when reason.StartsWith("not identified") && reason.Contains("pages point to") =>
            "not identified: pages point to a platform without a connector",
        _ when reason.StartsWith("not identified") => "not identified from the homepage",
        _ => "other",
    };

    // Runs from before discovery have no Discovery; there, a listed term is what showed the platform was right.
    private static bool IsIdentified(SchoolRunResult school) => school.Discovery?.Identified ?? school.TermsListed > 0;

    private static string MethodLabel(DiscoveryMethod method) => method switch
    {
        DiscoveryMethod.Configured => "platform and base_url in config",
        DiscoveryMethod.HomepageLink => "link or marker on the homepage",
        DiscoveryMethod.CrawledLink => "link on a schedule or registrar page",
        DiscoveryMethod.GuessedHost => "well-known host name probed",
        _ => "not found (review queue)",
    };

    private static int Count(IEnumerable<SchoolRunResult> schools, SchoolRunStatus status) =>
        schools.Count(school => school.Status == status);

    private static string Cell(string value) => value.Replace("|", "\\|").ReplaceLineEndings(" ");

    private static string Number(long value) => value.ToString("N0", Invariant);

    private static string Percent(int part, int whole) =>
        whole is 0 ? "n/a" : (part / (double)whole).ToString("0.0%", Invariant);

    private static string Megabytes(long bytes) => $"{bytes / 1_048_576.0:N1} MB";

    private static string Dollars(decimal? value) => value is { } dollars ? dollars.ToString("$0.0000", Invariant) : "n/a";

    private static string Duration(TimeSpan duration) => duration.ToString(@"hh\:mm\:ss", Invariant);

    #endregion Methods
}
