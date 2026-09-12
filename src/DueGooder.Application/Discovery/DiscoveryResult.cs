namespace DueGooder.Application.Discovery;

/// <summary>What identifying one school found, and every step of evidence behind it.</summary>
public sealed record DiscoveryResult
{
    #region State

    public required DiscoveryMethod Method { get; init; }

    /// <summary>The connector platform that confirmed the school; null when none did.</summary>
    public string? Platform { get; init; }

    public Uri? BaseUrl { get; init; }

    /// <summary>The confirming fingerprint's confidence, or the best one seen when nothing was confirmed.</summary>
    public double Confidence { get; init; }

    /// <summary>Requests and findings in the order they happened.</summary>
    public IReadOnlyList<string> Evidence { get; init; } = [];

    /// <summary>Platforms without a connector whose URL patterns showed up, e.g. <c>peoplesoft: https://…/psc/…</c>.</summary>
    public IReadOnlyList<string> PlatformHints { get; init; } = [];

    /// <summary>Why the school needs a person; null when it was identified.</summary>
    public string? ReviewReason { get; init; }

    public int PagesFetched { get; init; }

    public int Probes { get; init; }

    public TimeSpan Duration { get; init; }

    public bool Identified => Platform is not null && BaseUrl is not null;

    #endregion State

    #region Methods

    public static DiscoveryResult FromConfig(string platform, Uri baseUrl) =>
        new()
        {
            Method = DiscoveryMethod.Configured,
            Platform = platform,
            BaseUrl = baseUrl,
            Confidence = 1,
            Evidence = [$"platform and base_url set in config: {platform} at {baseUrl}"],
        };

    #endregion Methods
}
