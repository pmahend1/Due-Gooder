using System.Net;
using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Builds live fetchers that share one politeness state for the whole run: one rate limiter and one
/// robots.txt per host, and one response cache, whichever school's connector makes the request.
/// </summary>
/// <remarks>
/// Each session's handler chain, outermost first: dev cache → robots.txt → retry → per-host rate limit →
/// metrics → network. Retries go back through the rate limiter, and a cache hit never touches the host.
/// </remarks>
public sealed class HttpFetcherFactory : IHttpFetcherFactory, IDisposable
{
    #region State

    private const int MaxRobotsRedirects = 5;

    private readonly HttpFetcherOptions _options;

    private readonly TimeProvider _timeProvider;

    private readonly HostRateLimiter _rateLimiter;

    private readonly HttpClient _robotsClient;

    private readonly RobotsTxtCache _robots;

    private readonly ResponseCache? _cache;

    #endregion State

    #region Methods

    public HttpFetcherFactory(HttpFetcherOptions options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _rateLimiter = new HostRateLimiter(timeProvider);

        // RFC 9309 asks crawlers to follow robots.txt redirects, unlike every other request we make.
        var robotsNetwork = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = MaxRobotsRedirects,
            AutomaticDecompression = DecompressionMethods.All,
        };
        _robotsClient = NewClient(Throttled(robotsNetwork, _ => options.MinRequestInterval));
        _robots = new RobotsTxtCache(_robotsClient, RobotsTxt.ProductTokenOf(options.UserAgent));
        _cache = options.CacheMode is ResponseCacheMode.Off
            ? null
            : new ResponseCache(options.CacheDirectory, options.CacheKeyIgnoredParameters);
    }

    public IHttpFetcher Create(RequestMetrics metrics) => new HttpFetcher(this, metrics);

    public void Dispose() => _robotsClient.Dispose();

    /// <summary>A client with its own cookie jar that never follows redirects, so a sign-in redirect stays visible.</summary>
    internal HttpClient NewSessionClient()
    {
        var network = new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
        };

        HttpMessageHandler handler = new RobotsHandler(_robots) { InnerHandler = Throttled(network, IntervalFor) };
        if (_cache is not null)
        {
            handler = new ResponseCacheHandler(_cache, _options.CacheMode) { InnerHandler = handler };
        }

        return NewClient(handler);
    }

    private DelegatingHandler Throttled(HttpMessageHandler network, Func<Uri, TimeSpan> intervalFor) =>
        new RetryHandler(_options.MaxRetries, _timeProvider)
        {
            InnerHandler = new RateLimitHandler(_rateLimiter, intervalFor, _options.RequestTimeout)
            {
                InnerHandler = new MetricsHandler { InnerHandler = network },
            },
        };

    private TimeSpan IntervalFor(Uri url) =>
        _robots.CrawlDelayFor(url) is { } crawlDelay && crawlDelay > _options.MinRequestInterval
            ? crawlDelay
            : _options.MinRequestInterval;

    // Timeouts are per attempt, inside RateLimitHandler, so time spent waiting for a host's turn or for a retry never counts.
    private HttpClient NewClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(_options.UserAgent);
        return client;
    }

    #endregion Methods
}
