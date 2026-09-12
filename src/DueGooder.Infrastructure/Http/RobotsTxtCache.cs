using System.Collections.Concurrent;
using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Fetches each origin's robots.txt once per run. 2xx is parsed; 4xx or a redirect loop means no
/// restrictions; 5xx or a network failure means disallow-all, as RFC 9309 requires.
/// </summary>
internal sealed class RobotsTxtCache(HttpClient client, string productToken)
{
    #region State

    private readonly ConcurrentDictionary<string, Lazy<Task<RobotsTxt>>> _byOrigin = new(StringComparer.OrdinalIgnoreCase);

    #endregion State

    #region Methods

    /// <param name="url">Any URL on the origin.</param>
    /// <param name="metrics">Charged for the robots.txt request when this call is the one that fetches it.</param>
    /// <param name="cancellationToken">Stops this caller waiting; the shared fetch itself keeps going.</param>
    public Task<RobotsTxt> GetAsync(Uri url, RequestMetrics? metrics, CancellationToken cancellationToken) =>
        _byOrigin.GetOrAdd(OriginOf(url),
                           origin => new Lazy<Task<RobotsTxt>>(() => FetchAsync(new Uri(origin + "/robots.txt"), metrics)))
                 .Value
                 .WaitAsync(cancellationToken);

    /// <summary>The origin's Crawl-delay, once its robots.txt has been read.</summary>
    public TimeSpan? CrawlDelayFor(Uri url) =>
        _byOrigin.TryGetValue(OriginOf(url), out var entry) && entry.IsValueCreated && entry.Value.IsCompletedSuccessfully
            ? entry.Value.Result.CrawlDelay
            : null;

    private async Task<RobotsTxt> FetchAsync(Uri robotsUrl, RequestMetrics? metrics)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, robotsUrl);
        if (metrics is not null)
        {
            request.Options.Set(RequestOptionKeys.Metrics, metrics);
        }

        try
        {
            using var response = await client.SendAsync(request, CancellationToken.None);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                return RobotsTxt.Parse(robotsUrl, await response.Content.ReadAsStringAsync(), productToken);
            }

            // A 3xx here means more redirects than RFC 9309 asks us to follow; like 4xx, it counts as "no file".
            return status is >= 300 and < 500
                ? RobotsTxt.Unavailable(robotsUrl, productToken)
                : RobotsTxt.Unreachable(robotsUrl, productToken, $"HTTP {status}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException)
        {
            return RobotsTxt.Unreachable(robotsUrl, productToken, exception.Message);
        }
    }

    private static string OriginOf(Uri url) => url.GetLeftPart(UriPartial.Authority);

    #endregion Methods
}
