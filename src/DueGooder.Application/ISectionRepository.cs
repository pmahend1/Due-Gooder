using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>
/// Persists terms and their sections, upserting by natural key so a refresh with unchanged source data writes no
/// content. Nothing is ever deleted: a section the source stops listing keeps its row and stops being confirmed.
/// </summary>
public interface ISectionRepository
{
    #region Methods

    /// <summary>Every term stored for <paramref name="schoolId"/>, as the previous runs left it.</summary>
    Task<IReadOnlyList<StoredTerm>> GetStoredTermsAsync(string schoolId, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts <paramref name="term"/> and <paramref name="sections"/>, and marks every section seen here as confirmed at
    /// <paramref name="confirmedAt"/>. <paramref name="gaps"/> is stored with the term.
    /// </summary>
    Task<SectionChanges> UpsertAsync(Term term,
                                     IReadOnlyList<Section> sections,
                                     IReadOnlyList<CollectionGap> gaps,
                                     DateTimeOffset confirmedAt,
                                     CancellationToken cancellationToken);

    #endregion Methods
}
