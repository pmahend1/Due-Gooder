namespace DueGooder.Infrastructure.Persistence;

/// <summary>
/// EF Core row for <see cref="DueGooder.Domain.Section"/>. The school/term/subject/course/section-id
/// tuple is the natural key (see <see cref="DueGooder.Domain.SectionKey"/>); <see cref="Id"/> is only
/// a surrogate key so child rows have a simple foreign key.
/// </summary>
internal sealed class SectionRow
{
    #region State

    public int Id { get; set; }

    public required string SchoolId { get; set; }

    public required string TermCode { get; set; }

    public required string Subject { get; set; }

    public required string CourseNumber { get; set; }

    public required string SectionId { get; set; }

    public string? SourceSectionId { get; set; }

    public string? DisplaySectionNumber { get; set; }

    public string? Title { get; set; }

    public decimal? Credits { get; set; }

    public decimal? CreditsMin { get; set; }

    public decimal? CreditsMax { get; set; }

    public string? CreditsRaw { get; set; }

    public string? InstructionalMethod { get; set; }

    public string? Campus { get; set; }

    public int? Capacity { get; set; }

    public int? Enrolled { get; set; }

    public int? WaitlistCapacity { get; set; }

    public int? WaitlistCount { get; set; }

    public string? CrossListGroup { get; set; }

    public int? CrossListCapacity { get; set; }

    public int? CrossListEnrolled { get; set; }

    public required string SourceUrl { get; set; }

    /// <summary>When the stored values were retrieved; moves only when they change, so a refresh with no changes writes nothing.</summary>
    public DateTimeOffset RetrievedAt { get; set; }

    /// <summary>
    /// When a run last saw this section. A section the source stops listing keeps its row but stops being confirmed, so
    /// it goes stale instead of being deleted.
    /// </summary>
    public DateTimeOffset LastConfirmedAt { get; set; }

    public List<MeetingRow> Meetings { get; set; } = [];

    public List<InstructorRow> Instructors { get; set; } = [];

    public List<ExtractionFailureRow> Failures { get; set; } = [];

    #endregion State
}
