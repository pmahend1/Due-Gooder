using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>A school paired with the base URL of its registration platform.</summary>
public sealed record ConnectorTarget(School School, Uri BaseUrl);
