using DueGooder.Domain;

namespace DueGooder.Application;

/// <summary>
/// One school from <c>config/schools.yaml</c> before identification. The homepage is always known; the platform and
/// its base URL only when config sets them, which skips discovery for that school.
/// </summary>
/// <param name="School">The school.</param>
/// <param name="Platform">Configured platform, e.g. <c>banner9</c>; null when discovery has to find it.</param>
/// <param name="BaseUrl">Configured platform entry point; null when discovery has to find it.</param>
/// <param name="Options">Platform-specific settings from config, e.g. Banner's <c>mep_code</c>.</param>
public sealed record ListedSchool(School School,
                                  string? Platform,
                                  Uri? BaseUrl,
                                  IReadOnlyDictionary<string, string> Options)
{
    #region State

    public bool IsConfigured => Platform is not null && BaseUrl is not null;

    #endregion State
}
