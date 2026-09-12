using DueGooder.Application.Pipeline;
using DueGooder.Domain;

namespace DueGooder.Tests;

public sealed class FieldCompletenessTests
{
    #region Methods

    [Fact]
    public void Counts_published_fields_and_treats_no_set_days_as_no_days()
    {
        var inPerson = new Meeting
        {
            Days = MeetingDays.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(10, 0),
            Building = "Hall",
        };
        var asynchronous = new Meeting { Days = MeetingDays.None };

        var fields = FieldCompleteness.Of([NewSection("1", creditsMin: 3m, inPerson, asynchronous), NewSection("2", creditsMin: null)]);

        Assert.Equal((2, 2, 1, 1), (fields.Sections, fields.WithTitle, fields.WithCredits, fields.WithMeeting));
        Assert.Equal((2, 1, 1, 1, 0), (fields.Meetings, fields.WithDays, fields.WithTimes, fields.WithLocation, fields.WithDates));
        Assert.Equal(4, (fields + fields).Sections);
    }

    private static Section NewSection(string crn, decimal? creditsMin, params Meeting[] meetings) =>
        new()
        {
            Key = new SectionKey("school", "202710", "ART", "101", crn),
            Title = "Drawing",
            CreditsMin = creditsMin,
            Meetings = meetings,
            SourceUrl = new Uri("https://example.edu/"),
            RetrievedAt = DateTimeOffset.UnixEpoch,
        };

    #endregion Methods
}
