using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>A school paired with the base URL of its registration platform.</summary>
public sealed record ConnectorTarget(School School, Uri BaseUrl)
{
    #region State

    /// <summary>
    /// Platform-specific settings from <c>config/schools.yaml</c>, keyed by their YAML names,
    /// e.g. Banner's <c>mep_code</c>. Each connector documents the keys it reads.
    /// </summary>
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>();

    #endregion State
}
