namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Waits for the host's turn, then sends one attempt under its own timeout. The turn is held until the
/// body has been read (<see cref="MetricsHandler"/> buffers it), so requests to a host never overlap.
/// </summary>
internal sealed class RateLimitHandler(HostRateLimiter limiter, Func<Uri, TimeSpan> intervalFor, TimeSpan timeout)
    : DelegatingHandler
{
    #region Methods

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                                  CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        await limiter.EnterAsync(url, intervalFor(url), cancellationToken);
        try
        {
            using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeout.CancelAfter(timeout);
            try
            {
                return await base.SendAsync(request, attemptTimeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested is false)
            {
                throw new TimeoutException($"no complete response within {timeout.TotalSeconds:0}s");
            }
        }
        finally
        {
            limiter.Exit(url);
        }
    }

    #endregion Methods
}
