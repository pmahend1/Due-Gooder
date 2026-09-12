using System.Globalization;
using DueGooder.Application;
using DueGooder.Connectors.Banner9;
using DueGooder.Domain;

namespace DueGooder.Tests;

/// <summary>Schools with recorded Banner 9 fixtures, set up the way <c>config/schools.yaml</c> sets them up.</summary>
internal static class Banner9Fixtures
{
    #region State

    // The search fixtures were captured with pageMaxSize=10, two pages per school.
    public const int PageSize = 10;

    public const int CapturedSections = 2 * PageSize;

    #endregion State

    #region Methods

    public static ConnectorTarget Target(string schoolId) => schoolId switch
    {
        "eku" => NewTarget(schoolId, "https://registrationss.eku.edu/StudentRegistrationSsb/", mepCode: null),
        "sunyempire" => NewTarget(schoolId, "https://banner.esc.edu/StudentRegistrationSsb/", mepCode: null),
        "uiuc" => NewTarget(schoolId, "https://banner.apps.uillinois.edu/StudentRegistrationSSB/", mepCode: "1UIUC"),
        "kccd" => NewTarget(schoolId, "https://reg-prod.ec.kccd.edu/StudentRegistrationSsb/", mepCode: null),
        "oakland" => NewTarget(schoolId, "https://bstreg.oakland.edu/StudentRegistrationSsb/", mepCode: null),
        "odu" => NewTarget(schoolId, "https://reg-prod.ec.odu.edu/StudentRegistrationSsb/", mepCode: null),
        _ => throw new ArgumentOutOfRangeException(nameof(schoolId), schoolId, "No Banner 9 fixtures for this school"),
    };

    /// <summary>The term each school's search fixtures were captured for (Fall 2026 at every school).</summary>
    public static Term CapturedTerm(string schoolId)
    {
        var termCode = schoolId switch
        {
            "eku" => "202710",
            "sunyempire" => "202680",
            "uiuc" => "120268",
            "kccd" => "202670",
            "oakland" => "202640",
            "odu" => "202610",
            _ => throw new ArgumentOutOfRangeException(nameof(schoolId), schoolId, "No Banner 9 fixtures for this school"),
        };

        return new Term
        {
            Key = new TermKey(schoolId, termCode),
            Name = "Fall 2026",
            SourceUrl = Target(schoolId).BaseUrl,
            RetrievedAt = FixtureHttpFetcher.RetrievedAt,
        };
    }

    public static async Task<List<RawSection>> CollectCapturedSectionsAsync(string schoolId)
    {
        var connector = new Banner9Connector(new FixtureHttpFetcher(schoolId));
        return await connector.CollectSectionsAsync(Target(schoolId), CapturedTerm(schoolId), CancellationToken.None)
                              .Take(CapturedSections)
                              .ToListAsync();
    }

    private static ConnectorTarget NewTarget(string schoolId, string baseUrl, string? mepCode)
    {
        var options = new Dictionary<string, string>
        {
            [Banner9Connector.PageSizeOption] = PageSize.ToString(CultureInfo.InvariantCulture),
        };
        if (mepCode is not null)
        {
            options[Banner9Connector.MepCodeOption] = mepCode;
        }

        var school = new School
        {
            Id = schoolId,
            Name = schoolId,
            Homepage = new Uri(baseUrl),
            TimeZoneId = "America/New_York",
        };
        return new ConnectorTarget(school, new Uri(baseUrl)) { Options = options };
    }

    #endregion Methods
}
