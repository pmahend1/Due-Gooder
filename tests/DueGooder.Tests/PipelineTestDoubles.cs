using System.Collections.Concurrent;
using DueGooder.Application;
using DueGooder.Application.Pipeline;
using DueGooder.Domain;

namespace DueGooder.Tests;

/// <summary>
/// The pipeline's other collaborators in one fake: a repository that remembers what it was given, progress
/// that is kept in memory, and a fetcher factory for connectors that never touch HTTP.
/// </summary>
internal sealed class PipelineTestDoubles : ISectionRepository, IRunProgress, IHttpFetcherFactory, IHttpFetcher
{
    #region State

    public ConcurrentBag<(Term Term, IReadOnlyList<Section> Sections, IReadOnlyList<CollectionGap> Gaps)> Upserts { get; } = [];

    /// <summary>Fetcher for discovery; null means connectors never touch HTTP.</summary>
    public IHttpFetcher? Http { get; init; }

    /// <summary>What earlier runs "stored", by school id.</summary>
    public Dictionary<string, List<StoredTerm>> Stored { get; } = [];

    public ConcurrentQueue<string> LogLines { get; } = [];

    public ConcurrentQueue<SchoolRunResult> Finished { get; } = [];

    #endregion State

    #region Methods

    public Task<IReadOnlyList<StoredTerm>> GetStoredTermsAsync(string schoolId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredTerm>>(Stored.GetValueOrDefault(schoolId) ?? []);

    public Task<SectionChanges> UpsertAsync(Term term,
                                            IReadOnlyList<Section> sections,
                                            IReadOnlyList<CollectionGap> gaps,
                                            DateTimeOffset confirmedAt,
                                            CancellationToken cancellationToken)
    {
        Upserts.Add((term, sections, gaps));
        return Task.FromResult(new SectionChanges(sections.Count + 1, sections.Count, Changed: 0, Unchanged: 0, NotSeen: 0));
    }

    public void Log(string schoolId, string message) => LogLines.Enqueue($"[{schoolId}] {message}");

    public void SchoolFinished(SchoolRunResult result) => Finished.Enqueue(result);

    public IHttpFetcher Create(RequestMetrics metrics) => Http ?? this;

    public IHttpSession OpenSession() => throw new NotSupportedException("scripted connectors don't use HTTP");

    #endregion Methods
}
