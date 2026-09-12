using System.Text.Json.Nodes;
using DueGooder.Application;
using DueGooder.Connectors.Banner9;
using DueGooder.Domain;

namespace DueGooder.Tests;

public sealed class Banner9MappingTests
{
    #region State

    private static readonly Banner9Connector Connector = new(new FixtureHttpFetcher("eku"));

    #endregion State

    #region Methods

    [Fact]
    public async Task Maps_a_face_to_face_section_with_its_meeting_and_instructor()
    {
        var section = Connector.Map(await CapturedSectionAsync("eku", crn: "11457"));

        Assert.Equal(new SectionKey("eku", "202710", "ARH", "393", "11457"), section.Key);
        Assert.Equal("11457", section.SourceSectionId);
        Assert.Equal("001", section.DisplaySectionNumber);
        Assert.Equal("Survey of Global Art History II", section.Title);
        Assert.Equal(3m, section.Credits);
        Assert.Equal("Traditional Face-to-Face", section.InstructionalMethod);
        Assert.Equal("Richmond", section.Campus);
        Assert.Equal(30, section.Capacity);
        Assert.Equal(30, section.Enrolled);
        Assert.Equal(0, section.WaitlistCapacity);
        Assert.Equal(0, section.WaitlistCount);
        Assert.Empty(section.Failures);
        Assert.Contains("txt_term=202710", section.SourceUrl.Query);
        Assert.Equal(FixtureHttpFetcher.RetrievedAt, section.RetrievedAt);
        Assert.Equal(new Instructor { Name = "Amanda Strasik", Email = "Amanda.Strasik@eku.edu", IsPrimary = true },
                     Assert.Single(section.Instructors));
        Assert.Equal(new Meeting
                     {
                         Days = MeetingDays.Monday | MeetingDays.Wednesday,
                         DaysRaw = "MW",
                         StartTime = new TimeOnly(9, 30),
                         StartTimeRaw = "0930",
                         EndTime = new TimeOnly(10, 50),
                         EndTimeRaw = "1050",
                         Building = "Campbell Building",
                         Room = "239",
                         LocationRaw = "CAMP 239",
                         StartDate = new DateOnly(2026, 8, 24),
                         EndDate = new DateOnly(2026, 12, 12),
                         MeetingType = "Class",
                     },
                     Assert.Single(section.Meetings));
    }

    [Fact]
    public async Task An_asynchronous_online_section_has_no_set_days_and_no_times_without_failing()
    {
        var section = Connector.Map(await CapturedSectionAsync("eku", crn: "12426"));

        Assert.Equal("100 pct Online: Asynchronous", section.InstructionalMethod);
        Assert.Empty(section.Failures);
        Assert.Equal(new Meeting
                     {
                         Days = MeetingDays.None,
                         DaysRaw = "",
                         Building = "Internet Classes",
                         LocationRaw = "WEB",
                         StartDate = new DateOnly(2026, 8, 24),
                         EndDate = new DateOnly(2026, 12, 12),
                         MeetingType = "Class",
                     },
                     Assert.Single(section.Meetings));
    }

    [Fact]
    public async Task Keeps_every_meeting_of_a_section_that_meets_in_several_patterns()
    {
        var section = Connector.Map(await CapturedSectionAsync("sunyempire", crn: "83404"));

        Assert.Equal(new SectionKey("sunyempire", "202680", "ACCT", "2005", "83404"), section.Key);
        Assert.Equal("Goodwin, Valerie", Assert.Single(section.Instructors).Name);
        Assert.Empty(section.Failures);
        Assert.Equal(4, section.Meetings.Count);
        Assert.Equal([new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 24), new DateOnly(2026, 11, 21)],
                     section.Meetings.Take(3).Select(meeting => meeting.StartDate));
        Assert.All(section.Meetings.Take(3),
                   residency =>
                   {
                       Assert.Equal(MeetingDays.Saturday, residency.Days);
                       Assert.Equal(new TimeOnly(9, 0), residency.StartTime);
                       Assert.Equal(new TimeOnly(10, 30), residency.EndTime);
                       Assert.Equal("Virtual Meeting", residency.Building);
                       Assert.Equal("Residency", residency.MeetingType);
                   });
        var independentStudy = section.Meetings[3];
        Assert.Equal(MeetingDays.None, independentStudy.Days);
        Assert.Null(independentStudy.StartTime);
        Assert.Equal("COLWDE", independentStudy.LocationRaw);
    }

    [Theory]
    [InlineData("eku")]
    [InlineData("sunyempire")]
    [InlineData("kccd")]
    [InlineData("oakland")]
    [InlineData("odu")]
    public async Task Every_captured_section_maps_to_a_distinct_key_without_failures(string schoolId)
    {
        var sections = (await Banner9Fixtures.CollectCapturedSectionsAsync(schoolId)).Select(Connector.Map).ToList();

        Assert.Equal(Banner9Fixtures.CapturedSections, sections.Select(section => section.Key).Distinct().Count());
        Assert.All(sections,
                   section =>
                   {
                       Assert.Empty(section.Failures);
                       Assert.NotNull(section.Title);
                       Assert.NotEmpty(section.Meetings);
                   });
    }

    [Fact]
    public async Task An_unparseable_time_is_recorded_as_a_failure_with_its_raw_text()
    {
        var raw = await CapturedSectionAsync("eku", crn: "11457");
        var payload = JsonNode.Parse(raw.Payload)!;
        payload["meetingsFaculty"]![0]!["meetingTime"]!["beginTime"] = "9:30am";

        var section = Connector.Map(raw with { Payload = payload.ToJsonString() });

        var meeting = Assert.Single(section.Meetings);
        Assert.Null(meeting.StartTime);
        Assert.Equal("9:30am", meeting.StartTimeRaw);
        Assert.Equal(new TimeOnly(10, 50), meeting.EndTime);
        var failure = Assert.Single(section.Failures);
        Assert.Equal("Meetings[0].StartTime", failure.Field);
        Assert.Equal("9:30am", failure.RawValue);
    }

    [Fact]
    public async Task A_record_without_a_subject_is_reported_because_it_has_no_natural_key()
    {
        var raw = await CapturedSectionAsync("eku", crn: "11457");
        var payload = JsonNode.Parse(raw.Payload)!.AsObject();
        payload.Remove("subject");

        var exception = Assert.Throws<ConnectorException>(() => Connector.Map(raw with { Payload = payload.ToJsonString() }));

        Assert.Contains("subject", exception.Message);
        Assert.Equal(raw.SourceUrl, exception.SourceUrl);
    }

    private static async Task<RawSection> CapturedSectionAsync(string schoolId, string crn) =>
        (await Banner9Fixtures.CollectCapturedSectionsAsync(schoolId))
            .Single(raw => JsonNode.Parse(raw.Payload)?["courseReferenceNumber"]?.GetValue<string>() == crn);

    #endregion Methods
}
