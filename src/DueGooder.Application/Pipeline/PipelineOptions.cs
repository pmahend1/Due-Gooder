namespace DueGooder.Application.Pipeline;

/// <summary>How a run walks the school list.</summary>
public sealed record PipelineOptions
{
    #region State

    /// <summary>Hosts collected at the same time. Each host still sees one school and one request at a time.</summary>
    public int MaxConcurrentHosts { get; init; } = 8;

    /// <summary>Collect only the first N terms in the order the connector lists them; null collects every term.</summary>
    public int? MaxTermsPerSchool { get; init; }

    /// <summary>When set, collect only these term codes.</summary>
    public IReadOnlySet<string>? TermCodes { get; init; }

    /// <summary>
    /// A school stops after this many terms fail in a row. A sign-in wall or a dead endpoint fails every
    /// term the same way, and trying the rest would only add load on the registrar.
    /// </summary>
    public int MaxConsecutiveTermFailures { get; init; } = 5;

    #endregion State
}
