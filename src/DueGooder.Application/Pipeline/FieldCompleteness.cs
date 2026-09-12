using DueGooder.Domain;

namespace DueGooder.Application.Pipeline;

/// <summary>
/// How many collected sections and meetings carry each field. A field that's absent is either not published by the
/// school or failed to parse; parse failures are counted separately in <see cref="TermRunResult.ExtractionFailures"/>.
/// </summary>
public sealed record FieldCompleteness
{
    #region State

    public static readonly FieldCompleteness Empty = new();

    public int Sections { get; init; }

    public int WithTitle { get; init; }

    /// <summary>Sections with fixed credits or a credit range.</summary>
    public int WithCredits { get; init; }

    public int WithInstructionalMethod { get; init; }

    public int WithCampus { get; init; }

    public int WithCapacity { get; init; }

    public int WithEnrollment { get; init; }

    public int WithInstructor { get; init; }

    public int WithMeeting { get; init; }

    public int CrossListed { get; init; }

    public int Meetings { get; init; }

    /// <summary>Meetings on at least one set day; the rest have none (online, TBA or by arrangement).</summary>
    public int WithDays { get; init; }

    public int WithTimes { get; init; }

    /// <summary>Meetings with a building or room that isn't a placeholder like TBA.</summary>
    public int WithLocation { get; init; }

    public int WithDates { get; init; }

    #endregion State

    #region Methods

    public static FieldCompleteness Of(IReadOnlyCollection<Section> sections)
    {
        var meetings = sections.SelectMany(section => section.Meetings).ToList();
        return new FieldCompleteness
        {
            Sections = sections.Count,
            WithTitle = sections.Count(section => section.Title is not null),
            WithCredits = sections.Count(section => section.CreditsMin is not null),
            WithInstructionalMethod = sections.Count(section => section.InstructionalMethod is not null),
            WithCampus = sections.Count(section => section.Campus is not null),
            WithCapacity = sections.Count(section => section.Capacity is not null),
            WithEnrollment = sections.Count(section => section.Enrolled is not null),
            WithInstructor = sections.Count(section => section.Instructors.Count > 0),
            WithMeeting = sections.Count(section => section.Meetings.Count > 0),
            CrossListed = sections.Count(section => section.CrossListGroup is not null),
            Meetings = meetings.Count,
            WithDays = meetings.Count(meeting => meeting.Days is not (null or MeetingDays.None)),
            WithTimes = meetings.Count(meeting => meeting.StartTime is not null && meeting.EndTime is not null),
            WithLocation = meetings.Count(meeting => meeting.Building is not null || meeting.Room is not null),
            WithDates = meetings.Count(meeting => meeting.StartDate is not null && meeting.EndDate is not null),
        };
    }

    public static FieldCompleteness operator +(FieldCompleteness left, FieldCompleteness right) =>
        new()
        {
            Sections = left.Sections + right.Sections,
            WithTitle = left.WithTitle + right.WithTitle,
            WithCredits = left.WithCredits + right.WithCredits,
            WithInstructionalMethod = left.WithInstructionalMethod + right.WithInstructionalMethod,
            WithCampus = left.WithCampus + right.WithCampus,
            WithCapacity = left.WithCapacity + right.WithCapacity,
            WithEnrollment = left.WithEnrollment + right.WithEnrollment,
            WithInstructor = left.WithInstructor + right.WithInstructor,
            WithMeeting = left.WithMeeting + right.WithMeeting,
            CrossListed = left.CrossListed + right.CrossListed,
            Meetings = left.Meetings + right.Meetings,
            WithDays = left.WithDays + right.WithDays,
            WithTimes = left.WithTimes + right.WithTimes,
            WithLocation = left.WithLocation + right.WithLocation,
            WithDates = left.WithDates + right.WithDates,
        };

    #endregion Methods
}
