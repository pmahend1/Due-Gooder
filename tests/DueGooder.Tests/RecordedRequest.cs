namespace DueGooder.Tests;

/// <summary>A request a connector made through <see cref="FixtureHttpFetcher"/>.</summary>
/// <param name="Session">Which session sent it; requests sharing a number shared cookies.</param>
internal sealed record RecordedRequest(int Session,
                                       string Method,
                                       Uri Url,
                                       IReadOnlyDictionary<string, string>? Form);
