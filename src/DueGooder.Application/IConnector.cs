using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>
/// Collects sections from one registration platform. A single connector serves every school
/// on its platform; per-school differences come from config, never from code.
/// </summary>
public interface IConnector
{
    #region State

    /// <summary>Platform name used in config and reports, e.g. <c>banner9</c>.</summary>
    string Platform { get; }

    #endregion State

    #region Methods

    /// <summary>Scores how likely it is that <paramref name="candidate"/> runs this platform.</summary>
    Task<Fingerprint> FingerprintAsync(Uri candidate, CancellationToken cancellationToken);

    /// <summary>Lists every term the school currently publishes, including new ones.</summary>
    Task<IReadOnlyList<Term>> ListTermsAsync(ConnectorTarget target, CancellationToken cancellationToken);

    /// <summary>Yields the raw section records for one term, with their meetings.</summary>
    IAsyncEnumerable<RawSection> CollectSectionsAsync(ConnectorTarget target,
                                                      Term term,
                                                      CancellationToken cancellationToken);

    /// <summary>
    /// Maps a raw record to a normalized section. Fields that can't be parsed become
    /// <see cref="ExtractionFailure"/> entries on the section rather than exceptions.
    /// </summary>
    Section Map(RawSection raw);

    #endregion Methods
}
