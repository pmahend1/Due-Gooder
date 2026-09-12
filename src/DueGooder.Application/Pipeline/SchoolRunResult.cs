using DueGooder.Application.Discovery;

namespace DueGooder.Application.Pipeline;

/// <summary>One school in a run: how it was identified, what was collected, what it cost, and what failed.</summary>
public sealed record SchoolRunResult
{
    #region State

    public required string SchoolId { get; init; }

    /// <summary>The platform collected from, or <c>unidentified</c>.</summary>
    public required string Platform { get; init; }

    /// <summary>
    /// Host the school's requests went to (its homepage's when unidentified). Schools sharing a host were collected one
    /// after another.
    /// </summary>
    public required string Host { get; init; }

    public required SchoolRunStatus Status { get; init; }

    /// <summary>Why the school failed, stopped early or wasn't identified; null when nothing went wrong at school level.</summary>
    public string? FailureReason { get; init; }

    /// <summary>The request behind <see cref="FailureReason"/>, when known.</summary>
    public Uri? FailureSourceUrl { get; init; }

    /// <summary>How the platform entry point was found, with the evidence; null for a run that predates discovery.</summary>
    public DiscoveryResult? Discovery { get; init; }

    /// <summary>
    /// Signs that a working integration broke, compared with what earlier runs stored: a big section-count drop, a term
    /// that vanished from the listing, a school that stopped collecting. Stored data is never deleted for them.
    /// </summary>
    public IReadOnlyList<string> IntegrationFlags { get; init; } = [];

    /// <summary>Terms the platform published, before any term filter.</summary>
    public required int TermsListed { get; init; }

    /// <summary>Every term attempted, in order, with its counts or its failure.</summary>
    public required IReadOnlyList<TermRunResult> Terms { get; init; }

    public required RequestStats Requests { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Discovery plus collection; excludes time spent waiting for the host's turn.</summary>
    public required TimeSpan Duration { get; init; }

    public int TermsCollected => Terms.Count(term => term.FailureReason is null);

    /// <summary>Collected terms the database had no copy of before this run.</summary>
    public int NewTerms => Terms.Count(term => term.IsNew && term.FailureReason is null);

    public int Sections => Terms.Sum(term => term.Sections);

    public int Meetings => Terms.Sum(term => term.Meetings);

    public int ExtractionFailures => Terms.Sum(term => term.ExtractionFailures);

    public int RowsWritten => Terms.Sum(term => term.RowsWritten);

    public FieldCompleteness Fields =>
        Terms.Aggregate(FieldCompleteness.Empty, (total, term) => term.Fields is null ? total : total + term.Fields);

    #endregion State
}
