using System.Net;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Repeats a request after a network error, a timeout, or HTTP 429/502/503/504, waiting longer each time
/// and honouring Retry-After. Every attempt goes back through the per-host rate limiter.
/// </summary>
internal sealed class RetryHandler(int maxRetries, TimeProvider timeProvider) : DelegatingHandler
{
    #region State

    private static readonly TimeSpan FirstBackoff = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromMinutes(2);

    #endregion State

    #region Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                  CancellationToken cancellationToken)
    {
        request.Options.TryGetValue(RequestOptionKeys.Metrics, out var metrics);
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TimeoutException && attempt < maxRetries)
            {
                metrics?.RecordRetry();
                await Task.Delay(Backoff(attempt), timeProvider, cancellationToken);
                continue;
            }

            if (IsTransient(response.StatusCode) is false || attempt >= maxRetries)
            {
                return response;
            }

            var delay = RetryAfter(response) ?? Backoff(attempt);
            response.Dispose();
            metrics?.RecordRetry();
            await Task.Delay(delay, timeProvider, cancellationToken);
        }
    }

    // 10s, then 30s: long enough for a registrar that is briefly overloaded to recover.
    private static TimeSpan Backoff(int attempt) => FirstBackoff * Math.Pow(3, attempt);

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests
               or HttpStatusCode.BadGateway
               or HttpStatusCode.ServiceUnavailable
               or HttpStatusCode.GatewayTimeout;

    private TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        var delay = retryAfter?.Delta ?? retryAfter?.Date - timeProvider.GetUtcNow();
        return delay is { } value
            ? TimeSpan.FromTicks(Math.Clamp(value.Ticks, 0, MaxRetryAfter.Ticks))
            : null;
    }

    #endregion Methods
}
