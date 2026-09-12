namespace DueGooder.Infrastructure.Configuration;

/// <summary>Root shape of <c>config/schools.yaml</c>.</summary>
internal sealed class SchoolsYamlDocument
{
    #region State

    public List<SchoolYamlEntry> Schools { get; set; } = [];

    #endregion State
}
