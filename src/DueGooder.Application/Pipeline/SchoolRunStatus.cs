namespace DueGooder.Application.Pipeline;

/// <summary>How far a school got in one run.</summary>
public enum SchoolRunStatus
{
    /// <summary>Every attempted term was collected completely and stored.</summary>
    Collected,

    /// <summary>
    /// Some terms were collected; others failed, the school stopped after repeated failures, or a stored term has a
    /// recorded gap.
    /// </summary>
    Partial,

    /// <summary>Nothing was collected; <see cref="SchoolRunResult.FailureReason"/> says why.</summary>
    Failed,

    /// <summary>No connector confirmed the site, so the school is in the manual review queue with the discovery evidence.</summary>
    NotIdentified,
}
