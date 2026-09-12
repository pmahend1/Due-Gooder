using System.Diagnostics;
using DueGooder.Infrastructure.Http;

namespace DueGooder.Tests;

public sealed class HttpPolitenessTests
{
    #region Methods

    [Fact]
    public async Task Requests_to_one_host_are_spaced_by_the_interval()
    {
        var limiter = new HostRateLimiter(TimeProvider.System);
        var url = new Uri("https://a.edu/x");
        var clock = Stopwatch.StartNew();

        for (var request = 0; request < 3; request++)
        {
            await limiter.EnterAsync(url, TimeSpan.FromMilliseconds(150), CancellationToken.None);
            limiter.Exit(url);
        }

        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(290), $"took only {clock.Elapsed}");
    }

    [Fact]
    public async Task Different_hosts_do_not_wait_for_each_other()
    {
        var limiter = new HostRateLimiter(TimeProvider.System);
        var interval = TimeSpan.FromSeconds(5);
        var clock = Stopwatch.StartNew();

        foreach (var host in new[] { "a.edu", "b.edu", "c.edu" })
        {
            var url = new Uri($"https://{host}/x");
            await limiter.EnterAsync(url, interval, CancellationToken.None);
            limiter.Exit(url);
        }

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(1), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task Cache_keys_ignore_per_session_parameters_but_not_paging()
    {
        var cache = new ResponseCache(Path.GetTempPath(), new HashSet<string> { "uniqueSessionId" });

        var first = await KeyAsync(cache, "https://a.edu/searchResults?txt_term=202710&uniqueSessionId=dg111&pageOffset=0", "dg111");
        var sameSearchOtherSession = await KeyAsync(cache, "https://a.edu/searchResults?txt_term=202710&uniqueSessionId=dg222&pageOffset=0", "dg222");
        var nextPage = await KeyAsync(cache, "https://a.edu/searchResults?txt_term=202710&uniqueSessionId=dg111&pageOffset=500", "dg111");

        Assert.Equal(first, sameSearchOtherSession);
        Assert.NotEqual(first, nextPage);
    }

    [Fact]
    public async Task Cached_responses_round_trip_through_disk()
    {
        var directory = Path.Combine(Path.GetTempPath(), "duegooder-cache-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ResponseCache(directory, new HashSet<string>());
            var url = new Uri("https://a.edu/getTerms");
            var stored = new CachedResponse(url.ToString(), 200, "application/json", """[{"code":"202710"}]""");

            await cache.WriteAsync(url, "key", stored, CancellationToken.None);

            Assert.Equal(stored, await cache.TryReadAsync(url, "key", CancellationToken.None));
            Assert.Null(await cache.TryReadAsync(url, "other", CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task<string> KeyAsync(ResponseCache cache, string url, string sessionId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["term"] = "202710",
                ["uniqueSessionId"] = sessionId,
            }),
        };
        return cache.KeyForAsync(request, CancellationToken.None);
    }

    #endregion Methods
}
