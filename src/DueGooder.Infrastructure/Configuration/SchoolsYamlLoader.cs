using DueGooder.Application;
using DueGooder.Domain;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DueGooder.Infrastructure.Configuration;

/// <summary>
/// Loads <c>config/schools.yaml</c> into <see cref="ListedSchool"/>s. School-specific settings (e.g. Banner's
/// <c>mep_code</c>) are copied into <see cref="ListedSchool.Options"/> under their YAML key, never into code — this
/// loader doesn't know which connector reads them.
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
    /// Every school in the file. Those with both <c>platform</c> and <c>base_url</c> skip discovery; the rest are
    /// identified from their homepage when a run starts.
    /// </summary>
    public static IReadOnlyList<ListedSchool> Load(string path)
    {
        var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        var document = deserializer.Deserialize<SchoolsYamlDocument>(File.ReadAllText(path)) ?? new SchoolsYamlDocument();
        return document.Schools.Select(ToListedSchool).ToList();
    }

    private static ListedSchool ToListedSchool(SchoolYamlEntry entry)
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

        return new ListedSchool(school,
                                string.IsNullOrWhiteSpace(entry.Platform) ? null : entry.Platform,
                                string.IsNullOrWhiteSpace(entry.BaseUrl) ? null : new Uri(entry.BaseUrl),
                                options);
    }

    #endregion Methods
}
