namespace DueGooder.Domain;

/// <summary>
/// A normalized section. A <c>null</c> property means the school doesn't publish it;
/// a property we failed to extract also has an entry in <see cref="Failures"/>.
/// </summary>
public sealed record Section
{
    #region State

    public required SectionKey Key { get; init; }

    /// <summary>The platform's own identifier, e.g. Banner's CRN.</summary>
    public string? SourceSectionId { get; init; }

    /// <summary>
    /// The school's published section number (e.g. Banner's <c>sequenceNumber</c>), for display only.
    /// Some platforms reuse this across sections of the same course, so <see cref="SectionKey"/> keys
    /// on <see cref="SourceSectionId"/> instead.
    /// </summary>
    public string? DisplaySectionNumber { get; init; }

    public string? Title { get; init; }

    public decimal? Credits { get; init; }

    /// <summary>Delivery mode as published, e.g. <c>Online</c> or <c>Hybrid</c>.</summary>
    public string? InstructionalMethod { get; init; }

    public string? Campus { get; init; }

    public int? Capacity { get; init; }

    public int? Enrolled { get; init; }

    public int? WaitlistCapacity { get; init; }

    public int? WaitlistCount { get; init; }

    public IReadOnlyList<Meeting> Meetings { get; init; } = [];

    public IReadOnlyList<Instructor> Instructors { get; init; } = [];

    public IReadOnlyList<ExtractionFailure> Failures { get; init; } = [];

    public required Uri SourceUrl { get; init; }

    public required DateTimeOffset RetrievedAt { get; init => field = value.ToUniversalTime(); }

    #endregion State
}
