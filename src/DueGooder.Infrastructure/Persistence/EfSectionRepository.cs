using DueGooder.Application;
using DueGooder.Domain;
using Microsoft.EntityFrameworkCore;

namespace DueGooder.Infrastructure.Persistence;

/// <summary>
/// <see cref="ISectionRepository"/> over <see cref="DueGooderDbContext"/>. Existing rows are loaded and
/// their properties reassigned rather than replaced, so EF Core's change tracker only writes what
/// actually differs. A section whose content is unchanged keeps its source URL and retrieved_at, and only its
/// last_confirmed_at moves, in bulk: a refresh with no source changes writes zero content rows.
/// </summary>
/// <remarks>
/// Safe to share across schools collected concurrently: each call gets its own context, and calls take
/// turns because SQLite allows only one writer at a time anyway.
/// </remarks>
public sealed class EfSectionRepository(Func<DueGooderDbContext> createContext) : ISectionRepository
{
    #region State

    // Keeps each confirmation statement's id list well under SQLite's parameter limit.
    private const int ConfirmBatchSize = 500;

    private readonly SemaphoreSlim _writeTurn = new(1, 1);

    #endregion State

    #region Methods

    public async Task<IReadOnlyList<StoredTerm>> GetStoredTermsAsync(string schoolId, CancellationToken cancellationToken)
    {
        await _writeTurn.WaitAsync(cancellationToken);
        try
        {
            await using var db = createContext();
            return await db.Terms
                           .AsNoTracking()
                           .Where(row => row.SchoolId == schoolId)
                           .Select(row => new StoredTerm(row.TermCode, row.Name, row.SectionCount, row.LastConfirmedAt))
                           .ToListAsync(cancellationToken);
        }
        finally
        {
            _writeTurn.Release();
        }
    }

    public async Task<SectionChanges> UpsertAsync(Term term,
                                                  IReadOnlyList<Section> sections,
                                                  IReadOnlyList<CollectionGap> gaps,
                                                  DateTimeOffset confirmedAt,
                                                  CancellationToken cancellationToken)
    {
        await _writeTurn.WaitAsync(cancellationToken);
        try
        {
            await using var db = createContext();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            await UpsertTermAsync(db, term, sections.Count, gaps, confirmedAt, cancellationToken);

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
            var added = 0;
            var changed = 0;
            var unchangedIds = new List<int>();
            foreach (var section in sections)
            {
                if (existing.TryGetValue(section.Key, out var row) is false)
                {
                    db.Sections.Add(NewRow(section, confirmedAt));
                    added++;
                }
                else if (UpdateContent(db, row, section))
                {
                    row.SourceUrl = section.SourceUrl.ToString();
                    row.RetrievedAt = section.RetrievedAt;
                    row.LastConfirmedAt = confirmedAt;
                    changed++;
                }
                else
                {
                    unchangedIds.Add(row.Id);
                }
            }

            var rowsWritten = await db.SaveChangesAsync(cancellationToken);

            // Confirmation isn't a content change: one bulk statement per batch instead of a tracked write per section.
            foreach (var batch in unchangedIds.Chunk(ConfirmBatchSize))
            {
                await db.Sections
                        .Where(row => batch.Contains(row.Id))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastConfirmedAt, confirmedAt), cancellationToken);
            }

            await db.Terms
                    .Where(row => row.SchoolId == term.Key.SchoolId && row.TermCode == term.Key.TermCode)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.LastConfirmedAt, confirmedAt), cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var seen = sections.Select(section => section.Key).ToHashSet();
            var notSeen = existing.Keys.Count(key => seen.Contains(key) is false);
            return new SectionChanges(rowsWritten, added, changed, unchangedIds.Count, notSeen);
        }
        finally
        {
            _writeTurn.Release();
        }
    }

    private static async Task UpsertTermAsync(DueGooderDbContext db,
                                              Term term,
                                              int sectionCount,
                                              IReadOnlyList<CollectionGap> gaps,
                                              DateTimeOffset confirmedAt,
                                              CancellationToken cancellationToken)
    {
        var gapCount = gaps.Sum(gap => gap.Count);

        // Leaves the failing request URL out: it carries a random search id, which would make an unchanged gap look changed.
        var gapDetail = gaps.Count is 0
            ? null
            : string.Join("; ", gaps.Select(gap => $"records {gap.FirstPosition}-{gap.FirstPosition + gap.Count - 1} of "
                                                   + $"{gap.TotalCount}: {gap.Reason}"));
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
                LastConfirmedAt = confirmedAt,
                SectionCount = sectionCount,
                GapCount = gapCount,
                GapDetail = gapDetail,
            });
            return;
        }

        row.Name = term.Name;
        row.SectionCount = sectionCount;
        row.GapCount = gapCount;
        row.GapDetail = gapDetail;
        var entry = db.Entry(row);
        entry.DetectChanges();
        if (entry.State is EntityState.Modified)
        {
            row.SourceUrl = term.SourceUrl.ToString();
            row.RetrievedAt = term.RetrievedAt;
        }
    }

    /// <returns>Whether any stored value of the section differed; source URL and timestamps aren't compared.</returns>
    private static bool UpdateContent(DueGooderDbContext db, SectionRow row, Section section)
    {
        ApplyContent(row, section);
        var childrenChanged = false;
        if (MeetingsMatch(row.Meetings, section.Meetings) is false)
        {
            row.Meetings.Clear();
            row.Meetings.AddRange(section.Meetings.Select(ToRow));
            childrenChanged = true;
        }

        if (InstructorsMatch(row.Instructors, section.Instructors) is false)
        {
            row.Instructors.Clear();
            row.Instructors.AddRange(section.Instructors.Select(ToRow));
            childrenChanged = true;
        }

        if (FailuresMatch(row.Failures, section.Failures) is false)
        {
            row.Failures.Clear();
            row.Failures.AddRange(section.Failures.Select(ToRow));
            childrenChanged = true;
        }

        var entry = db.Entry(row);
        entry.DetectChanges();
        return childrenChanged || entry.State is EntityState.Modified;
    }

    private static SectionRow NewRow(Section section, DateTimeOffset confirmedAt)
    {
        var row = new SectionRow
        {
            SchoolId = section.Key.SchoolId,
            TermCode = section.Key.TermCode,
            Subject = section.Key.Subject,
            CourseNumber = section.Key.CourseNumber,
            SectionId = section.Key.SectionId,
            SourceUrl = section.SourceUrl.ToString(),
            RetrievedAt = section.RetrievedAt,
            LastConfirmedAt = confirmedAt,
        };
        ApplyContent(row, section);
        row.Meetings.AddRange(section.Meetings.Select(ToRow));
        row.Instructors.AddRange(section.Instructors.Select(ToRow));
        row.Failures.AddRange(section.Failures.Select(ToRow));
        return row;
    }

    private static void ApplyContent(SectionRow row, Section section)
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
