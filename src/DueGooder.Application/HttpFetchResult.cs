namespace DueGooder.Application;

/// <summary>An HTTP response as a connector sees it.</summary>
/// <param name="Url">The URL that was requested.</param>
/// <param name="StatusCode">
/// HTTP status. Redirects are returned, not followed, so a redirect to a sign-in page stays visible.
/// </param>
/// <param name="Body">Response body as text.</param>
/// <param name="RetrievedAt">When the response arrived (UTC).</param>
public sealed record HttpFetchResult(Uri Url, int StatusCode, string Body, DateTimeOffset RetrievedAt)
{
    #region State

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    #endregion State
}
