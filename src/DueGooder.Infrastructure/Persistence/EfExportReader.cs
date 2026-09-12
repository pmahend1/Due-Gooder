using DueGooder.Application;
using DueGooder.Domain;
using Microsoft.EntityFrameworkCore;

namespace DueGooder.Infrastructure.Persistence;

/// <summary><see cref="IExportReader"/> over <see cref="DueGooderDbContext"/>, mapping stored rows back to domain records.</summary>
public sealed class EfExportReader(Func<DueGooderDbContext> createContext) : IExportReader
{
    #region Methods

    public async Task<IReadOnlyList<ExportedSection>> GetSectionsAsync(IReadOnlySet<string>? schoolIds, CancellationToken cancellationToken)
    {
        await using var db = createContext();
        var terms = await db.Terms
                            .AsNoTracking()
                            .Where(row => schoolIds == null || schoolIds.Contains(row.SchoolId))
                            .ToDictionaryAsync(row => new TermKey(row.SchoolId, row.TermCode), row => row.Name, cancellationToken);
        var sections = await db.Sections
                               .AsNoTracking()
                               .Include(row => row.Meetings)
                               .Include(row => row.Instructors)
                               .Include(row => row.Failures)
                               .AsSplitQuery()
                               .Where(row => schoolIds == null || schoolIds.Contains(row.SchoolId))
                               .ToListAsync(cancellationToken);
        return sections.Select(row => ToExportedSection(row, terms)).ToList();
    }

    private static ExportedSection ToExportedSection(SectionRow row, IReadOnlyDictionary<TermKey, string?> terms)
    {
        var key = new SectionKey(row.SchoolId, row.TermCode, row.Subject, row.CourseNumber, row.SectionId);
        var section = new Section
        {
            Key = key,
            SourceSectionId = row.SourceSectionId,
            DisplaySectionNumber = row.DisplaySectionNumber,
            Title = row.Title,
            Credits = row.Credits,
            CreditsMin = row.CreditsMin,
            CreditsMax = row.CreditsMax,
            CreditsRaw = row.CreditsRaw,
            InstructionalMethod = row.InstructionalMethod,
            Campus = row.Campus,
            Capacity = row.Capacity,
            Enrolled = row.Enrolled,
            WaitlistCapacity = row.WaitlistCapacity,
            WaitlistCount = row.WaitlistCount,
            CrossListGroup = row.CrossListGroup,
            CrossListCapacity = row.CrossListCapacity,
            CrossListEnrolled = row.CrossListEnrolled,
            Meetings = row.Meetings.Select(ToMeeting).ToList(),
            Instructors = row.Instructors.Select(ToInstructor).ToList(),
            Failures = row.Failures.Select(ToFailure).ToList(),
            SourceUrl = new Uri(row.SourceUrl),
            RetrievedAt = row.RetrievedAt,
        };
        var termName = terms.GetValueOrDefault(key.Term);
        return new ExportedSection(section, termName, row.LastConfirmedAt);
    }

    private static Meeting ToMeeting(MeetingRow row) =>
        new()
        {
            Days = row.Days,
            DaysRaw = row.DaysRaw,
            StartTime = row.StartTime,
            StartTimeRaw = row.StartTimeRaw,
            EndTime = row.EndTime,
            EndTimeRaw = row.EndTimeRaw,
            Building = row.Building,
            Room = row.Room,
            LocationRaw = row.LocationRaw,
            StartDate = row.StartDate,
            EndDate = row.EndDate,
            MeetingType = row.MeetingType,
        };

    private static Instructor ToInstructor(InstructorRow row) =>
        new()
        {
            Name = row.Name,
            Email = row.Email,
            IsPrimary = row.IsPrimary,
        };

    private static ExtractionFailure ToFailure(ExtractionFailureRow row) => new(row.Field, row.Reason, row.RawValue);

    #endregion Methods
}
