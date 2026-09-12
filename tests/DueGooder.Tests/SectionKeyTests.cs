using DueGooder.Domain;

namespace DueGooder.Tests;

public sealed class SectionKeyTests
{
    #region Methods

    [Fact]
    public void Sections_collected_on_different_runs_share_a_key()
    {
        var key = new SectionKey("uky", "202610", "CS", "115", "001");
        var firstRun = NewSection(key, new DateTimeOffset(2026, 9, 11, 20, 0, 0, TimeSpan.FromHours(-4)));
        var secondRun = NewSection(key, new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal(firstRun.Key, secondRun.Key);
        Assert.Equal(new TermKey("uky", "202610"), firstRun.Key.Term);
        Assert.Equal(new CourseKey("uky", "CS", "115"), firstRun.Key.Course);
    }

    [Fact]
    public void Retrieved_at_is_stored_as_utc()
    {
        var section = NewSection(new SectionKey("uky", "202610", "CS", "115", "001"),
                                 new DateTimeOffset(2026, 9, 11, 20, 0, 0, TimeSpan.FromHours(-4)));

        Assert.Equal(TimeSpan.Zero, section.RetrievedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 12, 0, 0, 0, TimeSpan.Zero), section.RetrievedAt);
    }

    private static Section NewSection(SectionKey key, DateTimeOffset retrievedAt) =>
        new()
        {
            Key = key,
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/"),
            RetrievedAt = retrievedAt,
        };

    #endregion Methods
}
