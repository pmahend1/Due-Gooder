namespace DueGooder.Application;

/// <summary>A point-in-time copy of <see cref="RequestMetrics"/>.</summary>
/// <param name="Requests">Requests sent over the network, retries and robots.txt included; cache hits excluded.</param>
/// <param name="NetworkErrors">Requests that got no HTTP response (connection failure, reset or timeout).</param>
/// <param name="Retries">Requests repeated after a transient failure.</param>
/// <param name="BodyBytes">Response body bytes received over the network, measured after decompression.</param>
/// <param name="CacheHits">Requests answered from the dev response cache instead of the network.</param>
/// <param name="CachedBodyBytes">Body bytes served from the dev response cache.</param>
/// <param name="StatusCounts">Network responses per HTTP status code, for endpoint health.</param>
public sealed record RequestStats(long Requests,
                                  long NetworkErrors,
                                  long Retries,
                                  long BodyBytes,
                                  long CacheHits,
                                  long CachedBodyBytes,
                                  IReadOnlyDictionary<int, int> StatusCounts);
