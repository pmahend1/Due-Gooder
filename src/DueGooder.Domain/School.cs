namespace DueGooder.Domain;

/// <summary>A university we collect sections for. Its values come from <c>config/schools.yaml</c>.</summary>
public sealed record School
{
    #region State

    /// <summary>Stable slug from config, e.g. <c>uky</c>. Root of every natural key.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required Uri Homepage { get; init; }

    /// <summary>IANA time zone, e.g. <c>America/New_York</c>. Meeting times are wall-clock times in this zone.</summary>
    public required string TimeZoneId { get; init; }

    #endregion State
}
