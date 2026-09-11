namespace DueGooder.Domain;

/// <summary>An academic term offered by a school, as listed by its registration platform.</summary>
public sealed record Term
{
    #region State

    public required TermKey Key { get; init; }

    /// <summary>Display name as published, e.g. <c>Fall 2026</c>.</summary>
    public string? Name { get; init; }

    public required Uri SourceUrl { get; init; }

    public required DateTimeOffset RetrievedAt { get; init => field = value.ToUniversalTime(); }

    #endregion State
}
