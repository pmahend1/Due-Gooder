namespace DueGooder.Domain;

/// <summary>
/// A field we tried to extract and could not. This is different from a field the school
/// doesn't publish, which is just <c>null</c> with no failure entry.
/// </summary>
/// <param name="Field">Name of the domain property that failed, e.g. <c>nameof(Meeting.StartTime)</c>.</param>
/// <param name="Reason">Why it failed, written for the run report.</param>
/// <param name="RawValue">The source text we could not parse, kept for auditing.</param>
public sealed record ExtractionFailure(string Field, string Reason, string? RawValue);
