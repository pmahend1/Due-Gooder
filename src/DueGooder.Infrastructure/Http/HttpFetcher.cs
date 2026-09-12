using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Live <see cref="IHttpFetcher"/> for one school. Each session gets its own cookie jar, since Banner-style
/// platforms keep search state in the cookie session and different terms must never share one.
/// </summary>
internal sealed class HttpFetcher(HttpFetcherFactory factory, RequestMetrics metrics) : IHttpFetcher
{
    #region Methods

    public IHttpSession OpenSession() => new HttpSession(factory.NewSessionClient(), metrics);

    #endregion Methods
}
