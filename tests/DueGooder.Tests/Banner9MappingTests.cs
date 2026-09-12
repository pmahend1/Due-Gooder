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
        Assert.Equal((3m, 3m, "3"), (section.CreditsMin, section.CreditsMax, section.CreditsRaw));
        Assert.Null(section.CrossListGroup);
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

    [Fact]
    public async Task Variable_credit_keeps_its_range_and_published_text_instead_of_one_end_of_it()
    {
        var raw = await CapturedSectionAsync("eku", crn: "11457");
        var payload = JsonNode.Parse(raw.Payload)!;
        // How UCR publishes variable credit: creditHours holds the low end of "0 OR 4".
        payload["creditHours"] = 0;
        payload["creditHourLow"] = 0;
        payload["creditHourHigh"] = 4;
        payload["creditHourIndicator"] = "OR";

        var section = Connector.Map(raw with { Payload = payload.ToJsonString() });

        Assert.Null(section.Credits);
        Assert.Equal((0m, 4m, "0 OR 4"), (section.CreditsMin, section.CreditsMax, section.CreditsRaw));
        Assert.Empty(section.Failures);
    }

    [Fact]
    public async Task A_cross_listed_section_keeps_its_group_and_shared_seats_under_its_own_key()
    {
        var section = Connector.Map(await CapturedSectionAsync("odu", crn: "15268"));

        Assert.Equal(new SectionKey("odu", section.Key.TermCode, section.Key.Subject, section.Key.CourseNumber, "15268"),
                     section.Key);
        Assert.Equal("BU102", section.CrossListGroup);
        Assert.Equal(30, section.CrossListCapacity);
        Assert.Equal(3, section.CrossListEnrolled);
        Assert.Null(section.Credits);
        Assert.Equal((1.5m, 3m, "1.5 TO 3"), (section.CreditsMin, section.CreditsMax, section.CreditsRaw));
    }

    [Theory]
    [InlineData("TBA Tampa (TBAT)")]
    [InlineData("CHABOT - TBA")]
    [InlineData("To Be Arranged")]
    [InlineData("Arranged Room")]
    [InlineData("Not Applicable")]
    [InlineData("None")]
    public async Task A_placeholder_building_is_not_a_location_but_its_raw_codes_are_kept(string buildingDescription)
    {
        var meeting = await MapWithLocationAsync("TBAT", buildingDescription, room: "TBA");

        Assert.Null(meeting.Building);
        Assert.Null(meeting.Room);
        Assert.Equal("TBAT TBA", meeting.LocationRaw);
    }

    [Theory]
    [InlineData("Tarrant Hall")]
    [InlineData("Off Campus Location")]
    public async Task A_real_building_with_a_room_to_be_arranged_keeps_the_building(string buildingDescription)
    {
        var meeting = await MapWithLocationAsync("KC", buildingDescription, room: "ARR");

        Assert.Equal(buildingDescription, meeting.Building);
        Assert.Null(meeting.Room);
        Assert.Equal("KC ARR", meeting.LocationRaw);
    }

    [Fact]
    public async Task A_meeting_entry_without_a_meeting_time_is_recorded_as_a_failure_not_dropped()
    {
        var raw = await CapturedSectionAsync("eku", crn: "11457");
        var payload = JsonNode.Parse(raw.Payload)!;
        payload["meetingsFaculty"]![0]!["meetingTime"] = null;

        var section = Connector.Map(raw with { Payload = payload.ToJsonString() });

        Assert.Empty(section.Meetings);
        var failure = Assert.Single(section.Failures);
        Assert.Equal("Meetings", failure.Field);
        Assert.Contains("no meetingTime", failure.Reason);
        Assert.NotNull(failure.RawValue);
    }

    private static async Task<Meeting> MapWithLocationAsync(string building, string buildingDescription, string room)
    {
        var raw = await CapturedSectionAsync("eku", crn: "11457");
        var payload = JsonNode.Parse(raw.Payload)!;
        var time = payload["meetingsFaculty"]![0]!["meetingTime"]!;
        time["building"] = building;
        time["buildingDescription"] = buildingDescription;
        time["room"] = room;
        return Assert.Single(Connector.Map(raw with { Payload = payload.ToJsonString() }).Meetings);
    }

    private static async Task<RawSection> CapturedSectionAsync(string schoolId, string crn) =>
        (await Banner9Fixtures.CollectCapturedSectionsAsync(schoolId))
            .Single(raw => JsonNode.Parse(raw.Payload)?["courseReferenceNumber"]?.GetValue<string>() == crn);

    #endregion Methods
}
