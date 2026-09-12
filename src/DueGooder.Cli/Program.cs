using System.Globalization;
using DueGooder.Application;
using DueGooder.Application.Pipeline;
using DueGooder.Cli;
using DueGooder.Connectors.Banner9;
using DueGooder.Infrastructure.Configuration;
using DueGooder.Infrastructure.Http;
using DueGooder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

const string Usage = """
    Usage: duegooder run [--schools <path>] [--school <id>] [--term <code>] [--max-terms <n>]
                         [--db <path>] [--reports <dir>] [--cache off|use|record] [--cache-dir <dir>]
                         [--min-interval <seconds>] [--max-hosts <n>] [--human-steps <path>]
           duegooder report --run <reports/run-id.json> (see `duegooder report` for its options)
           duegooder export [--db <path>] ...                         (see `duegooder export` for its options)
           duegooder verify [--samples <path>] ...                    (see `duegooder verify` for its options)

      --schools       school list (default config/schools.yaml)
      --school        run only this school id (schools without a base_url are identified from their homepage)
      --term          collect only this term code (`--term none` identifies schools and lists terms, collecting nothing)
      --max-terms     collect only the first n terms each school lists (Banner lists newest first)
      --db            SQLite database (default data/duegooder.db)
      --reports       directory for the run's JSON results (default reports)
      --cache         dev response cache: off (default; use it for measured runs), use, or record
      --cache-dir     cache directory (default .cache/http)
      --min-interval  seconds between request starts per host (default 2; robots.txt Crawl-delay can raise it)
      --max-hosts     hosts collected at the same time (default 8)
      --human-steps   Markdown list of every human step, copied into the report (default config/human-steps.md)
    """;

string[] knownOptions =
[
    "--schools", "--school", "--term", "--max-terms", "--db", "--reports",
    "--cache", "--cache-dir", "--min-interval", "--max-hosts", "--human-steps",
];

if (args is ["report", .. var reportArgs])
{
    return ReportCommand.Execute(reportArgs);
}

if (args is ["export", .. var exportArgs])
{
    return await ExportCommand.ExecuteAsync(exportArgs);
}

if (args is ["verify", .. var verifyArgs])
{
    return await VerifyCommand.ExecuteAsync(verifyArgs);
}

if (args is not ["run", ..])
{
    Console.Error.WriteLine(Usage);
    return 1;
}

