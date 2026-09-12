using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// One live <see cref="IHttpSession"/>. Requests carry the school's <see cref="RequestMetrics"/> down the
/// handler chain, and a request that never got a response becomes a <see cref="ConnectorException"/>
/// with its URL, so the run records it against the school instead of crashing.
/// </summary>
internal sealed class HttpSession(HttpClient client, RequestMetrics metrics) : IHttpSession
{
    #region Methods

    public Task<HttpFetchResult> GetAsync(Uri url, CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);

    public Task<HttpFetchResult> PostFormAsync(Uri url,
                                               IReadOnlyDictionary<string, string> form,
                                               CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) },
                  cancellationToken);

    public void Dispose() => client.Dispose();

    private async Task<HttpFetchResult> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var ownedRequest = request;
        var url = request.RequestUri!;
        request.Options.Set(RequestOptionKeys.Metrics, metrics);
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new HttpFetchResult(url, (int)response.StatusCode, body, DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException exception)
        {
            throw new ConnectorException($"network error: {exception.Message}", url, exception);
        }
        catch (TimeoutException exception)
        {
            throw new ConnectorException(exception.Message, url, exception);
        }
    }

    #endregion Methods
}
