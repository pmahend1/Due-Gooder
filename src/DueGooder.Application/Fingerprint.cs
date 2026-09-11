namespace DueGooder.Application;

/// <summary>How confident a connector is that a site runs its platform, and why.</summary>
/// <param name="Confidence">From 0 (no match) to 1 (certain).</param>
/// <param name="BaseUrl">The platform's base URL on this site, when the connector found it.</param>
/// <param name="Evidence">What matched: URL patterns, HTML markers, headers, probe responses.</param>
public sealed record Fingerprint(double Confidence, Uri? BaseUrl, IReadOnlyList<string> Evidence);
