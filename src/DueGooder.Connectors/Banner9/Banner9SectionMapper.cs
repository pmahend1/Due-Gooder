using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    /* Registrars put "not decided" values in building and room instead of leaving them empty, e.g. "TBA Tampa (TBAT)",
       "CHABOT - TBA", "Arranged Room", room "ARR", "Not Applicable", "None" (all seen in the 2026-09-12 run). A placeholder
       isn't a location, so the parsed field stays null while LocationRaw keeps the source codes. */
    private static readonly Regex LocationPlaceholder =
        new(@"\b(TBA|TBD|ARR|ARRNGD|ARRANGED|TO BE (ANNOUNCED|ARRANGED|DETERMINED))\b|^(NOT APPLICABLE|NONE|NA|N/A|NAPPL)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    #endregion State

    #region Methods

    public static Section Map(RawSection raw)
    {
        using var document = JsonDocument.Parse(raw.Payload);
        var json = document.RootElement;
        var failures = new List<ExtractionFailure>();
        var credits = ReadCredits(json);

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
            Credits = credits.Fixed,
            CreditsMin = credits.Min,
            CreditsMax = credits.Max,
            CreditsRaw = credits.Raw,
            InstructionalMethod = Decode(json.OptionalString("instructionalMethodDescription")),
            Campus = Decode(json.OptionalString("campusDescription")),
            Capacity = json.OptionalInt("maximumEnrollment"),
            Enrolled = json.OptionalInt("enrollment"),
            WaitlistCapacity = json.OptionalInt("waitCapacity"),
            WaitlistCount = json.OptionalInt("waitCount"),
            CrossListGroup = NonBlank(json.OptionalString("crossList")),
            CrossListCapacity = json.OptionalInt("crossListCapacity"),
            CrossListEnrolled = json.OptionalInt("crossListCount"),
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

    /* Variable credit is a low-high pair joined by creditHourIndicator ("OR" / "TO"), and creditHours then holds an
       arbitrary end of it: UCR publishes "0 OR 4" with creditHours 0. A range has no single value, so Credits stays null. */
    private static (decimal? Fixed, decimal? Min, decimal? Max, string? Raw) ReadCredits(JsonElement json)
    {
        var hours = json.OptionalDecimal("creditHours");
        var low = json.OptionalDecimal("creditHourLow");
        var high = json.OptionalDecimal("creditHourHigh");
        var indicator = NonBlank(json.OptionalString("creditHourIndicator"));
        if (low is { } rangeLow && high is { } rangeHigh && rangeLow != rangeHigh && (indicator is not null || hours is null))
        {
            var raw = indicator is null
                ? $"{FormatCredits(rangeLow)}-{FormatCredits(rangeHigh)}"
                : $"{FormatCredits(rangeLow)} {indicator} {FormatCredits(rangeHigh)}";
            return (null, Math.Min(rangeLow, rangeHigh), Math.Max(rangeLow, rangeHigh), raw);
        }

        return (hours ?? low ?? high) is { } value
            ? (value, value, value, FormatCredits(value))
            : (null, null, null, null);
    }

    private static string FormatCredits(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static List<Meeting> MapMeetings(JsonElement json, List<ExtractionFailure> failures)
    {
        var meetings = new List<Meeting>();
        var entry = 0;
        foreach (var meetingSession in json.OptionalArray("meetingsFaculty"))
        {
            entry++;
            if (meetingSession.TryGetProperty("meetingTime", out var time) is false || time.ValueKind is not JsonValueKind.Object)
            {
                failures.Add(new ExtractionFailure(nameof(Section.Meetings),
                                                   $"meetingsFaculty entry {entry} has no meetingTime object",
                                                   meetingSession.GetRawText()));
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
                Building = UnlessPlaceholder(Decode(time.OptionalString("buildingDescription") ?? time.OptionalString("building"))),
                Room = UnlessPlaceholder(time.OptionalString("room")),
                LocationRaw = ReadLocationRaw(time),
                StartDate = ParseDate(time.OptionalString("startDate"), $"{field}.{nameof(Meeting.StartDate)}", failures),
                EndDate = ParseDate(time.OptionalString("endDate"), $"{field}.{nameof(Meeting.EndDate)}", failures),
                MeetingType = Decode(time.OptionalString("meetingTypeDescription")),
            });
        }

        return meetings;
    }

    /* All seven flags false means Banner has no set days for the meeting: asynchronous online, TBA or by arrangement.
       Banner doesn't say which, so it maps to None (the source's answer), not null (not published). */
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

    private static string? UnlessPlaceholder(string? location) =>
        location is not null && LocationPlaceholder.IsMatch(location.Trim()) ? null : location;

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

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    #endregion Methods
}
