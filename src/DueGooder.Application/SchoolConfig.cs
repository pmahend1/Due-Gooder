namespace DueGooder.Application;

/// <summary>One school from <c>config/schools.yaml</c>, paired with its suspected platform.</summary>
/// <param name="Platform">Suspected platform name, e.g. <c>banner9</c>; matches <see cref="IConnector.Platform"/>.</param>
/// <param name="Target">The school and its connector settings.</param>
public sealed record SchoolConfig(string Platform, ConnectorTarget Target);
