namespace DueGooder.Domain;

/// <summary>A catalog course. Sections point at it through <see cref="SectionKey.Course"/>.</summary>
public sealed record Course
{
    #region State

    public required CourseKey Key { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public required Uri SourceUrl { get; init; }

    public required DateTimeOffset RetrievedAt { get; init => field = value.ToUniversalTime(); }

    #endregion State
}
