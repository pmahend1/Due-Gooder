namespace DueGooder.Application.Discovery;

/// <summary>
/// Identifies one school's platform starting from its homepage alone. It fetches the homepage, then follows the
/// highest-ranked schedule and registrar links on the school's own site. Every connector checks each page's URL and
/// HTML for its own entry points, and a probe request confirms them. When no page points anywhere, the connectors'
/// well-known host names are probed. Every step goes into the evidence, which is what the review queue shows when
/// nothing is confirmed.
/// </summary>
/// <remarks>One instance per school: it accumulates that school's evidence.</remarks>
public sealed class HomepageDiscovery(IReadOnlyList<IConnector> connectors, IHttpFetcher fetcher, DiscoveryOptions options)
{
    #region State

    private const int MaxLinkTextLength = 40;

    private readonly List<string> _evidence = [];

    private readonly HashSet<string> _probed = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _hints = [];

    private readonly List<string> _failedGuesses = [];

    private string? _linkedProbeFailure;

    private string? _guessedHostFailure;

    private double _bestConfidence;

    private int _pagesFetched;

    private int _probes;

    #endregion State

    #region Methods

    public async Task<DiscoveryResult> DiscoverAsync(Uri homepage, CancellationToken cancellationToken)
    {
        using var session = fetcher.OpenSession();
        var sites = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { RegistrableDomain.Of(homepage.Host) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { homepage.AbsoluteUri };

        // Lower sorts first: a schedule link two hops away beats a registrar link one hop away.
        var queue = new PriorityQueue<(Uri Url, int Depth, string Via), int>();
        queue.Enqueue((homepage, 0, "homepage"), 0);
        while (_pagesFetched < options.MaxPages && queue.TryDequeue(out var next, out _))
        {
            var method = next.Depth is 0 ? DiscoveryMethod.HomepageLink : DiscoveryMethod.CrawledLink;
            var (page, redirectTargets) = await FetchPageAsync(session, next.Url, next.Via, sites, next.Depth is 0, cancellationToken);
            foreach (var target in redirectTargets)
            {
                if (await ProbeEntryPointsAsync(target, "", $"redirect target of {next.Url}", method, cancellationToken) is { } viaRedirect)
                {
                    return viaRedirect;
                }
            }

            if (page is null)
            {
                continue;
            }

            // A homepage that redirects to another domain (a renamed school) makes that domain the school's site too.
            sites.Add(RegistrableDomain.Of(page.Url.Host));
            if (await ProbeEntryPointsAsync(page.Url, page.Body, $"found on {page.Url}", method, cancellationToken) is { } found)
            {
                return found;
            }

            foreach (var hint in PlatformHints.Find(page.Body))
            {
                _hints.TryAdd(hint[..hint.IndexOf(':')], hint);
            }

            if (next.Depth >= options.MaxDepth)
            {
                continue;
            }

            foreach (var link in HtmlLinks.Extract(page.Url, page.Body))
            {
                var score = HtmlLinks.ScheduleScore(link);
                if (score > 0 && sites.Any(site => RegistrableDomain.IsWithin(link.Url, site)) && seen.Add(link.Url.AbsoluteUri))
                {
                    queue.Enqueue((link.Url, next.Depth + 1, $"link \"{Shorten(link.Text)}\" on {page.Url}"), next.Depth + 1 - 10 * score);
                }
            }
        }

        foreach (var connector in connectors)
        {
            foreach (var guess in connector.GuessEntryPoints(homepage))
            {
                if (await ProbeAsync(connector, guess, "well-known host name", DiscoveryMethod.GuessedHost, cancellationToken) is { } guessed)
                {
                    return guessed;
                }
            }
        }

        if (_failedGuesses.Count > 0)
        {
            _evidence.Add($"guessed hosts that didn't confirm: {string.Join("; ", _failedGuesses)}");
        }

        var reason = _linkedProbeFailure
                     ?? (_hints.Count > 0 ? $"no connector recognized the site; its pages point to {string.Join(", ", _hints.Keys)}" : null)
                     ?? _guessedHostFailure
                     ?? $"no connector recognized the site after {_pagesFetched} pages and {_probes} probes";
        return Result(DiscoveryMethod.NotFound, platform: null, baseUrl: null, _bestConfidence, $"not identified from the homepage: {reason}");
    }

    /// <returns>The page, or null when it failed, wasn't HTML-like success, or redirected off the site; plus every redirect target.</returns>
    private async Task<(HttpFetchResult? Page, List<Uri> RedirectTargets)> FetchPageAsync(IHttpSession session,
                                                                                         Uri url,
                                                                                         string via,
                                                                                         HashSet<string> sites,
                                                                                         bool mayLeaveSite,
                                                                                         CancellationToken cancellationToken)
    {
        var redirectTargets = new List<Uri>();
        for (var hop = 0; hop <= options.MaxRedirects && _pagesFetched < options.MaxPages; hop++)
        {
            _pagesFetched++;
            HttpFetchResult response;
            try
            {
                response = await session.GetAsync(url, cancellationToken);
            }
            catch (ConnectorException exception)
            {
                _evidence.Add($"GET {url} ({via}) not sent or failed: {exception.Message}");
                return (null, redirectTargets);
            }

            if (response.RedirectLocation is { } location)
            {
                redirectTargets.Add(location);
                _evidence.Add($"GET {url} ({via}) → HTTP {response.StatusCode} to {location}");
                if (mayLeaveSite is false && sites.Any(site => RegistrableDomain.IsWithin(location, site)) is false)
                {
                    return (null, redirectTargets);
                }

                url = location;
                continue;
            }

            if (response.IsSuccess is false)
            {
                _evidence.Add($"GET {url} ({via}) → HTTP {response.StatusCode}");
                return (null, redirectTargets);
            }

            _evidence.Add($"GET {url} ({via}) → HTTP {response.StatusCode}, {response.Body.Length / 1024} KB");
            return (response, redirectTargets);
        }

        _evidence.Add($"GET {url} ({via}): stopped, the redirect or page limit was reached");
        return (null, redirectTargets);
    }

    private async Task<DiscoveryResult?> ProbeEntryPointsAsync(Uri pageUrl,
                                                               string html,
                                                               string how,
                                                               DiscoveryMethod method,
                                                               CancellationToken cancellationToken)
    {
        foreach (var connector in connectors)
        {
            foreach (var entryPoint in connector.FindEntryPoints(pageUrl, html))
            {
                if (await ProbeAsync(connector, entryPoint, how, method, cancellationToken) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private async Task<DiscoveryResult?> ProbeAsync(IConnector connector,
                                                    Uri entryPoint,
                                                    string how,
                                                    DiscoveryMethod method,
                                                    CancellationToken cancellationToken)
    {
        if (_probed.Add($"{connector.Platform} {entryPoint.AbsoluteUri}") is false)
        {
            return null;
        }

        _probes++;
        var guessed = method is DiscoveryMethod.GuessedHost;
        string failure;
        try
        {
            var fingerprint = await connector.FingerprintAsync(entryPoint, cancellationToken);
            if (fingerprint.Confidence >= options.MinConfidence && fingerprint.BaseUrl is { } baseUrl)
            {
                _evidence.Add($"{connector.Platform} entry point {entryPoint} ({how})");
                _evidence.AddRange(fingerprint.Evidence.Select(line => $"{connector.Platform} probe: {line}"));
                return Result(method, connector.Platform, baseUrl, fingerprint.Confidence, reviewReason: null);
            }

            _bestConfidence = Math.Max(_bestConfidence, fingerprint.Confidence);
            failure = fingerprint.Evidence.Count > 0 ? fingerprint.Evidence[^1] : $"confidence {fingerprint.Confidence}";
        }
        catch (ConnectorException exception)
        {
            failure = $"the probe was not sent or failed: {exception.Message}";
        }

        if (guessed)
        {
            _failedGuesses.Add($"{entryPoint.Authority} ({failure})");

            /* A guessed host whose robots.txt refused us may well be the platform, blocked: worth a person's look. One that
               doesn't resolve, or answers 404 or a redirect, just isn't this platform and would only hide better evidence. */
            if (failure.Contains("robots.txt") && failure.Contains(ConnectorException.UnresolvedHostReason) is false)
            {
                _guessedHostFailure ??= $"the well-known {connector.Platform} host {entryPoint.Authority} exists, but its "
                                        + $"robots.txt refused the probe: {failure}";
            }

            return null;
        }

        _evidence.Add($"{connector.Platform} entry point {entryPoint} ({how}) not confirmed: {failure}");
        _linkedProbeFailure ??= $"a {connector.Platform} entry point {entryPoint} was {how}, but it wasn't confirmed: {failure}";
        return null;
    }

    private DiscoveryResult Result(DiscoveryMethod method, string? platform, Uri? baseUrl, double confidence, string? reviewReason) =>
        new()
        {
            Method = method,
            Platform = platform,
            BaseUrl = baseUrl,
            Confidence = confidence,
            Evidence = [.. _evidence],
            PlatformHints = [.. _hints.Values],
            ReviewReason = reviewReason,
            PagesFetched = _pagesFetched,
            Probes = _probes,
        };

    private static string Shorten(string text) =>
        text.Length <= MaxLinkTextLength ? text : text[..MaxLinkTextLength] + "…";

    #endregion Methods
}
