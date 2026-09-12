using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>Per-request values handed down the handler chain.</summary>
internal static class RequestOptionKeys
{
    #region State

    /// <summary>The school's counters. Shared handlers (robots.txt) use it to charge requests to the right school.</summary>
    public static readonly HttpRequestOptionsKey<RequestMetrics> Metrics = new("DueGooder.RequestMetrics");

    #endregion State
}
