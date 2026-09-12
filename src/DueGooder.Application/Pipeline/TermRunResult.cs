namespace DueGooder.Application.Pipeline;

/// <summary>One term of one school in a run: its counts, or why it failed.</summary>
/// <param name="TermCode">The platform's term code.</param>
/// <param name="Name">Display name as published.</param>
/// <param name="Sections">Distinct sections stored.</param>
/// <param name="Meetings">Meetings across those sections.</param>
/// <param name="ExtractionFailures">Fields that were published but couldn't be parsed.</param>
/// <param name="DuplicateSections">
/// Sections returned more than once under the same key, e.g. when results shift between pages. Only the
/// last copy is stored.
/// </param>
/// <param name="RowsWritten">Rows the upsert inserted or changed; 0 means nothing differed from the database.</param>
/// <param name="Duration">Wall-clock time for this term, collection and storage together.</param>
/// <param name="FailureReason">Why the term failed; null when it was collected. Nothing is stored for a failed term.</param>
/// <param name="FailureSourceUrl">The request that got the unexpected answer, when known.</param>
public sealed record TermRunResult(string TermCode,
                                   string? Name,
                                   int Sections,
                                   int Meetings,
                                   int ExtractionFailures,
                                   int DuplicateSections,
                                   int RowsWritten,
                                   TimeSpan Duration,
                                   string? FailureReason,
                                   Uri? FailureSourceUrl);
