using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>One section record exactly as the platform returned it, before mapping.</summary>
/// <param name="Term">The term the record was collected for.</param>
/// <param name="SourceUrl">The request that returned it.</param>
/// <param name="RetrievedAt">When it was fetched (UTC).</param>
/// <param name="Payload">The record's source text (JSON or HTML), kept so mapping can be re-run and audited.</param>
public sealed record RawSection(TermKey Term, Uri SourceUrl, DateTimeOffset RetrievedAt, string Payload);
