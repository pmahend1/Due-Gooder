using System.Globalization;
using System.Net;
using System.Text.Json;
using DueGooder.Application;
using DueGooder.Domain;

namespace DueGooder.Connectors.Banner9;

/// <summary>Maps one Banner 9 <c>searchResults</c> section record to a normalized <see cref="Section"/>.</summary>
internal static class Banner9SectionMapper
{
    #region State

    private const string BannerTimeFormat = "HHmm";

    private const string BannerDateFormat = "MM/dd/yyyy";

    // Banner publishes one boolean per day; the letters are the registrar convention for the raw value.
    private static readonly (string Property, MeetingDays Day, char Letter)[] DayFields =
    [
        ("monday", MeetingDays.Monday, 'M'),
        ("tuesday", MeetingDays.Tuesday, 'T'),
        ("wednesday", MeetingDays.Wednesday, 'W'),
        ("thursday", MeetingDays.Thursday, 'R'),
        ("friday", MeetingDays.Friday, 'F'),
        ("saturday", MeetingDays.Saturday, 'S'),
        ("sunday", MeetingDays.Sunday, 'U'),
    ];

    #endregion State

    #region Methods

    public static Section Map(RawSection raw)
    {
        using var document = JsonDocument.Parse(raw.Payload);
        var json = document.RootElement;
        var failures = new List<ExtractionFailure>();

        return new Section
        {
            Key = new SectionKey(raw.Term.SchoolId,
                                 raw.Term.TermCode,
                                 RequiredKeyPart(json, "subject", raw),
                                 RequiredKeyPart(json, "courseNumber", raw),
                                 RequiredKeyPart(json, "courseReferenceNumber", raw)),
            SourceSectionId = json.OptionalString("courseReferenceNumber"),
            DisplaySectionNumber = json.OptionalString("sequenceNumber"),
            Title = Decode(json.OptionalString("courseTitle")),
            Credits = ReadCredits(json),
            InstructionalMethod = Decode(json.OptionalString("instructionalMethodDescription")),
            Campus = Decode(json.OptionalString("campusDescription")),
            Capacity = json.OptionalInt("maximumEnrollment"),
            Enrolled = json.OptionalInt("enrollment"),
            WaitlistCapacity = json.OptionalInt("waitCapacity"),
            WaitlistCount = json.OptionalInt("waitCount"),
            Meetings = MapMeetings(json, failures),
            Instructors = MapInstructors(json),
            Failures = failures,
            SourceUrl = raw.SourceUrl,
            RetrievedAt = raw.RetrievedAt,
        };
    }

    // Without these the record has no natural key, so it can't be stored at all: that's a connector failure, not a field failure.
    private static string RequiredKeyPart(JsonElement json, string property, RawSection raw) =>
        json.OptionalString(property) is { Length: > 0 } value
            ? value
            : throw new ConnectorException($"Banner 9 section record has no {property}, so it has no natural key", raw.SourceUrl);

    // A variable-credit section publishes a low-high range and no creditHours; a range isn't a single value, so it stays null.
    private static decimal? ReadCredits(JsonElement json) =>
        json.OptionalDecimal("creditHours")
        ?? (json.OptionalDecimal("creditHourHigh") is null ? json.OptionalDecimal("creditHourLow") : null);

    private static List<Meeting> MapMeetings(JsonElement json, List<ExtractionFailure> failures)
    {
        var meetings = new List<Meeting>();
        foreach (var meetingSession in json.OptionalArray("meetingsFaculty"))
        {
            if (meetingSession.TryGetProperty("meetingTime", out var time) is false || time.ValueKind is not JsonValueKind.Object)
            {
                continue;
            }

            var field = $"{nameof(Section.Meetings)}[{meetings.Count}]";
            var startRaw = time.OptionalString("beginTime");
            var endRaw = time.OptionalString("endTime");
            var (days, daysRaw) = ReadDays(time);
            meetings.Add(new Meeting
            {
                Days = days,
                DaysRaw = daysRaw,
                StartTime = ParseTime(startRaw, $"{field}.{nameof(Meeting.StartTime)}", failures),
                StartTimeRaw = startRaw,
                EndTime = ParseTime(endRaw, $"{field}.{nameof(Meeting.EndTime)}", failures),
                EndTimeRaw = endRaw,
                Building = Decode(time.OptionalString("buildingDescription") ?? time.OptionalString("building")),
                Room = time.OptionalString("room"),
                LocationRaw = ReadLocationRaw(time),
                StartDate = ParseDate(time.OptionalString("startDate"), $"{field}.{nameof(Meeting.StartDate)}", failures),
                EndDate = ParseDate(time.OptionalString("endDate"), $"{field}.{nameof(Meeting.EndDate)}", failures),
                MeetingType = Decode(time.OptionalString("meetingTypeDescription")),
            });
        }

        return meetings;
    }

    // All seven flags false is Banner's way of saying "no set days" (asynchronous online), so it maps to None, not null.
    private static (MeetingDays Days, string Raw) ReadDays(JsonElement time)
    {
        var days = MeetingDays.None;
        var letters = new List<char>();
        foreach (var (property, day, letter) in DayFields)
        {
            if (time.OptionalBool(property) is true)
            {
                days |= day;
                letters.Add(letter);
            }
        }

        return (days, new string([.. letters]));
    }

    private static string? ReadLocationRaw(JsonElement time)
    {
        var parts = new[] { time.OptionalString("building"), time.OptionalString("room") }
            .Where(part => part is not null);
        var location = string.Join(' ', parts);
        return location is "" ? null : location;
    }

    private static TimeOnly? ParseTime(string? raw, string field, List<ExtractionFailure> failures)
    {
        if (raw is null)
        {
            return null;
        }

        if (TimeOnly.TryParseExact(raw, BannerTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        failures.Add(new ExtractionFailure(field, $"Expected a 24-hour {BannerTimeFormat} time", raw));
        return null;
    }

    private static DateOnly? ParseDate(string? raw, string field, List<ExtractionFailure> failures)
    {
        if (raw is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(raw, BannerDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        failures.Add(new ExtractionFailure(field, $"Expected a {BannerDateFormat} date", raw));
        return null;
    }

    private static List<Instructor> MapInstructors(JsonElement json) =>
        json.OptionalArray("faculty")
            .Where(faculty => faculty.OptionalString("displayName") is not null)
            .Select(faculty => new Instructor
            {
                Name = Decode(faculty.OptionalString("displayName"))!,
                Email = faculty.OptionalString("emailAddress"),
                IsPrimary = faculty.OptionalBool("primaryIndicator") is true,
            })
            .ToList();

    // Banner HTML-encodes text inside its JSON, e.g. "Fundamentals &amp; Methods".
    private static string? Decode(string? value) => value is null ? null : WebUtility.HtmlDecode(value);

    #endregion Methods
}
