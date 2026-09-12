namespace DueGooder.Domain;

/// <summary>Natural key of a term: school + the platform's term code (e.g. Banner's <c>202610</c>).</summary>
public readonly record struct TermKey(string SchoolId, string TermCode);
