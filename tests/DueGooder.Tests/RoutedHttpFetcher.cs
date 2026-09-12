using DueGooder.Application;

namespace DueGooder.Tests;

/// <summary>
/// Answers every request through one function, so a test can script whole sites: homepages, redirects, and a Banner
/// server that computes its answer from the query. Records every request.
/// </summary>
internal sealed class RoutedHttpFetcher(Func<string, Uri, HttpFetchResult> respond) : IHttpFetcher
{
    #region State

    public List<RecordedRequest> Requests { get; } = [];

    #endregion State

    #region Methods

    public IHttpSession OpenSession() => new RoutedHttpSession(this, Requests.Select(request => request.Session).DefaultIfEmpty().Max() + 1);

    internal HttpFetchResult Respond(int session, string method, Uri url, IReadOnlyDictionary<string, string>? form)
    {
        Requests.Add(new RecordedRequest(session, method, url, form));
        return respond(method, url);
    }

    public static HttpFetchResult Ok(Uri url, string body) => new(url, 200, body, FixtureHttpFetcher.RetrievedAt);

    public static HttpFetchResult Status(Uri url, int status) => new(url, status, "", FixtureHttpFetcher.RetrievedAt);

    public static HttpFetchResult Redirect(Uri url, string location) =>
        new(url, 302, "", FixtureHttpFetcher.RetrievedAt) { RedirectLocation = new Uri(url, location) };

    #endregion Methods
}
