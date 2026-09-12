namespace DueGooder.Connectors.Banner9;

/// <summary>Banner 9 Student Registration Self-Service URLs for one school.</summary>
internal sealed class Banner9Endpoints
{
    #region State

    private readonly Uri _baseUrl;

    private readonly string? _mepCode;

    #endregion State

    #region Methods

    public Banner9Endpoints(Uri baseUrl, string? mepCode)
    {
        // Relative URLs resolve against the last '/', so without a trailing slash the base would lose its final segment.
        _baseUrl = baseUrl.AbsoluteUri.EndsWith('/') ? baseUrl : new Uri(baseUrl.AbsoluteUri + "/");
        _mepCode = mepCode is null or "" ? null : Uri.EscapeDataString(mepCode);
    }

    /// <summary>One page of terms. <paramref name="page"/> is a 1-based page number, not a row offset.</summary>
    public Uri Terms(int page, int pageSize) =>
        Build($"ssb/classSearch/getTerms?searchTerm=&offset={page}&max={pageSize}");

    public Uri TermSearch() => Build("ssb/term/search?mode=search");

    // A fixed sort order keeps page boundaries stable, so no section is skipped or repeated between pages.
    public Uri SearchResults(string termCode, string sessionId, int pageOffset, int pageSize) =>
        Build("ssb/searchResults/searchResults"
              + $"?txt_term={Uri.EscapeDataString(termCode)}&startDatepicker=&endDatepicker="
              + $"&uniqueSessionId={sessionId}&pageOffset={pageOffset}&pageMaxSize={pageSize}"
              + "&sortColumn=subjectDescription&sortDirection=asc");

    public Uri ResetDataForm() => Build("ssb/classSearch/resetDataForm");

    // A server hosting several institutions needs mepCode on every request, or it answers HTTP 500.
    private Uri Build(string relativeUrl)
    {
        if (_mepCode is null)
        {
            return new Uri(_baseUrl, relativeUrl);
        }

        var separator = relativeUrl.Contains('?') ? '&' : '?';
        return new Uri(_baseUrl, $"{relativeUrl}{separator}mepCode={_mepCode}");
    }

    #endregion Methods
}
