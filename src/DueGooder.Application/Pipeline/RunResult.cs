namespace DueGooder.Application.Pipeline;

/// <summary>A whole run over the school list.</summary>
/// <param name="StartedAt">When the run started (UTC).</param>
/// <param name="Duration">Wall-clock time of the run.</param>
/// <param name="Completed">False when the run was cancelled before every school finished.</param>
/// <param name="Schools">Finished schools, in config order.</param>
public sealed record RunResult(DateTimeOffset StartedAt,
                               TimeSpan Duration,
                               bool Completed,
                               IReadOnlyList<SchoolRunResult> Schools);
