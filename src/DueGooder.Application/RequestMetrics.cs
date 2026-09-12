using System.Collections.Concurrent;

namespace DueGooder.Application;

/// <summary>
/// Thread-safe HTTP counters for one school. The HTTP layer records every network attempt and cache hit
/// here, so per-school requests and bytes in the run report are measured, not estimated.
/// </summary>
public sealed class RequestMetrics
{
    #region State

    private readonly ConcurrentDictionary<int, int> _statusCounts = new();

    private long _requests;

    private long _networkErrors;

    private long _retries;

    private long _bodyBytes;

    private long _cacheHits;

    private long _cachedBodyBytes;

    #endregion State

    #region Methods

    /// <summary>A response that arrived over the network; <paramref name="bodyBytes"/> is measured after decompression.</summary>
    public void RecordResponse(int statusCode, long bodyBytes)
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Add(ref _bodyBytes, bodyBytes);
        _statusCounts.AddOrUpdate(statusCode, 1, (_, count) => count + 1);
    }

    /// <summary>A request that got no HTTP response: connection failure, reset or timeout.</summary>
    public void RecordNetworkError()
    {
        Interlocked.Increment(ref _requests);
        Interlocked.Increment(ref _networkErrors);
    }

    public void RecordRetry() => Interlocked.Increment(ref _retries);

    public void RecordCacheHit(long bodyBytes)
    {
        Interlocked.Increment(ref _cacheHits);
        Interlocked.Add(ref _cachedBodyBytes, bodyBytes);
    }

    public RequestStats Snapshot() =>
        new(Interlocked.Read(ref _requests),
            Interlocked.Read(ref _networkErrors),
            Interlocked.Read(ref _retries),
            Interlocked.Read(ref _bodyBytes),
            Interlocked.Read(ref _cacheHits),
            Interlocked.Read(ref _cachedBodyBytes),
            new SortedDictionary<int, int>(_statusCounts));

    #endregion Methods
}
