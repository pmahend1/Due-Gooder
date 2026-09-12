using System.Text.Json;
using System.Text.Json.Serialization;
using DueGooder.Application.Pipeline;

namespace DueGooder.Cli;

/// <summary>
/// The run's machine-readable results, <c>reports/&lt;run-id&gt;.json</c>. Rewritten after every school, so an
/// unattended run that dies halfway still leaves what it collected.
/// </summary>
internal sealed class RunReportFile(string filePath, string runId, IReadOnlyDictionary<string, string> settings)
{
    #region State

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();

    public string FilePath => filePath;

    #endregion State

    #region Methods

    public void Write(RunResult run)
    {
        lock (_lock)
        {
            var temporaryPath = filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new { runId, settings, run }, JsonOptions));
            File.Move(temporaryPath, filePath, overwrite: true);
        }
    }

    #endregion Methods
}
