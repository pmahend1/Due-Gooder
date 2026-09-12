using DueGooder.Application;
using DueGooder.Domain;
using Microsoft.EntityFrameworkCore;

namespace DueGooder.Infrastructure.Persistence;

/// <summary>
/// <see cref="ISectionRepository"/> over <see cref="DueGooderDbContext"/>. Existing rows are loaded and
/// their properties reassigned rather than replaced, so EF Core's change tracker only writes what
/// actually differs: re-saving identical data produces zero SQL statements.
/// </summary>
/// <remarks>
/// Safe to share across schools collected concurrently: each call gets its own context, and calls take
/// turns because SQLite allows only one writer at a time anyway.
/// </remarks>
public sealed class EfSectionRepository(Func<DueGooderDbContext> createContext) : ISectionRepository
{
    #region State

    private readonly SemaphoreSlim _writeTurn = new(1, 1);

    #endregion State

    #region Methods

    public async Task<int> UpsertAsync(Term term, IReadOnlyList<Section> sections, CancellationToken cancellationToken)
    {
        await _writeTurn.WaitAsync(cancellationToken);
        try
        {
            await using var db = createContext();
            await UpsertTermAsync(db, term, cancellationToken);

            // One query for the whole term instead of one per section; a large term has thousands.
            var existing = await db.Sections
                                   .Include(row => row.Meetings)
                                   .Include(row => row.Instructors)
                                   .Include(row => row.Failures)
                                   .AsSplitQuery()
                                   .Where(row => row.SchoolId == term.Key.SchoolId && row.TermCode == term.Key.TermCode)
                                   .ToDictionaryAsync(row => new SectionKey(row.SchoolId,
                                                                            row.TermCode,
                                                                            row.Subject,
                                                                            row.CourseNumber,
                                                                            row.SectionId),
                                                      cancellationToken);
            foreach (var section in sections)
            {
                UpsertSection(db, existing, section);
            }

            return await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _writeTurn.Release();
        }
    }

    private static async Task UpsertTermAsync(DueGooderDbContext db, Term term, CancellationToken cancellationToken)
    {
        var row = await db.Terms.FirstOrDefaultAsync(candidate => candidate.SchoolId == term.Key.SchoolId
                                                                 && candidate.TermCode == term.Key.TermCode,
                                                      cancellationToken);
        if (row is null)
        {
            db.Terms.Add(new TermRow
            {
                SchoolId = term.Key.SchoolId,
                TermCode = term.Key.TermCode,
                Name = term.Name,
                SourceUrl = term.SourceUrl.ToString(),
                RetrievedAt = term.RetrievedAt,
            });
            return;
        }

        row.Name = term.Name;
        row.SourceUrl = term.SourceUrl.ToString();
        row.RetrievedAt = term.RetrievedAt;
    }

    private static void UpsertSection(DueGooderDbContext db, Dictionary<SectionKey, SectionRow> existing, Section section)
    {
        if (existing.TryGetValue(section.Key, out var row) is false)
        {
            db.Sections.Add(NewRow(section));
            return;
        }

        ApplyScalars(row, section);

        if (MeetingsMatch(row.Meetings, section.Meetings) is false)
        {
            row.Meetings.Clear();
            row.Meetings.AddRange(section.Meetings.Select(ToRow));
        }

        if (InstructorsMatch(row.Instructors, section.Instructors) is false)
        {
            row.Instructors.Clear();
            row.Instructors.AddRange(section.Instructors.Select(ToRow));
        }

        if (FailuresMatch(row.Failures, section.Failures) is false)
        {
            row.Failures.Clear();
            row.Failures.AddRange(section.Failures.Select(ToRow));
        }
    }

    private static SectionRow NewRow(Section section)
    {
        var row = new SectionRow
        {
            SchoolId = section.Key.SchoolId,
            TermCode = section.Key.TermCode,
            Subject = section.Key.Subject,
            CourseNumber = section.Key.CourseNumber,
            SectionId = section.Key.SectionId,
            SourceUrl = section.SourceUrl.ToString(),
        };
        ApplyScalars(row, section);
        row.Meetings.AddRange(section.Meetings.Select(ToRow));
        row.Instructors.AddRange(section.Instructors.Select(ToRow));
        row.Failures.AddRange(section.Failures.Select(ToRow));
        return row;
    }

