namespace DueGooder.Application;

/// <summary>
/// Requests that share one cookie jar, for platforms such as Banner 9 that keep search state
/// in the server session. Implementations return redirects instead of following them.
/// </summary>
public interface IHttpSession : IDisposable
{
    #region Methods

    Task<HttpFetchResult> GetAsync(Uri url, CancellationToken cancellationToken);

    Task<HttpFetchResult> PostFormAsync(Uri url,
                                        IReadOnlyDictionary<string, string> form,
                                        CancellationToken cancellationToken);

    #endregion Methods
}
