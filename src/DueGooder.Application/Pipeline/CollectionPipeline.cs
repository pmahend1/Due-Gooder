using System.Collections.Concurrent;
using DueGooder.Application.Discovery;
using DueGooder.Domain;

namespace DueGooder.Application.Pipeline;

/// <summary>
/// Runs every school in the list without human input. Schools without a configured platform are identified from their
/// homepage first; identified schools are then collected with hosts running concurrently and schools on the same host
/// one after another. Every run is compared with what earlier runs stored, and any failure is recorded against its
/// school or term instead of stopping the run.
/// </summary>
public sealed class CollectionPipeline(IReadOnlyDictionary<string, Func<IHttpFetcher, IConnector>> connectorFactories,
                                       IHttpFetcherFactory fetcherFactory,
                                       ISectionRepository repository,
                                       IRunProgress progress,
                                       PipelineOptions options,
                                       TimeProvider timeProvider)
{
    #region State

    private const string UnidentifiedPlatform = "unidentified";

    #endregion State

    #region Methods

    public async Task<RunResult> RunAsync(IReadOnlyList<ListedSchool> schools, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var startTimestamp = timeProvider.GetTimestamp();
        var results = new ConcurrentDictionary<int, SchoolRunResult>();
        var identified = new ConcurrentDictionary<int, (SchoolConfig Config, DiscoveryResult Discovery, RequestMetrics Metrics)>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.MaxConcurrentHosts,
            CancellationToken = cancellationToken,
        };

        var completed = true;
        try
        {
            // Identification comes first because collection queues schools by the platform host that discovery finds.
            await Parallel.ForEachAsync(schools.Select((school, position) => (School: school, Position: position)),
                                        parallelOptions,
                                        async (entry, token) =>
                                        {
                                            var metrics = new RequestMetrics();
                                            var discovery = await IdentifyAsync(entry.School, metrics, token);
                                            if (discovery.Identified)
                                            {
                                                var target = new ConnectorTarget(entry.School.School, discovery.BaseUrl!)
                                                {
                                                    Options = entry.School.Options,
                                                };
                                                identified[entry.Position] = (new SchoolConfig(discovery.Platform!, target), discovery, metrics);
                                                return;
                                            }

                                            var result = await NotIdentifiedAsync(entry.School, discovery, metrics, token);
                                            results[entry.Position] = result;
                                            progress.SchoolFinished(result);
                                        });

            // Queues are per host, not per school, so a server that hosts several schools still sees one at a time.
            var hostQueues = identified.OrderBy(pair => pair.Key)
                                       .GroupBy(pair => pair.Value.Config.Target.BaseUrl.Authority, StringComparer.OrdinalIgnoreCase)
                                       .ToList();
            await Parallel.ForEachAsync(hostQueues,
                                        parallelOptions,
                                        async (queue, token) =>
                                        {
                                            foreach (var (position, school) in queue)
                                            {
                                                var result = await RunSchoolAsync(school.Config, school.Discovery, school.Metrics, token);
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

    private async Task<DiscoveryResult> IdentifyAsync(ListedSchool school, RequestMetrics metrics, CancellationToken cancellationToken)
    {
        if (school.IsConfigured)
        {
            return DiscoveryResult.FromConfig(school.Platform!, school.BaseUrl!);
        }

        var schoolId = school.School.Id;
        var startTimestamp = timeProvider.GetTimestamp();
        progress.Log(schoolId, $"identifying the platform from {school.School.Homepage}");
        DiscoveryResult discovery;
        try
        {
            var fetcher = fetcherFactory.Create(metrics);
            var connectors = connectorFactories.Values.Select(create => create(fetcher)).ToList();
            discovery = await new HomepageDiscovery(connectors, fetcher, options.Discovery).DiscoverAsync(school.School.Homepage,
                                                                                                          cancellationToken);
        }
        catch (Exception exception) when (IsRunCancellation(exception, cancellationToken) is false)
        {
            var (reason, _) = Describe(exception);
            discovery = new DiscoveryResult
            {
                Method = DiscoveryMethod.NotFound,
                Evidence = [reason],
                ReviewReason = $"not identified from the homepage: discovery stopped with {reason}",
            };
        }

        discovery = discovery with { Duration = timeProvider.GetElapsedTime(startTimestamp) };
        progress.Log(schoolId,
                     discovery.Identified
                         ? $"identified {discovery.Platform} at {discovery.BaseUrl} ({discovery.Method}; {discovery.PagesFetched} pages, "
                           + $"{discovery.Probes} probes, {discovery.Duration.TotalSeconds:0.0}s)"
                         : $"{discovery.ReviewReason} ({discovery.PagesFetched} pages, {discovery.Probes} probes)");
        return discovery;
    }

    private async Task<SchoolRunResult> NotIdentifiedAsync(ListedSchool school,
                                                           DiscoveryResult discovery,
                                                           RequestMetrics metrics,
                                                           CancellationToken cancellationToken)
    {
        var stored = await repository.GetStoredTermsAsync(school.School.Id, cancellationToken);
        return new SchoolRunResult
        {
            SchoolId = school.School.Id,
            Platform = UnidentifiedPlatform,
            Host = school.School.Homepage.Authority,
            Status = SchoolRunStatus.NotIdentified,
            FailureReason = discovery.ReviewReason,
            Discovery = discovery,
            IntegrationFlags = stored.Count is 0
                ? []
                : [$"earlier runs stored {stored.Count} terms, but the school wasn't identified this run; the stored data is kept"],
            TermsListed = 0,
            Terms = [],
            Requests = metrics.Snapshot(),
            StartedAt = timeProvider.GetUtcNow() - discovery.Duration,
            Duration = discovery.Duration,
        };
    }

    private async Task<SchoolRunResult> RunSchoolAsync(SchoolConfig school,
                                                       DiscoveryResult discovery,
                                                       RequestMetrics metrics,
                                                       CancellationToken cancellationToken)
    {
        var schoolId = school.Target.School.Id;
        var startedAt = timeProvider.GetUtcNow();
        var startTimestamp = timeProvider.GetTimestamp();
        var terms = new List<TermRunResult>();
        var flags = new List<string>();
        IReadOnlyList<StoredTerm> stored = [];
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
                stored = await repository.GetStoredTermsAsync(schoolId, cancellationToken);
                var connector = createConnector(fetcherFactory.Create(metrics));
                var listed = await connector.ListTermsAsync(school.Target, cancellationToken);
                termsListed = listed.Count;
                flags.AddRange(VanishedTermFlags(stored, listed));
                var selected = SelectTerms(listed);
                progress.Log(schoolId, $"{listed.Count} terms listed, collecting {selected.Count}");
                failureReason = selected.Count is 0
                    ? NoTermsReason(listed.Count)
                    : await CollectTermsAsync(connector,
                                              school.Target,
                                              selected,
                                              stored.ToDictionary(term => term.TermCode),
                                              terms,
                                              cancellationToken);
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

        flags.AddRange(terms.Where(term => term.IntegrationFlag).Select(term => $"term {term.TermCode}: {term.FailureReason}"));
        if (collectedCount is 0 && stored.Count > 0)
        {
            flags.Add($"earlier runs stored {stored.Count} terms, but nothing was collected this run; the stored data is kept");
        }

        var result = new SchoolRunResult
        {
            SchoolId = schoolId,
            Platform = school.Platform,
            Host = school.Target.BaseUrl.Authority,
            Status = collectedCount is 0 ? SchoolRunStatus.Failed
                   : failureReason is not null || lastFailedTerm is not null || terms.Any(term => term.MissingSections > 0)
                       ? SchoolRunStatus.Partial
                   : SchoolRunStatus.Collected,
            FailureReason = failureReason,
            FailureSourceUrl = failureSourceUrl,
            Discovery = discovery,
            IntegrationFlags = flags,
            TermsListed = termsListed,
            Terms = terms,
            Requests = metrics.Snapshot(),
            StartedAt = startedAt,
            Duration = discovery.Duration + timeProvider.GetElapsedTime(startTimestamp),
        };
        progress.Log(schoolId, Summarize(result));
        return result;
    }

    /// <returns>Why the school stopped early, or null when every selected term was attempted.</returns>
    private async Task<string?> CollectTermsAsync(IConnector connector,
                                                  ConnectorTarget target,
                                                  IReadOnlyList<Term> selected,
                                                  IReadOnlyDictionary<string, StoredTerm> stored,
                                                  List<TermRunResult> results,
                                                  CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        foreach (var term in selected)
        {
            var result = await CollectTermAsync(connector, target, term, stored.GetValueOrDefault(term.Key.TermCode), cancellationToken);
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

    /*
     * A term is written only when what was collected can stand in for the whole term: collected completely, or with a
     * small recorded gap of records the platform wouldn't return. A term whose section count collapsed since the last
     * run is not written at all, so a broken integration can't overwrite good data with a fragment.
     */
    private async Task<TermRunResult> CollectTermAsync(IConnector connector,
                                                       ConnectorTarget target,
                                                       Term term,
                                                       StoredTerm? previous,
                                                       CancellationToken cancellationToken)
    {
        var startTimestamp = timeProvider.GetTimestamp();
        try
        {
            var sectionsByKey = new Dictionary<SectionKey, Section>();
            var gaps = new List<CollectionGap>();
            var received = 0;
            await foreach (var raw in connector.CollectSectionsAsync(target, term, gaps, cancellationToken))
            {
                var section = connector.Map(raw);
                sectionsByKey[section.Key] = section;
                received++;
            }

            var sections = sectionsByKey.Values.ToList();
            var missing = gaps.Sum(gap => gap.Count);
            if (missing > 0 && missing > (sections.Count + missing) * options.MaxGapShare)
            {
                return Failed(term,
                              $"{missing} of {sections.Count + missing} sections couldn't be returned ({DescribeGaps(gaps)}), more than "
                              + $"the {options.MaxGapShare:0%} a term may miss and still be stored",
                              gaps[0].SourceUrl,
                              startTimestamp,
                              previous);
            }

            if (previous is not null
                && previous.SectionCount >= options.MinSectionsForDropCheck
                && sections.Count < previous.SectionCount * (1 - options.MaxSectionDrop))
            {
                return Failed(term,
                              $"broken integration suspected: {sections.Count} sections, down "
                              + $"{1 - sections.Count / (double)previous.SectionCount:0%} from {previous.SectionCount} when last "
                              + $"collected ({previous.LastConfirmedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC); nothing written, the "
                              + "stored sections are kept",
                              target.BaseUrl,
                              startTimestamp,
                              previous,
                              integrationFlag: true);
            }

            var changes = await repository.UpsertAsync(term, sections, gaps, timeProvider.GetUtcNow(), cancellationToken);
            return new TermRunResult(term.Key.TermCode,
                                     term.Name,
                                     sections.Count,
                                     sections.Sum(section => section.Meetings.Count),
                                     sections.Sum(section => section.Failures.Count),
                                     received - sections.Count,
                                     changes.RowsWritten,
                                     timeProvider.GetElapsedTime(startTimestamp),
                                     FailureReason: null,
                                     FailureSourceUrl: null,
                                     FieldCompleteness.Of(sections),
                                     IsNew: previous is null,
                                     PreviousSections: previous?.SectionCount,
                                     Changes: changes,
                                     Gaps: gaps.Count is 0 ? null : gaps);
        }
        catch (Exception exception) when (IsRunCancellation(exception, cancellationToken) is false)
        {
            var (reason, sourceUrl) = Describe(exception);
            return Failed(term, reason, sourceUrl, startTimestamp, previous);
        }
    }

    private TermRunResult Failed(Term term,
                                 string reason,
                                 Uri? sourceUrl,
                                 long startTimestamp,
                                 StoredTerm? previous,
                                 bool integrationFlag = false) =>
        new(term.Key.TermCode,
            term.Name,
            Sections: 0,
            Meetings: 0,
            ExtractionFailures: 0,
            DuplicateSections: 0,
            RowsWritten: 0,
            timeProvider.GetElapsedTime(startTimestamp),
            reason,
            sourceUrl,
            IsNew: previous is null,
            PreviousSections: previous?.SectionCount,
            IntegrationFlag: integrationFlag);

    private static IEnumerable<string> VanishedTermFlags(IReadOnlyList<StoredTerm> stored, IReadOnlyList<Term> listed)
    {
        var listedCodes = listed.Select(term => term.Key.TermCode).ToHashSet();
        return stored.Where(term => listedCodes.Contains(term.TermCode) is false)
                     .Select(term => $"term {term.TermCode} \"{term.Name}\" is no longer listed; its {term.SectionCount} stored "
                                     + "sections are kept");
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

    private static string DescribeGaps(IReadOnlyList<CollectionGap> gaps) =>
        string.Join(", ", gaps.Select(gap => gap.Count is 1 ? $"#{gap.FirstPosition}" : $"#{gap.FirstPosition}-{gap.FirstPosition + gap.Count - 1}"))
        + $" of {gaps[0].TotalCount}: {gaps[0].Reason}";

    private static bool IsRunCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static (string Reason, Uri? SourceUrl) Describe(Exception exception) => exception switch
    {
        ConnectorException connectorException => (connectorException.Message, connectorException.SourceUrl),
        _ => ($"{exception.GetType().Name}: {exception.Message}", null),
    };

    private static string Summarize(TermRunResult term) => term.FailureReason is null
        ? $"term {term.TermCode} \"{term.Name}\"{(term.IsNew ? " (new)" : "")}: {term.Sections} sections, {term.Meetings} meetings, "
          + $"{term.ExtractionFailures} extraction failures; {term.Changes?.Added} added, {term.Changes?.Changed} changed, "
          + $"{term.Changes?.Unchanged} unchanged, {term.Changes?.NotSeen} not seen; {term.RowsWritten} rows written"
          + (term.MissingSections > 0 ? $"; {term.MissingSections} sections missing ({DescribeGaps(term.Gaps!)})" : "")
          + $" ({term.Duration.TotalSeconds:0.0}s)"
        : $"term {term.TermCode} \"{term.Name}\" FAILED: {term.FailureReason}"
          + (term.FailureSourceUrl is null ? "" : $" [{term.FailureSourceUrl}]");

    private static string Summarize(SchoolRunResult school) =>
        $"{school.Status}: {school.TermsCollected}/{school.Terms.Count} terms ({school.NewTerms} new), {school.Sections} sections, "
        + $"{school.Meetings} meetings, {school.Requests.Requests} requests ({school.Requests.CacheHits} from cache), "
        + $"{school.Requests.BodyBytes / 1_048_576.0:0.0} MB, {school.Duration:hh\\:mm\\:ss}"
        + (school.FailureReason is null ? "" : $"; {school.FailureReason}")
        + (school.IntegrationFlags.Count is 0 ? "" : $"; FLAGGED: {string.Join(" | ", school.IntegrationFlags)}");

    #endregion Methods
}
