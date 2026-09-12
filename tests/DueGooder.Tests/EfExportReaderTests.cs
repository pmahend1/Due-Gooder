using DueGooder.Domain;
using DueGooder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DueGooder.Tests;

public sealed class EfExportReaderTests
{
    #region State

    private static readonly DateTimeOffset At = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    #endregion State

    #region Methods

    [Fact]
    public async Task Reads_back_a_stored_section_with_its_term_name_and_confirmation_time()
    {
        using var connection = OpenConnection();
        var term = new Term
        {
            Key = new TermKey("eku", "202710"),
            Name = "Fall 2026",
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/"),
            RetrievedAt = At,
        };
        var section = new Section
        {
            Key = new SectionKey("eku", "202710", "ARH", "393", "11457"),
            DisplaySectionNumber = "001",
            Title = "Survey of Global Art History II",
            Meetings = [new Meeting { Days = MeetingDays.Monday, DaysRaw = "M", StartTime = new TimeOnly(9, 30) }],
            Instructors = [new Instructor { Name = "Amanda Strasik", IsPrimary = true }],
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/?txt_term=202710"),
            RetrievedAt = At,
        };
        await new EfSectionRepository(() => NewContext(connection)).UpsertAsync(term, [section], [], At, CancellationToken.None);

        var exported = Assert.Single(await new EfExportReader(() => NewContext(connection))
            .GetSectionsAsync(null, CancellationToken.None));

        Assert.Equal(section.Key, exported.Section.Key);
        Assert.Equal("Fall 2026", exported.TermName);
        Assert.Equal(At, exported.LastConfirmedAt);
        Assert.Equal("Amanda Strasik", Assert.Single(exported.Section.Instructors).Name);
        Assert.Equal(MeetingDays.Monday, Assert.Single(exported.Section.Meetings).Days);
    }

    [Fact]
    public async Task Narrows_to_the_requested_schools()
    {
        using var connection = OpenConnection();
        var repository = new EfSectionRepository(() => NewContext(connection));
        await repository.UpsertAsync(NewTerm("eku"), [NewSection("eku")], [], At, CancellationToken.None);
        await repository.UpsertAsync(NewTerm("uncc"), [NewSection("uncc")], [], At, CancellationToken.None);

        var onlyUncc = await new EfExportReader(() => NewContext(connection))
            .GetSectionsAsync(new HashSet<string> { "uncc" }, CancellationToken.None);

        var exported = Assert.Single(onlyUncc);
        Assert.Equal("uncc", exported.Section.Key.SchoolId);
    }

    private static SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        return connection;
    }

    private static DueGooderDbContext NewContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<DueGooderDbContext>().UseSqlite(connection).Options;
        var context = new DueGooderDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    private static Term NewTerm(string schoolId) =>
        new()
        {
            Key = new TermKey(schoolId, "202710"),
            Name = "Fall 2026",
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/"),
            RetrievedAt = At,
        };

    private static Section NewSection(string schoolId) =>
        new()
        {
            Key = new SectionKey(schoolId, "202710", "ARH", "393", "11457"),
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/?txt_term=202710"),
            RetrievedAt = At,
        };

    #endregion Methods
}
