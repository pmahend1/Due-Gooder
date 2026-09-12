using System.Net;
using System.Text;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Outermost handler: in <see cref="ResponseCacheMode.Use"/> a cached response is returned without
/// touching the host; otherwise the request goes out and a successful answer is stored.
/// </summary>
/// <remarks>Only 2xx responses are stored, so redirects and errors are always re-checked live.</remarks>
internal sealed class ResponseCacheHandler(ResponseCache cache, ResponseCacheMode mode) : DelegatingHandler
{
    #region Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                  CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        var key = await cache.KeyForAsync(request, cancellationToken);
        if (mode is ResponseCacheMode.Use && await cache.TryReadAsync(url, key, cancellationToken) is { } cached)
        {
            request.Options.TryGetValue(RequestOptionKeys.Metrics, out var metrics);
            metrics?.RecordCacheHit(Encoding.UTF8.GetByteCount(cached.Body));
            return new HttpResponseMessage((HttpStatusCode)cached.StatusCode)
            {
                RequestMessage = request,
                Content = new StringContent(cached.Body, Encoding.UTF8, cached.MediaType ?? "text/plain"),
            };
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            await cache.WriteAsync(url,
                                   key,
                                   new CachedResponse(url.ToString(),
                                                      (int)response.StatusCode,
                                                      response.Content.Headers.ContentType?.MediaType,
                                                      body),
                                   cancellationToken);
        }

        return response;
    }

    #endregion Methods
}
