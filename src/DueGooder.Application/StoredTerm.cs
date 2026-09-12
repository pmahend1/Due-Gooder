namespace DueGooder.Application;

/// <summary>A term as the database holds it before this run, to compare this run against.</summary>
/// <param name="TermCode">The platform's term code.</param>
/// <param name="Name">Display name when last stored.</param>
/// <param name="SectionCount">Sections the last successful collection of this term saw.</param>
/// <param name="LastConfirmedAt">When a run last collected this term (UTC).</param>
public sealed record StoredTerm(string TermCode, string? Name, int SectionCount, DateTimeOffset LastConfirmedAt);
