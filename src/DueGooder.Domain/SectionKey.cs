namespace DueGooder.Domain;

/// <summary>
/// Natural key of a section: school + term + course + section number.
/// Refreshes upsert on this key, so re-collecting a section never duplicates it.
/// </summary>
public readonly record struct SectionKey(string SchoolId,
                                         string TermCode,
                                         string Subject,
                                         string CourseNumber,
                                         string SectionNumber)
{
    #region State

    public TermKey Term => new(SchoolId, TermCode);

    public CourseKey Course => new(SchoolId, Subject, CourseNumber);

    #endregion State
}
