using DueGooder.Application;

namespace DueGooder.Tests;

/// <summary>One session opened on a <see cref="FixtureHttpFetcher"/>.</summary>
internal sealed class FixtureHttpSession(FixtureHttpFetcher fetcher, int sessionNumber) : IHttpSession
{
    #region Methods

    public Task<HttpFetchResult> GetAsync(Uri url, CancellationToken cancellationToken) =>
        Task.FromResult(fetcher.Respond(sessionNumber, "GET", url, form: null));

    public Task<HttpFetchResult> PostFormAsync(Uri url,
                                               IReadOnlyDictionary<string, string> form,
                                               CancellationToken cancellationToken) =>
        Task.FromResult(fetcher.Respond(sessionNumber, "POST", url, form));

    public void Dispose()
    {
    }

    #endregion Methods
}
