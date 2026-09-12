using DueGooder.Application;
using DueGooder.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DueGooder.Infrastructure.Configuration;

/// <summary>
/// Loads <c>config/schools.yaml</c> into <see cref="ConnectorTarget"/>s. School-specific settings
/// (e.g. Banner's <c>mep_code</c>) are copied into <see cref="ConnectorTarget.Options"/> under their
/// YAML key, never into code — this loader doesn't know which connector reads them.
/// </summary>
public static class SchoolsYamlLoader
{
    #region State

    // Mirrors Banner9Connector.MepCodeOption's value; kept as a literal so Infrastructure doesn't
    // need to reference Connectors just to pass a config key through.
    private const string MepCodeOptionKey = "mep_code";

    #endregion State

    #region Methods

    /// <summary>
    /// Schools that already have a <c>base_url</c> and so can be collected today. Schools known only
    /// by homepage wait on discovery (T12) before they can appear here.
    /// </summary>
    public static IReadOnlyList<SchoolConfig> Load(string path)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        var document = deserializer.Deserialize<SchoolsYamlDocument>(File.ReadAllText(path)) ?? new SchoolsYamlDocument();

        return document.Schools
                       .Where(entry => string.IsNullOrWhiteSpace(entry.BaseUrl) is false)
                       .Select(ToConfig)
                       .ToList();
    }

    private static SchoolConfig ToConfig(SchoolYamlEntry entry)
    {
        var school = new School
        {
            Id = entry.Id,
            Name = entry.Name,
            Homepage = new Uri(entry.Homepage),
            TimeZoneId = entry.Timezone,
        };

        var options = new Dictionary<string, string>();
        if (string.IsNullOrWhiteSpace(entry.MepCode) is false)
        {
            options[MepCodeOptionKey] = entry.MepCode;
        }

        var target = new ConnectorTarget(school, new Uri(entry.BaseUrl!)) { Options = options };
        return new SchoolConfig(entry.Platform, target);
    }

    #endregion Methods
}
