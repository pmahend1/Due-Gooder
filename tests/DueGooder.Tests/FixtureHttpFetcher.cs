using System.Web;
using DueGooder.Application;

namespace DueGooder.Tests;

/// <summary>
/// Answers Banner 9 requests from the recorded responses in <c>tests/fixtures/banner9/&lt;school&gt;/</c>
/// and records every request. A request with no recorded response gets HTTP 404.
/// </summary>
internal sealed class FixtureHttpFetcher(string school) : IHttpFetcher
{
    #region State

    public static readonly DateTimeOffset RetrievedAt = new(2026, 9, 11, 23, 40, 0, TimeSpan.Zero);

    private int _sessionCount;

    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Statuses to answer with instead of a recorded response, keyed by fixture file name.</summary>
    public Dictionary<string, int> StatusOverrides { get; } = [];

    /// <summary>HTTP 200 bodies to answer with, one per request, before falling back to the recorded response.</summary>
    public Dictionary<string, Queue<string>> OneTimeBodies { get; } = [];

    #endregion State

    #region Methods

    public IHttpSession OpenSession() => new FixtureHttpSession(this, ++_sessionCount);

    internal HttpFetchResult Respond(int session,
                                     string method,
                                     Uri url,
                                     IReadOnlyDictionary<string, string>? form)
    {
        Requests.Add(new RecordedRequest(session, method, url, form));
        var fileName = FixtureFileFor(url);
        if (StatusOverrides.TryGetValue(fileName, out var status))
        {
            return new HttpFetchResult(url, status, "", RetrievedAt);
        }

        if (OneTimeBodies.TryGetValue(fileName, out var bodies) && bodies.TryDequeue(out var body))
        {
            return new HttpFetchResult(url, 200, body, RetrievedAt);
        }

        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "banner9", school, fileName);
        return File.Exists(path)
            ? new HttpFetchResult(url, 200, File.ReadAllText(path), RetrievedAt)
            : new HttpFetchResult(url, 404, "", RetrievedAt);
    }

    // File names match what tools/capture-banner9-fixtures.sh writes.
    private static string FixtureFileFor(Uri url)
    {
        var path = url.AbsolutePath;
        if (path.EndsWith("/classSearch/getTerms", StringComparison.OrdinalIgnoreCase))
        {
            return "getTerms.json";
        }

        if (path.EndsWith("/term/search", StringComparison.OrdinalIgnoreCase))
        {
            return "term-search.json";
        }

        if (path.EndsWith("/searchResults/searchResults", StringComparison.OrdinalIgnoreCase))
        {
            return $"searchResults-{HttpUtility.ParseQueryString(url.Query)["pageOffset"]}.json";
        }

        if (path.EndsWith("/classSearch/resetDataForm", StringComparison.OrdinalIgnoreCase))
        {
            return "resetDataForm.json";
        }

        throw new InvalidOperationException($"No Banner 9 fixture route for {url}");
    }

    #endregion Methods
}