// Unknown options fail at once instead of being ignored: a typo in an unattended overnight command must not go unnoticed.
var values = new Dictionary<string, string>();
for (var index = 1; index < args.Length; index += 2)
{
    if (knownOptions.Contains(args[index]) is false || index + 1 >= args.Length)
    {
        Console.Error.WriteLine($"Unknown option or missing value: {args[index]}");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    values[args[index]] = args[index + 1];
}

var schoolsPath = values.GetValueOrDefault("--schools", "config/schools.yaml");
var databasePath = values.GetValueOrDefault("--db", "data/duegooder.db");
var reportsDirectory = values.GetValueOrDefault("--reports", "reports");

PipelineOptions pipelineOptions;
HttpFetcherOptions fetcherOptions;
try
{
    pipelineOptions = new PipelineOptions
    {
        MaxConcurrentHosts = OptionalPositive("--max-hosts") ?? 8,
        MaxTermsPerSchool = OptionalPositive("--max-terms"),
        TermCodes = values.TryGetValue("--term", out var termCode) ? new HashSet<string> { termCode } : null,
    };
    fetcherOptions = new HttpFetcherOptions
    {
        MinRequestInterval = values.TryGetValue("--min-interval", out var interval)
            ? TimeSpan.FromSeconds(ParseNonNegative("--min-interval", interval))
            : TimeSpan.FromSeconds(2),
        CacheMode = values.GetValueOrDefault("--cache", "off") switch
        {
            "off" => ResponseCacheMode.Off,
            "use" => ResponseCacheMode.Use,
            "record" => ResponseCacheMode.Record,
            var other => throw new FormatException($"--cache must be off, use or record, not '{other}'"),
        },
        CacheDirectory = values.GetValueOrDefault("--cache-dir", ".cache/http"),
        CacheKeyIgnoredParameters = Banner9Connector.SessionScopedParameters,
    };
}
catch (FormatException exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

var schools = SchoolsYamlLoader.Load(schoolsPath);
if (values.TryGetValue("--school", out var schoolId))
{
    schools = schools.Where(school => school.School.Id == schoolId).ToList();
    if (schools.Count is 0)
    {
        Console.Error.WriteLine($"No school '{schoolId}' in {schoolsPath}");
        return 1;
    }
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
var dbOptions = new DbContextOptionsBuilder<DueGooderDbContext>().UseSqlite($"Data Source={databasePath}").Options;
await using (var db = new DueGooderDbContext(dbOptions))
{
    if (await db.EnsureCurrentSchemaAsync(CancellationToken.None) is { } schemaProblem)
    {
        Console.Error.WriteLine($"{databasePath}: {schemaProblem}. Pass a new --db file; runs never migrate or delete stored data.");
        return 1;
    }
}

var timeProvider = TimeProvider.System;
var startedAt = timeProvider.GetUtcNow();
var runId = $"run-{startedAt:yyyyMMdd'T'HHmmss'Z'}";
Directory.CreateDirectory(reportsDirectory);
var settings = new Dictionary<string, string>
{
    ["schoolList"] = schoolsPath,
    ["schoolsSelected"] = schools.Count.ToString(CultureInfo.InvariantCulture),
    ["database"] = databasePath,
    ["userAgent"] = fetcherOptions.UserAgent,
    ["minRequestIntervalSeconds"] = fetcherOptions.MinRequestInterval.TotalSeconds.ToString(CultureInfo.InvariantCulture),
    ["requestTimeoutSeconds"] = fetcherOptions.RequestTimeout.TotalSeconds.ToString(CultureInfo.InvariantCulture),
    ["maxRetries"] = fetcherOptions.MaxRetries.ToString(CultureInfo.InvariantCulture),
    ["cache"] = fetcherOptions.CacheMode.ToString(),
    ["maxConcurrentHosts"] = pipelineOptions.MaxConcurrentHosts.ToString(CultureInfo.InvariantCulture),
    ["maxTermsPerSchool"] = pipelineOptions.MaxTermsPerSchool?.ToString(CultureInfo.InvariantCulture) ?? "all",
    ["termCodes"] = pipelineOptions.TermCodes is null ? "all" : string.Join(",", pipelineOptions.TermCodes),
    ["maxConsecutiveTermFailures"] = pipelineOptions.MaxConsecutiveTermFailures.ToString(CultureInfo.InvariantCulture),
    ["discoveryMaxPagesPerSchool"] = pipelineOptions.Discovery.MaxPages.ToString(CultureInfo.InvariantCulture),
    ["discoveryMaxLinkDepth"] = pipelineOptions.Discovery.MaxDepth.ToString(CultureInfo.InvariantCulture),
    ["discoveryMinConfidence"] = pipelineOptions.Discovery.MinConfidence.ToString(CultureInfo.InvariantCulture),
    ["maxSectionDrop"] = pipelineOptions.MaxSectionDrop.ToString(CultureInfo.InvariantCulture),
    ["maxGapShare"] = pipelineOptions.MaxGapShare.ToString(CultureInfo.InvariantCulture),
};
var report = new RunReportFile(Path.Combine(reportsDirectory, runId + ".json"), runId, settings);
var progress = new ConsoleRunProgress(report, startedAt, timeProvider);

using var fetchers = new HttpFetcherFactory(fetcherOptions, timeProvider);
var connectors = new Dictionary<string, Func<IHttpFetcher, IConnector>>
{
    ["banner9"] = fetcher => new Banner9Connector(fetcher),
};
var pipeline = new CollectionPipeline(connectors,
                                      fetchers,
                                      new EfSectionRepository(() => new DueGooderDbContext(dbOptions)),
                                      progress,
                                      pipelineOptions,
                                      timeProvider);

// The first Ctrl+C stops the run cleanly and still writes results; a second one kills the process.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, keyPress) =>
{
    keyPress.Cancel = cancellation.IsCancellationRequested is false;
    cancellation.Cancel();
};

progress.WriteLine($"{runId}: {schools.Count} schools from {schoolsPath}; cache {fetcherOptions.CacheMode}; "
                   + $"{fetcherOptions.MinRequestInterval.TotalSeconds}s between requests per host; "
                   + $"up to {pipelineOptions.MaxConcurrentHosts} hosts at once");
var run = await pipeline.RunAsync(schools, cancellation.Token);
var cost = RunCostEstimate.From(run, pipelineOptions.MaxConcurrentHosts, ReportCommand.FileSize(databasePath));
report.Write(run, cost);
var markdownPath = RunReportMarkdown.WriteFile(report.FilePath,
                                               new RunReportDocument(runId, settings, run, cost),
                                               run.Schools.ToDictionary(school => school.SchoolId, school => school.Fields),
                                               "counted by this run",
                                               values.GetValueOrDefault("--human-steps", ReportCommand.DefaultHumanStepsPath));
PrintSummary(progress, run, report.FilePath);
progress.WriteLine($"Report: {markdownPath}");
return 0;

int? OptionalPositive(string name)
{
    if (values.TryGetValue(name, out var text) is false)
    {
        return null;
    }

    return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
        ? value
        : throw new FormatException($"{name} must be a positive whole number, not '{text}'");
}

static double ParseNonNegative(string name, string text) =>
    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0
        ? value
        : throw new FormatException($"{name} must be a number of seconds, not '{text}'");

static void PrintSummary(ConsoleRunProgress progress, RunResult run, string reportPath)
{
    progress.WriteLine($"Run {(run.Completed ? "finished" : "CANCELLED")} in {run.Duration:hh\\:mm\\:ss}; results in {reportPath}");
    progress.WriteLine($"{"school",-14} {"status",-9} {"terms",7} {"sections",9} {"meetings",9} {"requests",9} {"MB",8} {"time",9}  reason");
    foreach (var school in run.Schools)
    {
        progress.WriteLine($"{school.SchoolId,-14} {school.Status,-9} {$"{school.TermsCollected}/{school.Terms.Count}",7} "
                           + $"{school.Sections,9} {school.Meetings,9} {school.Requests.Requests,9} "
                           + $"{school.Requests.BodyBytes / 1_048_576.0,8:0.0} {school.Duration,9:hh\\:mm\\:ss}  {school.FailureReason}");
    }

    progress.WriteLine($"Schools: {run.Schools.Count(school => school.Status is SchoolRunStatus.Collected)} collected, "
                       + $"{run.Schools.Count(school => school.Status is SchoolRunStatus.Partial)} partial, "
                       + $"{run.Schools.Count(school => school.Status is SchoolRunStatus.Failed)} failed. "
                       + $"Sections: {run.Schools.Sum(school => school.Sections)}. "
                       + $"Meetings: {run.Schools.Sum(school => school.Meetings)}. "
                       + $"Requests: {run.Schools.Sum(school => school.Requests.Requests)}. "
                       + $"Body MB: {run.Schools.Sum(school => school.Requests.BodyBytes) / 1_048_576.0:0.0}.");
}
