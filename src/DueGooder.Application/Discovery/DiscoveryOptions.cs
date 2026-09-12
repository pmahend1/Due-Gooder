namespace DueGooder.Application.Discovery;

/// <summary>How far discovery may look for a school's platform, starting from its homepage.</summary>
public sealed record DiscoveryOptions
{
    #region State

    /// <summary>Pages fetched from the school's own site, homepage and redirects included.</summary>
    public int MaxPages { get; init; } = 10;

    /// <summary>Link hops from the homepage; 2 reaches the typical homepage → registrar → class schedule path.</summary>
    public int MaxDepth { get; init; } = 2;

    public int MaxRedirects { get; init; } = 5;

    /// <summary>A connector's fingerprint must reach this to identify the school; below it the school goes to review.</summary>
    public double MinConfidence { get; init; } = 0.9;

    #endregion State
}
