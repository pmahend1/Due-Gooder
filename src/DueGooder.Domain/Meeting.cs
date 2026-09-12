namespace DueGooder.Domain;

/// <summary>
/// One meeting pattern of a section. Each parsed value sits next to the raw source text
/// it came from, so a placeholder like <c>TBA</c> survives and accuracy can be audited.
/// Times are wall-clock times in the school's <see cref="School.TimeZoneId"/>.
/// </summary>
public sealed record Meeting
{
    #region State

    public MeetingDays? Days { get; init; }

    public string? DaysRaw { get; init; }

    public TimeOnly? StartTime { get; init; }

    public string? StartTimeRaw { get; init; }

    public TimeOnly? EndTime { get; init; }

    public string? EndTimeRaw { get; init; }

    public string? Building { get; init; }

    public string? Room { get; init; }

    public string? LocationRaw { get; init; }

    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    /// <summary>Kind of meeting as published, e.g. <c>Lecture</c> or <c>Lab</c>.</summary>
    public string? MeetingType { get; init; }

    #endregion State
}
