using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>A stored section together with the term context an export needs but <see cref="Section"/> doesn't carry.</summary>
/// <param name="TermName">Display name of the section's term, as last collected.</param>
/// <param name="LastConfirmedAt">When a run last saw this exact section.</param>
public sealed record ExportedSection(Section Section, string? TermName, DateTimeOffset LastConfirmedAt);
