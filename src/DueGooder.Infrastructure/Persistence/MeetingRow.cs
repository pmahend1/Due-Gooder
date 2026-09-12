using DueGooder.Domain;

namespace DueGooder.Infrastructure.Persistence;

/// <summary>EF Core row for one <see cref="DueGooder.Domain.Meeting"/> of a <see cref="SectionRow"/>.</summary>
internal sealed class MeetingRow
{
    #region State

    public int Id { get; set; }

    public int SectionRowId { get; set; }

    public MeetingDays? Days { get; set; }

    public string? DaysRaw { get; set; }

    public TimeOnly? StartTime { get; set; }

    public string? StartTimeRaw { get; set; }

    public TimeOnly? EndTime { get; set; }

    public string? EndTimeRaw { get; set; }

    public string? Building { get; set; }

    public string? Room { get; set; }

    public string? LocationRaw { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public string? MeetingType { get; set; }

    #endregion State
}
