using DueGooder.Application.Discovery;

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

    /// <summary>How schools without a configured <c>base_url</c> are identified from their homepage.</summary>
    public DiscoveryOptions Discovery { get; init; } = new();

    /// <summary>
    /// A term whose section count falls by more than this share since the last run flags the integration as broken:
    /// nothing is written for it and the stored sections are kept. Registrars cancel sections, but not half a term.
    /// </summary>
    public double MaxSectionDrop { get; init; } = 0.5;

    /// <summary>Terms that had fewer sections than this last run are too small for the drop check to mean anything.</summary>
    public int MinSectionsForDropCheck { get; init; } = 20;

    /// <summary>
    /// A term is stored with a recorded gap only while the records the platform wouldn't return are at most this share of
    /// it; a bigger gap fails the term, since the stored copy would misrepresent it.
    /// </summary>
    public double MaxGapShare { get; init; } = 0.05;

    #endregion State
}
