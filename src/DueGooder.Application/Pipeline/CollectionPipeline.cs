using System.Collections.Concurrent;
using DueGooder.Domain;

namespace DueGooder.Application.Pipeline;

/// <summary>
/// Runs every school in the list without human input. Hosts are collected concurrently, schools on the
/// same host one after another, and any failure is recorded against its school or term instead of
/// stopping the run.
/// </summary>
public sealed class CollectionPipeline(IReadOnlyDictionary<string, Func<IHttpFetcher, IConnector>> connectorFactories,
                                       IHttpFetcherFactory fetcherFactory,
                                       ISectionRepository repository,
                                       IRunProgress progress,
                                       PipelineOptions options,
                                       TimeProvider timeProvider)
{
    #region Methods

    public async Task<RunResult> RunAsync(IReadOnlyList<SchoolConfig> schools, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var startTimestamp = timeProvider.GetTimestamp();
        var results = new ConcurrentDictionary<int, SchoolRunResult>();

        // Queues are per host, not per school, so a server that hosts several schools still sees one at a time.
        var hostQueues = schools.Select((school, position) => (School: school, Position: position))
                                .GroupBy(entry => entry.School.Target.BaseUrl.Authority, StringComparer.OrdinalIgnoreCase)
                                .ToList();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxConcurrentHosts,
            CancellationToken = cancellationToken,
        };

        var completed = true;
        try
        {
            await Parallel.ForEachAsync(hostQueues,
                                        parallelOptions,
                                        async (queue, token) =>
                                        {
                                            foreach (var (school, position) in queue)
                                            {
                                                var result = await RunSchoolAsync(school, token);
                                                results[position] = result;
                                                progress.SchoolFinished(result);
                                            }
                                        });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completed = false;
        }

        return new RunResult(startedAt,
                             timeProvider.GetElapsedTime(startTimestamp),
                             completed,
                             results.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToList());
    }

    private async Task<SchoolRunResult> RunSchoolAsync(SchoolConfig school, CancellationToken cancellationToken)
    {
        var schoolId = school.Target.School.Id;
        var metrics = new RequestMetrics();
        var startedAt = timeProvider.GetUtcNow();
        var startTimestamp = timeProvider.GetTimestamp();
        var terms = new List<TermRunResult>();
        var termsListed = 0;
        string? failureReason = null;
        Uri? failureSourceUrl = null;
        progress.Log(schoolId, $"started ({school.Platform}, {school.Target.BaseUrl})");

        if (connectorFactories.TryGetValue(school.Platform, out var createConnector) is false)
        {
            failureReason = $"no connector for platform '{school.Platform}'";
        }
        else
        {
            try
            {
                var connector = createConnector(fetcherFactory.Create(metrics));
                var listed = await connector.ListTermsAsync(school.Target, cancellationToken);
                termsListed = listed.Count;
                var selected = SelectTerms(listed);
                progress.Log(schoolId, $"{listed.Count} terms listed, collecting {selected.Count}");
                failureReason = selected.Count is 0
                    ? NoTermsReason(listed.Count)
                    : await CollectTermsAsync(connector, school.Target, selected, terms, cancellationToken);
            }
            catch (Exception exception) when (IsRunCancellation(exception, cancellationToken) is false)
            {
                (failureReason, failureSourceUrl) = Describe(exception);
            }
        }

        var collectedCount = terms.Count(term => term.FailureReason is null);
        var lastFailedTerm = terms.LastOrDefault(term => term.FailureReason is not null);
        if (lastFailedTerm is not null)
        {
            failureReason ??= collectedCount is 0 ? $"every term failed; last: {lastFailedTerm.FailureReason}" : null;
            failureSourceUrl ??= failureReason is null ? null : lastFailedTerm.FailureSourceUrl;
        }

        var result = new SchoolRunResult
        {
            SchoolId = schoolId,
            Platform = school.Platform,
            Host = school.Target.BaseUrl.Authority,
            Status = collectedCount is 0 ? SchoolRunStatus.Failed
                   : failureReason is not null || lastFailedTerm is not null ? SchoolRunStatus.Partial
                   : SchoolRunStatus.Collected,
            FailureReason = failureReason,
            FailureSourceUrl = failureSourceUrl,
            TermsListed = termsListed,
            Terms = terms,
            Requests = metrics.Snapshot(),
            StartedAt = startedAt,
            Duration = timeProvider.GetElapsedTime(startTimestamp),
        };
        progress.Log(schoolId, Summarize(result));
        return result;
    }

    /// <returns>Why the school stopped early, or null when every selected term was attempted.</returns>
    private async Task<string?> CollectTermsAsync(IConnector connector,
                                                  ConnectorTarget target,
                                                  IReadOnlyList<Term> selected,
                                                  List<TermRunResult> results,
                                                  CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        foreach (var term in selected)
        {
            var result = await CollectTermAsync(connector, target, term, cancellationToken);
            results.Add(result);
            progress.Log(target.School.Id, Summarize(result));
            consecutiveFailures = result.FailureReason is null ? 0 : consecutiveFailures + 1;
            if (consecutiveFailures >= options.MaxConsecutiveTermFailures)
            {
                return $"stopped after {consecutiveFailures} terms failed in a row; last: {result.FailureReason}";
            }
        }

        return null;
    }

    // A term is stored only when it was collected completely: a half-collected term would look like a section drop.
    private async Task<TermRunResult> CollectTermAsync(IConnector connector,
                                                       ConnectorTarget target,
                                                       Term term,
                                                       CancellationToken cancellationToken)
    {
        var startTimestamp = timeProvider.GetTimestamp();
        try
        {
            var sectionsByKey = new Dictionary<SectionKey, Section>();
            var received = 0;
            await foreach (var raw in connector.CollectSectionsAsync(target, term, cancellationToken))
            {
                var section = connector.Map(raw);
                sectionsByKey[section.Key] = section;
                received++;
            }

            var sections = sectionsByKey.Values.ToList();
            var rowsWritten = await repository.UpsertAsync(term, sections, cancellationToken);
            return new TermRunResult(term.Key.TermCode,
                                     term.Name,
                                     sections.Count,
                                     sections.Sum(section => section.Meetings.Count),
                                     sections.Sum(section => section.Failures.Count),
                                     received - sections.Count,
                                     rowsWritten,
                                     timeProvider.GetElapsedTime(startTimestamp),
                                     FailureReason: null,
                                     FailureSourceUrl: null);
        }
        catch (Exception exception) when (IsRunCancellation(exception, cancellationToken) is false)
        {
            var (reason, sourceUrl) = Describe(exception);
            return new TermRunResult(term.Key.TermCode,
                                     term.Name,
                                     Sections: 0,
                                     Meetings: 0,
                                     ExtractionFailures: 0,
                                     DuplicateSections: 0,
                                     RowsWritten: 0,
                                     timeProvider.GetElapsedTime(startTimestamp),
                                     reason,
                                     sourceUrl);
        }
    }

    private List<Term> SelectTerms(IReadOnlyList<Term> listed)
    {
        IEnumerable<Term> selected = listed;
        if (options.TermCodes is { } termCodes)
        {
            selected = selected.Where(term => termCodes.Contains(term.Key.TermCode));
        }

        if (options.MaxTermsPerSchool is { } maxTerms)
        {
            selected = selected.Take(maxTerms);
        }

        return [.. selected];
    }

    private string NoTermsReason(int listedCount) => listedCount is 0
        ? "listed no terms"
        : $"none of the requested terms ({string.Join(", ", options.TermCodes ?? new HashSet<string>())}) is listed";

    private static bool IsRunCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static (string Reason, Uri? SourceUrl) Describe(Exception exception) => exception switch
    {
        ConnectorException connectorException => (connectorException.Message, connectorException.SourceUrl),
        _ => ($"{exception.GetType().Name}: {exception.Message}", null),
    };

    private static string Summarize(TermRunResult term) => term.FailureReason is null
        ? $"term {term.TermCode} \"{term.Name}\": {term.Sections} sections, {term.Meetings} meetings, "
          + $"{term.ExtractionFailures} extraction failures, {term.RowsWritten} rows written ({term.Duration.TotalSeconds:0.0}s)"
        : $"term {term.TermCode} \"{term.Name}\" FAILED: {term.FailureReason}"
          + (term.FailureSourceUrl is null ? "" : $" [{term.FailureSourceUrl}]");

    private static string Summarize(SchoolRunResult school) =>
        $"{school.Status}: {school.TermsCollected}/{school.Terms.Count} terms, {school.Sections} sections, "
        + $"{school.Meetings} meetings, {school.Requests.Requests} requests ({school.Requests.CacheHits} from cache), "
        + $"{school.Requests.BodyBytes / 1_048_576.0:0.0} MB, {school.Duration:hh\\:mm\\:ss}"
        + (school.FailureReason is null ? "" : $"; {school.FailureReason}");

    #endregion Methods
}
