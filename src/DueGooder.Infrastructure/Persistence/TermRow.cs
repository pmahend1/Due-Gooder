namespace DueGooder.Infrastructure.Persistence;

/// <summary>EF Core row for <see cref="DueGooder.Domain.Term"/>. <see cref="SchoolId"/> + <see cref="TermCode"/> is the natural key.</summary>
internal sealed class TermRow
{
    #region State

    public int Id { get; set; }

    public required string SchoolId { get; set; }

    public required string TermCode { get; set; }

    public string? Name { get; set; }

    public required string SourceUrl { get; set; }

    /// <summary>When the stored values were retrieved; moves only when they change.</summary>
    public DateTimeOffset RetrievedAt { get; set; }

    /// <summary>When a run last collected this term; data not re-confirmed within the refresh window is stale.</summary>
    public DateTimeOffset LastConfirmedAt { get; set; }

    /// <summary>Sections the last successful collection saw; the next run compares against it to spot a broken integration.</summary>
    public int SectionCount { get; set; }

    /// <summary>Records the platform listed but wouldn't return in the last collection; 0 when the term is complete.</summary>
    public int GapCount { get; set; }

    /// <summary>Which records were missing and why; null when the term is complete.</summary>
    public string? GapDetail { get; set; }

    #endregion State
}
