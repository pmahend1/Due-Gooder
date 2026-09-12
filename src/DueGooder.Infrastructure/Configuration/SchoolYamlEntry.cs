namespace DueGooder.Infrastructure.Configuration;

/// <summary>One <c>schools:</c> entry in <c>config/schools.yaml</c>, shaped for YamlDotNet to bind to.</summary>
internal sealed class SchoolYamlEntry
{
    #region State

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Homepage { get; set; } = "";

    public string Timezone { get; set; } = "";

    /// <summary>Platform to collect with; set together with <see cref="BaseUrl"/> to skip discovery.</summary>
    public string? Platform { get; set; }

    /// <summary>Platform entry point. Most schools leave it out and discovery finds it from the homepage.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Banner multi-institution code, for servers that host several campuses.</summary>
    public string? MepCode { get; set; }

    #endregion State
}