    private static void ApplyScalars(SectionRow row, Section section)
    {
        row.SourceSectionId = section.SourceSectionId;
        row.DisplaySectionNumber = section.DisplaySectionNumber;
        row.Title = section.Title;
        row.Credits = section.Credits;
        row.CreditsMin = section.CreditsMin;
        row.CreditsMax = section.CreditsMax;
        row.CreditsRaw = section.CreditsRaw;
        row.InstructionalMethod = section.InstructionalMethod;
        row.Campus = section.Campus;
        row.Capacity = section.Capacity;
        row.Enrolled = section.Enrolled;
        row.WaitlistCapacity = section.WaitlistCapacity;
        row.WaitlistCount = section.WaitlistCount;
        row.CrossListGroup = section.CrossListGroup;
        row.CrossListCapacity = section.CrossListCapacity;
        row.CrossListEnrolled = section.CrossListEnrolled;
        row.SourceUrl = section.SourceUrl.ToString();
        row.RetrievedAt = section.RetrievedAt;
    }

    private static bool MeetingsMatch(IReadOnlyList<MeetingRow> existing, IReadOnlyList<Meeting> desired) =>
        existing.Count == desired.Count
        && existing.Zip(desired, MeetingMatches).All(match => match);

    private static bool MeetingMatches(MeetingRow row, Meeting meeting) =>
        row.Days == meeting.Days
        && row.DaysRaw == meeting.DaysRaw
        && row.StartTime == meeting.StartTime
        && row.StartTimeRaw == meeting.StartTimeRaw
        && row.EndTime == meeting.EndTime
        && row.EndTimeRaw == meeting.EndTimeRaw
        && row.Building == meeting.Building
        && row.Room == meeting.Room
        && row.LocationRaw == meeting.LocationRaw
        && row.StartDate == meeting.StartDate
        && row.EndDate == meeting.EndDate
        && row.MeetingType == meeting.MeetingType;

    private static bool InstructorsMatch(IReadOnlyList<InstructorRow> existing, IReadOnlyList<Instructor> desired) =>
        existing.Count == desired.Count
        && existing.Zip(desired, InstructorMatches).All(match => match);

    private static bool InstructorMatches(InstructorRow row, Instructor instructor) =>
        row.Name == instructor.Name
        && row.Email == instructor.Email
        && row.IsPrimary == instructor.IsPrimary;

    private static bool FailuresMatch(IReadOnlyList<ExtractionFailureRow> existing, IReadOnlyList<ExtractionFailure> desired) =>
        existing.Count == desired.Count
        && existing.Zip(desired, FailureMatches).All(match => match);

    private static bool FailureMatches(ExtractionFailureRow row, ExtractionFailure failure) =>
        row.Field == failure.Field
        && row.Reason == failure.Reason
        && row.RawValue == failure.RawValue;

    private static MeetingRow ToRow(Meeting meeting) =>
        new()
        {
            Days = meeting.Days,
            DaysRaw = meeting.DaysRaw,
            StartTime = meeting.StartTime,
            StartTimeRaw = meeting.StartTimeRaw,
            EndTime = meeting.EndTime,
            EndTimeRaw = meeting.EndTimeRaw,
            Building = meeting.Building,
            Room = meeting.Room,
            LocationRaw = meeting.LocationRaw,
            StartDate = meeting.StartDate,
            EndDate = meeting.EndDate,
            MeetingType = meeting.MeetingType,
        };

    private static InstructorRow ToRow(Instructor instructor) =>
        new()
        {
            Name = instructor.Name,
            Email = instructor.Email,
            IsPrimary = instructor.IsPrimary,
        };

    private static ExtractionFailureRow ToRow(ExtractionFailure failure) =>
        new()
        {
            Field = failure.Field,
            Reason = failure.Reason,
            RawValue = failure.RawValue,
        };

    #endregion Methods
}
