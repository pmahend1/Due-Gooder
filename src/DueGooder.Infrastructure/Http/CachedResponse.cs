namespace DueGooder.Infrastructure.Http;

/// <summary>A successful response as stored in the dev response cache.</summary>
/// <param name="Url">The URL originally requested, kept so cache files can be inspected by hand.</param>
/// <param name="StatusCode">HTTP status.</param>
/// <param name="MediaType">Content-Type media type, when the server sent one.</param>
/// <param name="Body">Response body as text.</param>
internal sealed record CachedResponse(string Url, int StatusCode, string? MediaType, string Body);
