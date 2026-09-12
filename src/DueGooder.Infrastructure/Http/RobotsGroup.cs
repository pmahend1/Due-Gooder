namespace DueGooder.Infrastructure.Http;

/// <summary>One robots.txt group: its user-agent lines and the rules under them.</summary>
internal sealed class RobotsGroup
{
    #region State

    /// <summary>Lower-cased product tokens, or <c>*</c>.</summary>
    public List<string> UserAgents { get; } = [];

    public List<RobotsRule> Rules { get; } = [];

    public TimeSpan? CrawlDelay { get; set; }

    #endregion State
}
