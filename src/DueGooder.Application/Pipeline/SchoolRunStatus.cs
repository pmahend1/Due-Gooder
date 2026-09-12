namespace DueGooder.Application.Pipeline;

/// <summary>How far a school got in one run.</summary>
public enum SchoolRunStatus
{
    /// <summary>Every attempted term was collected and stored.</summary>
    Collected,

    /// <summary>Some terms were collected; others failed, or the school stopped after repeated failures.</summary>
    Partial,

    /// <summary>Nothing was collected; <see cref="SchoolRunResult.FailureReason"/> says why.</summary>
    Failed,
}
