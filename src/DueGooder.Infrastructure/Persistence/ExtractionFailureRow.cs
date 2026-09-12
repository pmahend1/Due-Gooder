namespace DueGooder.Infrastructure.Persistence;

/// <summary>EF Core row for one <see cref="DueGooder.Domain.ExtractionFailure"/> of a <see cref="SectionRow"/>.</summary>
internal sealed class ExtractionFailureRow
{
    #region State

    public int Id { get; set; }

    public int SectionRowId { get; set; }

    public required string Field { get; set; }

    public required string Reason { get; set; }

    public string? RawValue { get; set; }

    #endregion State
}
