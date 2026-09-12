using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>
/// Persists a term and its sections, upserting by natural key so a refresh with unchanged
/// source data writes nothing.
/// </summary>
public interface ISectionRepository
{
    #region Methods

    /// <summary>Upserts <paramref name="term"/> and <paramref name="sections"/>.</summary>
    /// <returns>The number of rows actually written (0 when nothing changed).</returns>
    Task<int> UpsertAsync(Term term, IReadOnlyList<Section> sections, CancellationToken cancellationToken);

    #endregion Methods
}
