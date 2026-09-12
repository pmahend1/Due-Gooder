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

    public ConcurrentBag<(Term Term, IReadOnlyList<Section> Sections)> Upserts { get; } = [];

    public ConcurrentQueue<string> LogLines { get; } = [];

    public ConcurrentQueue<SchoolRunResult> Finished { get; } = [];

    #endregion State

    #region Methods

    public Task<int> UpsertAsync(Term term, IReadOnlyList<Section> sections, CancellationToken cancellationToken)
    {
        Upserts.Add((term, sections));
        return Task.FromResult(sections.Count + 1);
    }

    public void Log(string schoolId, string message) => LogLines.Enqueue($"[{schoolId}] {message}");

    public void SchoolFinished(SchoolRunResult result) => Finished.Enqueue(result);

    public IHttpFetcher Create(RequestMetrics metrics) => this;

    public IHttpSession OpenSession() => throw new NotSupportedException("scripted connectors don't use HTTP");

    #endregion Methods
}
