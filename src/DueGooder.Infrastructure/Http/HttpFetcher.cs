using DueGooder.Application;

namespace DueGooder.Infrastructure.Http;

/// <summary>
/// Live <see cref="IHttpFetcher"/> over <see cref="HttpClient"/>. Each session gets its own
/// <see cref="System.Net.CookieContainer"/>, since Banner-style platforms keep search state
/// in the cookie session and different terms/schools must never share one.
/// </summary>
public sealed class HttpFetcher(string userAgent) : IHttpFetcher
{
    #region State

    /// <summary>Default identifying User-Agent: says what the crawler is without naming a person.</summary>
    public const string DefaultUserAgent =
        "DueGooderBot/0.1 (Hack Kentucky 2026 hackathon project; collects public course/section data)";

    #endregion State

    #region Methods

    public IHttpSession OpenSession() => new HttpSession(userAgent);

    #endregion Methods
}
