using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DueGooder.Application;
using DueGooder.Domain;

namespace DueGooder.Connectors.Banner9;

/// <summary>
/// Ellucian Banner 9 Student Registration Self-Service, shared by every Banner 9 school.
/// Class search is stateful: <c>term/search</c> binds a term to the cookie session, then
/// <c>searchResults</c> pages through that term's sections.
/// </summary>
/// <remarks>
/// Reads two <see cref="ConnectorTarget.Options"/>: <c>mep_code</c>, the institution code on servers
/// that host several campuses, and <c>page_size</c>, the number of sections per search page.
/// </remarks>
public sealed class Banner9Connector(IHttpFetcher fetcher) : IConnector
{
    #region State

    public const string MepCodeOption = "mep_code";

    public const string PageSizeOption = "page_size";

    // Fewer, larger pages mean fewer requests per term; 500 is the largest page Banner 9 servers commonly accept.
    private const int DefaultPageSize = 500;

    private const int TermPageSize = 100;

    private const string BannerPathSegment = "StudentRegistrationSsb";

    private const double ProbeMatchConfidence = 0.95;

    private const double PathOnlyConfidence = 0.3;

    public string Platform => "banner9";

    #endregion State

    #region Methods

    public async Task<Fingerprint> FingerprintAsync(Uri candidate, CancellationToken cancellationToken)
    {
        var evidence = new List<string>();
        var baseUrlFromPath = FindBaseUrlInPath(candidate);
        if (baseUrlFromPath is not null)
        {
            evidence.Add($"URL path contains {BannerPathSegment}");
        }

        var baseUrl = baseUrlFromPath ?? new Uri(candidate, $"/{BannerPathSegment}/");
        var probeUrl = new Banner9Endpoints(baseUrl, mepCode: null).Terms(page: 1, pageSize: 1);
        using var session = fetcher.OpenSession();
        var probe = await session.GetAsync(probeUrl, cancellationToken);
        if (probe.IsSuccess && TryReadTerms(probe.Body, out var terms) && terms.Count > 0)
        {
            evidence.Add($"GET {probe.Url} returned Banner term JSON, e.g. {terms[0].Code} \"{terms[0].Description}\"");
            return new Fingerprint(ProbeMatchConfidence, baseUrl, evidence);
        }

        evidence.Add($"GET {probe.Url} returned HTTP {probe.StatusCode} without Banner term JSON");
        return baseUrlFromPath is null
            ? new Fingerprint(0, null, evidence)
            : new Fingerprint(PathOnlyConfidence, baseUrlFromPath, evidence);
    }

    public async Task<IReadOnlyList<Term>> ListTermsAsync(ConnectorTarget target, CancellationToken cancellationToken)
    {
        var endpoints = EndpointsFor(target);
        using var session = fetcher.OpenSession();
        var terms = new List<Term>();
        var seenCodes = new HashSet<string>();
        for (var page = 1; ; page++)
        {
            var response = await session.GetAsync(endpoints.Terms(page, TermPageSize), cancellationToken);
            if (response.IsSuccess is false || TryReadTerms(response.Body, out var entries) is false)
            {
                throw new ConnectorException($"getTerms returned HTTP {response.StatusCode} without Banner term JSON",
                                             response.Url);
            }

            // A server that ignored the page number would otherwise return the same page forever.
            var newEntries = entries.Where(entry => seenCodes.Add(entry.Code)).ToList();
            terms.AddRange(newEntries.Select(entry => new Term
            {
                Key = new TermKey(target.School.Id, entry.Code),
                Name = entry.Description,
                SourceUrl = response.Url,
                RetrievedAt = response.RetrievedAt,
            }));

            if (entries.Count < TermPageSize || newEntries.Count is 0)
            {
                return terms;
            }
        }
    }

