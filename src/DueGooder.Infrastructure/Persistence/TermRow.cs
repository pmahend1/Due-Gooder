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

    public DateTimeOffset RetrievedAt { get; set; }

    #endregion State
}
