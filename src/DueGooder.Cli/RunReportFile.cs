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

    public void Write(RunResult run, RunCostEstimate? cost = null)
    {
        lock (_lock)
        {
            Save(filePath, new RunReportDocument(runId, settings, run, cost));
        }
    }

    public static RunReportDocument Read(string path) =>
        JsonSerializer.Deserialize<RunReportDocument>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path} holds no run report");

    // Written to a temporary file first, so a reader never sees half a report.
    public static void Save(string path, RunReportDocument document)
    {
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    #endregion Methods
}
