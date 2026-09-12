using DueGooder.Domain;
using DueGooder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DueGooder.Tests;

public sealed class EfSectionRepositoryTests
{
    #region Methods

    [Fact]
    public async Task Upserting_the_same_data_twice_writes_nothing_on_the_second_run()
    {
        using var connection = OpenConnection();
        await using var db = NewContext(connection);
        var repository = new EfSectionRepository(db);
        var term = NewTerm();
        var sections = new[] { NewSection() };

        var firstRun = await repository.UpsertAsync(term, sections, CancellationToken.None);
        var secondRun = await repository.UpsertAsync(term, sections, CancellationToken.None);

        Assert.True(firstRun > 0, "the first run should have written the term and section");
        Assert.Equal(0, secondRun);
    }

    [Fact]
    public async Task Upserting_changed_data_updates_the_existing_row_instead_of_duplicating_it()
    {
        using var connection = OpenConnection();
        await using var firstContext = NewContext(connection);
        await new EfSectionRepository(firstContext).UpsertAsync(NewTerm(), [NewSection()], CancellationToken.None);

        await using var secondContext = NewContext(connection);
        var changed = NewSection() with { Enrolled = 99 };
        var rowsWritten = await new EfSectionRepository(secondContext).UpsertAsync(NewTerm(),
                                                                                   [changed],
                                                                                   CancellationToken.None);

        Assert.True(rowsWritten > 0);
        await using var verifyContext = NewContext(connection);
        var sections = await verifyContext.Set<SectionRow>().ToListAsync();
        var section = Assert.Single(sections);
        Assert.Equal(99, section.Enrolled);
    }

    [Fact]
    public async Task Upserting_persists_meetings_instructors_and_the_source_url_and_retrieved_at()
    {
        using var connection = OpenConnection();
        await using var db = NewContext(connection);
        await new EfSectionRepository(db).UpsertAsync(NewTerm(), [NewSection()], CancellationToken.None);

        await using var verifyContext = NewContext(connection);
        var section = Assert.Single(await verifyContext.Set<SectionRow>()
                                                        .Include(row => row.Meetings)
                                                        .Include(row => row.Instructors)
                                                        .ToListAsync());
        Assert.Equal("https://example.edu/StudentRegistrationSsb/?txt_term=202710", section.SourceUrl);
        Assert.Equal(new DateTimeOffset(2026, 9, 11, 23, 40, 0, TimeSpan.Zero), section.RetrievedAt);
        var meeting = Assert.Single(section.Meetings);
        Assert.Equal(MeetingDays.Monday | MeetingDays.Wednesday, meeting.Days);
        Assert.Equal(new TimeOnly(9, 30), meeting.StartTime);
        Assert.Equal("Amanda Strasik", Assert.Single(section.Instructors).Name);
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

    private static Term NewTerm() =>
        new()
        {
            Key = new TermKey("eku", "202710"),
            Name = "Fall 2026",
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/"),
            RetrievedAt = new DateTimeOffset(2026, 9, 11, 23, 40, 0, TimeSpan.Zero),
        };

    private static Section NewSection() =>
        new()
        {
            Key = new SectionKey("eku", "202710", "ARH", "393", "11457"),
            SourceSectionId = "11457",
            DisplaySectionNumber = "001",
            Title = "Survey of Global Art History II",
            Credits = 3m,
            Capacity = 30,
            Enrolled = 30,
            Meetings =
            [
                new Meeting
                {
                    Days = MeetingDays.Monday | MeetingDays.Wednesday,
                    DaysRaw = "MW",
                    StartTime = new TimeOnly(9, 30),
                    EndTime = new TimeOnly(10, 50),
                },
            ],
            Instructors = [new Instructor { Name = "Amanda Strasik", Email = "Amanda.Strasik@eku.edu", IsPrimary = true }],
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/?txt_term=202710"),
            RetrievedAt = new DateTimeOffset(2026, 9, 11, 23, 40, 0, TimeSpan.Zero),
        };

    #endregion Methods
}
