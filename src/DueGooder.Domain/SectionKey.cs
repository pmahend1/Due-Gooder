namespace DueGooder.Domain;

/// <summary>
/// Natural key of a section: school + term + course + the platform's own section identifier
/// (e.g. Banner's CRN). Refreshes upsert on this key, so re-collecting a section never duplicates it.
/// This is deliberately not the school's published section number: some platforms reuse that number
/// across sections of the same course, so it can't identify a section on its own.
/// </summary>
public readonly record struct SectionKey(string SchoolId,
                                         string TermCode,
                                         string Subject,
                                         string CourseNumber,
                                         string SectionId)
{
    #region State

    public TermKey Term => new(SchoolId, TermCode);

    public CourseKey Course => new(SchoolId, Subject, CourseNumber);

    #endregion State
}
