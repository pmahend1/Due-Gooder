namespace DueGooder.Application.Discovery;

/// <summary>An <c>&lt;a href&gt;</c> on a fetched page, resolved to an absolute URL.</summary>
/// <param name="Url">Absolute http(s) URL without its fragment.</param>
/// <param name="Text">The link's visible text, tags stripped and whitespace collapsed.</param>
public sealed record PageLink(Uri Url, string Text);
