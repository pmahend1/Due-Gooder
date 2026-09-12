using System.Globalization;
using System.Text;
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
        Line($"| Schools identified (the configured platform's connector returned a term list) | {schools.Count(school => school.TermsListed > 0)} |");
        Line($"| Schools collected (every attempted term) | {Count(schools, SchoolRunStatus.Collected)} |");
        Line($"| Schools partial (some terms failed) | {Count(schools, SchoolRunStatus.Partial)} |");
        Line($"| Schools failed (nothing collected) | {Count(schools, SchoolRunStatus.Failed)} |");
        Line($"| Terms listed / attempted / collected | {Number(schools.Sum(school => school.TermsListed))} / {Number(terms.Count)} / {Number(terms.Count(term => term.FailureReason is null))} |");
        Line($"| Sections | {Number(terms.Sum(term => term.Sections))} |");
        Line($"| Meetings | {Number(terms.Sum(term => term.Meetings))} |");
        Line($"| Extraction failures (published fields that couldn't be parsed) | {Number(terms.Sum(term => term.ExtractionFailures))} |");
        Line($"| Duplicate section records dropped | {Number(terms.Sum(term => term.DuplicateSections))} |");
        Line($"| Rows written | {Number(terms.Sum(term => term.RowsWritten))} |");

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

        RenderFieldCompleteness(Line, schools, fieldsBySchool, fieldsSource);
        RenderCost(Line, cost);

        Line();
        Line("## Human steps");
        Line();
        Line(humanSteps ?? $"No human-steps file was found at `{humanStepsPath}`.");

        RenderFailures(Line, schools);
        RenderSchools(Line, schools, fieldsBySchool);
        return text.ToString();
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
        _ when reason.Contains("no success") || reason.Contains("did not report success") =>
            "Banner answered a results page with success:false",
        _ => "other",
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
