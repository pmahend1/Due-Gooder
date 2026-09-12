using System.Web;
using DueGooder.Application;

namespace DueGooder.Tests;

/// <summary>
/// A Banner 9 class search over <paramref name="totalSections"/> numbered sections that answers the way MSU Denver's did:
/// any results window containing an unreturnable record gets <c>success:false</c>, the real count and no sections.
/// </summary>
internal static class SimulatedBanner
{
    #region Methods

    public static RoutedHttpFetcher Fetcher(int totalSections, params int[] unreturnablePositions) =>
        new((_, url) => Respond(url, totalSections, unreturnablePositions));

    private static HttpFetchResult Respond(Uri url, int totalSections, int[] unreturnablePositions)
    {
        if (url.AbsolutePath.EndsWith("/searchResults/searchResults") is false)
        {
            return RoutedHttpFetcher.Ok(url, "{}");
        }

        var query = HttpUtility.ParseQueryString(url.Query);
        var offset = int.Parse(query["pageOffset"]!);
        var size = int.Parse(query["pageMaxSize"]!);
        var window = Enumerable.Range(offset, Math.Max(0, Math.Min(size, totalSections - offset))).ToList();
        if (window.Any(position => unreturnablePositions.Contains(position + 1)))
        {
            return RoutedHttpFetcher.Ok(url, $$"""{"success":false,"totalCount":{{totalSections}},"data":[]}""");
        }

        var data = string.Join(",", window.Select(position => $$"""{"courseReferenceNumber":"{{position + 1}}"}"""));
        return RoutedHttpFetcher.Ok(url, $$"""{"success":true,"totalCount":{{totalSections}},"data":[{{data}}]}""");
    }

    #endregion Methods
}
