namespace DueGooder.Application;

/// <summary>An identified school: the platform to collect with and its connector settings.</summary>
/// <param name="Platform">Platform name, e.g. <c>banner9</c>; matches <see cref="IConnector.Platform"/>.</param>
/// <param name="Target">The school and its connector settings.</param>
public sealed record SchoolConfig(string Platform, ConnectorTarget Target);
