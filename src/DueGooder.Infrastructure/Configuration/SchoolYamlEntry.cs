namespace DueGooder.Infrastructure.Configuration;

/// <summary>One <c>schools:</c> entry in <c>config/schools.yaml</c>, shaped for YamlDotNet to bind to.</summary>
internal sealed class SchoolYamlEntry
{
    #region State

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Homepage { get; set; } = "";

    public string Timezone { get; set; } = "";

    public string Platform { get; set; } = "";

    /// <summary>Platform entry point. Not every school has one yet; discovery (T12) fills the rest in.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Banner multi-institution code, for servers that host several campuses.</summary>
    public string? MepCode { get; set; }

    #endregion State
}
