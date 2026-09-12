namespace DueGooder.Application.Pipeline;

/// <summary>One school in a run: what was collected, what it cost in requests and time, and what failed.</summary>
public sealed record SchoolRunResult
{
    #region State

    public required string SchoolId { get; init; }

    public required string Platform { get; init; }

    /// <summary>Host the school's requests went to. Schools sharing a host were collected one after another.</summary>
    public required string Host { get; init; }

    public required SchoolRunStatus Status { get; init; }

    /// <summary>Why the school failed or stopped early; null when nothing went wrong at school level.</summary>
    public string? FailureReason { get; init; }

    /// <summary>The request behind <see cref="FailureReason"/>, when known.</summary>
    public Uri? FailureSourceUrl { get; init; }

    /// <summary>Terms the platform published, before any term filter.</summary>
    public required int TermsListed { get; init; }

    /// <summary>Every term attempted, in order, with its counts or its failure.</summary>
    public required IReadOnlyList<TermRunResult> Terms { get; init; }

    public required RequestStats Requests { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required TimeSpan Duration { get; init; }

    public int TermsCollected => Terms.Count(term => term.FailureReason is null);

    public int Sections => Terms.Sum(term => term.Sections);

    public int Meetings => Terms.Sum(term => term.Meetings);

    public int ExtractionFailures => Terms.Sum(term => term.ExtractionFailures);

    public int RowsWritten => Terms.Sum(term => term.RowsWritten);

    public FieldCompleteness Fields =>
        Terms.Aggregate(FieldCompleteness.Empty, (total, term) => term.Fields is null ? total : total + term.Fields);

    #endregion State
}
