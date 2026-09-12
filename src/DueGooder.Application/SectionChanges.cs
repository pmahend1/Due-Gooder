namespace DueGooder.Application;

/// <summary>What one term's upsert changed.</summary>
/// <param name="RowsWritten">Rows inserted, updated or deleted for content changes; 0 when the source didn't change.</param>
/// <param name="Added">Sections stored for the first time.</param>
/// <param name="Changed">Stored sections whose content changed.</param>
/// <param name="Unchanged">Stored sections seen again unchanged; only their <c>last_confirmed_at</c> moved.</param>
/// <param name="NotSeen">Stored sections this collection didn't return. Kept, not confirmed, so they go stale.</param>
public sealed record SectionChanges(int RowsWritten, int Added, int Changed, int Unchanged, int NotSeen);
