namespace DueGooder.Domain;

/// <summary>An instructor assigned to a section.</summary>
public sealed record Instructor
{
    #region State

    /// <summary>Display name exactly as published.</summary>
    public required string Name { get; init; }

    public string? Email { get; init; }

    public bool IsPrimary { get; init; }

    #endregion State
}
