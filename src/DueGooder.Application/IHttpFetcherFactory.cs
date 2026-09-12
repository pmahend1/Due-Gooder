namespace DueGooder.Application;

/// <summary>
/// Creates fetchers that count their requests into one school's <see cref="RequestMetrics"/>, while
/// sharing the run's politeness state (per-host rate limits, robots.txt) across every school.
/// </summary>
public interface IHttpFetcherFactory
{
    #region Methods

    IHttpFetcher Create(RequestMetrics metrics);

    #endregion Methods
}
