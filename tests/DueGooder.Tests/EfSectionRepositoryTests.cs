using DueGooder.Application;
using DueGooder.Domain;
using DueGooder.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DueGooder.Tests;

public sealed class EfSectionRepositoryTests
{
    #region State

    private static readonly DateTimeOffset At = new(2026, 9, 11, 23, 40, 0, TimeSpan.Zero);

    #endregion State

    #region Methods

    [Fact]
    public async Task Upserting_the_same_data_twice_writes_nothing_on_the_second_run()
    {
        using var connection = OpenConnection();
        var repository = new EfSectionRepository(() => NewContext(connection));
        var term = NewTerm();
        var sections = new[] { NewSection() };

        var firstRun = await repository.UpsertAsync(term, sections, [], At, CancellationToken.None);
        var secondRun = await repository.UpsertAsync(term, sections, [], At, CancellationToken.None);

        Assert.True(firstRun.RowsWritten > 0, "the first run should have written the term and section");
        Assert.Equal(1, firstRun.Added);
        Assert.Equal(0, secondRun.RowsWritten);
        Assert.Equal(1, secondRun.Unchanged);
    }

    [Fact]
    public async Task Upserting_changed_data_updates_the_existing_row_instead_of_duplicating_it()
    {
        using var connection = OpenConnection();
        await new EfSectionRepository(() => NewContext(connection)).UpsertAsync(NewTerm(),
                                                                                [NewSection()],
                                                                                [],
                                                                                At,
                                                                                CancellationToken.None);

        var changed = NewSection() with { Enrolled = 99 };
        var changes = await new EfSectionRepository(() => NewContext(connection)).UpsertAsync(NewTerm(),
                                                                                              [changed],
                                                                                              [],
                                                                                              At,
                                                                                              CancellationToken.None);

        Assert.True(changes.RowsWritten > 0);
        Assert.Equal(1, changes.Changed);
        await using var verifyContext = NewContext(connection);
        var sections = await verifyContext.Set<SectionRow>().ToListAsync();
        var section = Assert.Single(sections);
        Assert.Equal(99, section.Enrolled);
    }

    [Fact]
    public async Task Upserting_persists_meetings_instructors_and_the_source_url_and_retrieved_at()
    {
        using var connection = OpenConnection();
        await new EfSectionRepository(() => NewContext(connection)).UpsertAsync(NewTerm(),
                                                                                [NewSection()],
                                                                                [],
                                                                                At,
                                                                                CancellationToken.None);

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

    [Fact]
    public async Task A_refresh_that_only_moves_retrieved_at_and_the_source_url_writes_no_content_and_confirms_the_section()
    {
        using var connection = OpenConnection();
        var repository = new EfSectionRepository(() => NewContext(connection));
        await repository.UpsertAsync(NewTerm(), [NewSection()], [], At, CancellationToken.None);
        var later = At.AddDays(1);
        var refetched = NewSection() with
        {
            SourceUrl = new Uri("https://example.edu/StudentRegistrationSsb/?txt_term=202710&uniqueSessionId=other"),
            RetrievedAt = later,
        };

        var changes = await repository.UpsertAsync(NewTerm() with { RetrievedAt = later }, [refetched], [], later, CancellationToken.None);

        Assert.Equal(new SectionChanges(RowsWritten: 0, Added: 0, Changed: 0, Unchanged: 1, NotSeen: 0), changes);
        await using var verifyContext = NewContext(connection);
        var section = Assert.Single(await verifyContext.Set<SectionRow>().ToListAsync());
        Assert.Equal(At, section.RetrievedAt);
        Assert.Equal("https://example.edu/StudentRegistrationSsb/?txt_term=202710", section.SourceUrl);
        Assert.Equal(later, section.LastConfirmedAt);
        Assert.Equal(later, Assert.Single(await verifyContext.Set<TermRow>().ToListAsync()).LastConfirmedAt);
    }

    [Fact]
    public async Task A_section_the_source_stops_listing_is_kept_and_no_longer_confirmed()
    {
        using var connection = OpenConnection();
        var repository = new EfSectionRepository(() => NewContext(connection));
        var cancelled = NewSection() with { Key = new SectionKey("eku", "202710", "ARH", "393", "11458") };
        await repository.UpsertAsync(NewTerm(), [NewSection(), cancelled], [], At, CancellationToken.None);
        var later = At.AddDays(1);

        var changes = await repository.UpsertAsync(NewTerm(), [NewSection()], [], later, CancellationToken.None);

        Assert.Equal(1, changes.NotSeen);
        await using var verifyContext = NewContext(connection);
        var rows = await verifyContext.Set<SectionRow>().ToDictionaryAsync(row => row.SectionId);
        Assert.Equal(2, rows.Count);
        Assert.Equal(later, rows["11457"].LastConfirmedAt);
        Assert.Equal(At, rows["11458"].LastConfirmedAt);
    }

    [Fact]
    public async Task Stored_terms_carry_the_last_section_count_and_a_recorded_gap()
    {
        using var connection = OpenConnection();
        var repository = new EfSectionRepository(() => NewContext(connection));
        var gap = new CollectionGap(350, 1, 684, "Banner answered success:false", new Uri("https://example.edu/?pageOffset=349"));

        await repository.UpsertAsync(NewTerm(), [NewSection()], [gap], At, CancellationToken.None);

        var stored = Assert.Single(await repository.GetStoredTermsAsync("eku", CancellationToken.None));
        Assert.Equal(new StoredTerm("202710", "Fall 2026", 1, At), stored);
        await using var verifyContext = NewContext(connection);
        var term = Assert.Single(await verifyContext.Set<TermRow>().ToListAsync());
        Assert.Equal(1, term.GapCount);
        Assert.Equal("records 350-350 of 684: Banner answered success:false", term.GapDetail);
        Assert.Empty(await repository.GetStoredTermsAsync("other", CancellationToken.None));
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
