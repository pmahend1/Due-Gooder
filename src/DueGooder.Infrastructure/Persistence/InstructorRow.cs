namespace DueGooder.Infrastructure.Persistence;

/// <summary>EF Core row for one <see cref="DueGooder.Domain.Instructor"/> of a <see cref="SectionRow"/>.</summary>
internal sealed class InstructorRow
{
    #region State

    public int Id { get; set; }

    public int SectionRowId { get; set; }

    public required string Name { get; set; }

    public string? Email { get; set; }

    public bool IsPrimary { get; set; }

    #endregion State
}
