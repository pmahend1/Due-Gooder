using System.Net;
using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// One live <see cref="IHttpSession"/>: its own <see cref="HttpClient"/>, cookie jar and, crucially,
/// redirects are never followed automatically so a sign-in redirect stays visible to the connector.
/// </summary>
internal sealed class HttpSession : IHttpSession
{
    #region State

    private readonly HttpClient _client;

    #endregion State

    #region Methods

    public HttpSession(string userAgent)
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false,
        };
        _client = new HttpClient(handler);
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    public async Task<HttpFetchResult> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(url, cancellationToken);
        return await ToResultAsync(url, response, cancellationToken);
    }

    public async Task<HttpFetchResult> PostFormAsync(Uri url,
                                                      IReadOnlyDictionary<string, string> form,
                                                      CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _client.PostAsync(url, content, cancellationToken);
        return await ToResultAsync(url, response, cancellationToken);
    }

    public void Dispose() => _client.Dispose();

    private static async Task<HttpFetchResult> ToResultAsync(Uri url,
                                                              HttpResponseMessage response,
                                                              CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new HttpFetchResult(url, (int)response.StatusCode, body, DateTimeOffset.UtcNow);
    }

    #endregion Methods
}
