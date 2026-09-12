namespace DueGooder.Infrastructure.Http;

/// <summary>Politeness and caching settings for live HTTP. The defaults are deliberately conservative.</summary>
public sealed record HttpFetcherOptions
{
    #region State

    /// <summary>Default identifying User-Agent: says what the crawler is without naming a person.</summary>
    public const string DefaultUserAgent =
        "DueGooderBot/0.1 (Hack Kentucky 2026 hackathon project; collects public course/section data)";

    /// <summary>Sent on every request. Its product token (text before the first <c>/</c>) is matched against robots.txt groups.</summary>
    public string UserAgent { get; init; } = DefaultUserAgent;

    /// <summary>Minimum gap between the starts of two requests to one host. A robots.txt Crawl-delay can raise it, never lower it.</summary>
    public TimeSpan MinRequestInterval { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Time allowed for one attempt, body included. Waiting for a host's turn doesn't count.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Extra attempts after a network error, a timeout, or HTTP 429/502/503/504.</summary>
    public int MaxRetries { get; init; } = 2;

    public ResponseCacheMode CacheMode { get; init; } = ResponseCacheMode.Off;

    public string CacheDirectory { get; init; } = ".cache/http";

    /// <summary>
    /// Query and form parameters left out of cache keys because their values are random per session
    /// (e.g. a search id), which would otherwise make every request a cache miss.
    /// </summary>
    public IReadOnlySet<string> CacheKeyIgnoredParameters { get; init; } = new HashSet<string>();

    #endregion State
}
