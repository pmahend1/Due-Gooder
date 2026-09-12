namespace DueGooder.Application;

/// <summary>
/// Records of a term the platform listed but wouldn't return. The rest of the term is still collected and stored, with
/// this gap recorded next to it, so the missing sections are counted and explained rather than silently absent.
/// </summary>
/// <param name="FirstPosition">1-based position of the first missing record in the platform's result order.</param>
/// <param name="Count">How many consecutive records are missing.</param>
/// <param name="TotalCount">How many records the platform said the term has.</param>
/// <param name="Reason">What the platform answered for them.</param>
/// <param name="SourceUrl">The narrowest request that still failed.</param>
public sealed record CollectionGap(int FirstPosition, int Count, int TotalCount, string Reason, Uri SourceUrl);