    public async IAsyncEnumerable<RawSection> CollectSectionsAsync(ConnectorTarget target,
                                                                   Term term,
                                                                   [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var endpoints = EndpointsFor(target);
        var pageSize = PageSizeFor(target);
        var sessionId = "dg" + Guid.NewGuid().ToString("N")[..12];
        using var session = fetcher.OpenSession();

        var termBinding = await session.PostFormAsync(endpoints.TermSearch(),
                                                      new Dictionary<string, string>
                                                      {
                                                          ["term"] = term.Key.TermCode,
                                                          ["studyPath"] = "",
                                                          ["studyPathText"] = "",
                                                          ["startDatepicker"] = "",
                                                          ["endDatepicker"] = "",
                                                          ["uniqueSessionId"] = sessionId,
                                                      },
                                                      cancellationToken);
        EnsureTermBound(termBinding);

        try
        {
            for (var pageOffset = 0; ; pageOffset += pageSize)
            {
                var url = endpoints.SearchResults(term.Key.TermCode, sessionId, pageOffset, pageSize);
                var response = await session.GetAsync(url, cancellationToken);
                var (totalCount, sections) = ReadSearchPage(response);
                foreach (var payload in sections)
                {
                    yield return new RawSection(term.Key, response.Url, response.RetrievedAt, payload);
                }

                if (sections.Count is 0 || pageOffset + pageSize >= totalCount)
                {
                    break;
                }
            }
        }
        finally
        {
            // Clears the term binding so the next term searched on this server starts from a clean form.
            await session.PostFormAsync(endpoints.ResetDataForm(),
                                        new Dictionary<string, string>
                                        {
                                            ["resetCourses"] = "false",
                                            ["resetSections"] = "true",
                                        },
                                        CancellationToken.None);
        }
    }

    public Section Map(RawSection raw) => Banner9SectionMapper.Map(raw);

    private static Banner9Endpoints EndpointsFor(ConnectorTarget target) =>
        new(target.BaseUrl, target.Options.GetValueOrDefault(MepCodeOption));

    private static int PageSizeFor(ConnectorTarget target)
    {
        if (target.Options.TryGetValue(PageSizeOption, out var configured) is false)
        {
            return DefaultPageSize;
        }

        if (int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize) && pageSize > 0)
        {
            return pageSize;
        }

        throw new ConnectorException($"{PageSizeOption} '{configured}' in config is not a positive whole number");
    }

    // Keeps the server's own casing: UIUC serves StudentRegistrationSSB, most schools StudentRegistrationSsb.
    private static Uri? FindBaseUrlInPath(Uri candidate)
    {
        var path = candidate.AbsolutePath;
        var start = path.IndexOf("/" + BannerPathSegment, StringComparison.OrdinalIgnoreCase);
        return start is -1
            ? null
            : new Uri(candidate, path[..(start + 1 + BannerPathSegment.Length)] + "/");
    }

    private static void EnsureTermBound(HttpFetchResult response)
    {
        if (response.StatusCode is >= 300 and < 400)
        {
            throw new ConnectorException($"term/search redirected (HTTP {response.StatusCode}), so class search needs a sign-in; "
                                         + "only public data is collected",
                                         response.Url);
        }

        if (response.IsSuccess is false)
        {
            throw new ConnectorException($"term/search returned HTTP {response.StatusCode}", response.Url);
        }
    }

    private static bool TryReadTerms(string body, out List<(string Code, string Description)> terms)
    {
        terms = [];
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                return false;
            }

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind is not JsonValueKind.Object || entry.OptionalString("code") is not { Length: > 0 } code)
                {
                    return false;
                }

                terms.Add((code, WebUtility.HtmlDecode(entry.OptionalString("description") ?? "")));
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static (int TotalCount, List<string> Sections) ReadSearchPage(HttpFetchResult response)
    {
        if (response.IsSuccess is false)
        {
            throw new ConnectorException($"searchResults returned HTTP {response.StatusCode}", response.Url);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Body);
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object || root.OptionalBool("success") is not true)
            {
                throw new ConnectorException("searchResults did not report success", response.Url);
            }

            var sections = root.OptionalArray("data").Select(section => section.GetRawText()).ToList();
            return (root.OptionalInt("totalCount") ?? 0, sections);
        }
        catch (JsonException exception)
        {
            throw new ConnectorException("searchResults did not return JSON", response.Url, exception);
        }
    }

    #endregion Methods
}
