namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Innermost handler: counts every network attempt and its body size against the request's school.
/// Buffers the body here so the size is known and the whole download happens inside the host's turn.
/// </summary>
internal sealed class MetricsHandler : DelegatingHandler
{
    #region Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                  CancellationToken cancellationToken)
    {
        request.Options.TryGetValue(RequestOptionKeys.Metrics, out var metrics);
        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            metrics?.RecordResponse((int)response.StatusCode, body.Length);
            return response;
        }
        catch
        {
            response?.Dispose();
            metrics?.RecordNetworkError();
            throw;
        }
    }

    #endregion Methods
}
