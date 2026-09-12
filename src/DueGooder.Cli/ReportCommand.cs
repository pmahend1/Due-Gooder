using System.Globalization;
using DueGooder.Application.Pipeline;

namespace DueGooder.Cli;

/// <summary><c>report --run &lt;json&gt;</c>: fills in the cost and writes the Markdown report for a run that already finished.</summary>
internal static class ReportCommand
{
    #region State

    public const string DefaultHumanStepsPath = "config/human-steps.md";

    public const string Usage = """
        Usage: duegooder report --run <reports/run-id.json> [--fields-from <run json>] [--human-steps <path>]

          --run          finished run: its JSON gets the cost filled in and its .md is written next to it
          --fields-from  field completeness from another run over the same data, e.g. a --cache use replay with the
                         current mapper, for a run whose results predate field-completeness counting
          --human-steps  Markdown list of every human step (default config/human-steps.md)
        """;

    #endregion State

    #region Methods

    public static int Execute(string[] args)
    {
        var values = new Dictionary<string, string>();
        for (var index = 0; index < args.Length; index += 2)
        {
            if (args[index] is not ("--run" or "--fields-from" or "--human-steps") || index + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Unknown option or missing value: {args[index]}");
                Console.Error.WriteLine(Usage);
                return 1;
            }

            values[args[index]] = args[index + 1];
        }

        if (values.TryGetValue("--run", out var runPath) is false)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        var document = RunReportFile.Read(runPath);
        var fieldsBySchool = FieldsBySchool(document);
        var fieldsSource = "counted by this run";
        if (values.TryGetValue("--fields-from", out var fieldsPath))
        {
            var other = RunReportFile.Read(fieldsPath);
            fieldsBySchool = FieldsBySchool(other);
            fieldsSource = $"counted by {other.RunId}, which mapped this run's recorded responses again from the dev cache "
                           + "with the current mapper";
        }

        var concurrentHosts = int.TryParse(document.Settings.GetValueOrDefault("maxConcurrentHosts"),
                                           NumberStyles.None,
                                           CultureInfo.InvariantCulture,
                                           out var hosts)
            ? hosts
            : 1;
        var withCost = document with
        {
            Cost = document.Cost
                   ?? RunCostEstimate.From(document.Run, concurrentHosts, FileSize(document.Settings.GetValueOrDefault("database", ""))),
        };
        RunReportFile.Save(runPath, withCost);
        var markdownPath = RunReportMarkdown.WriteFile(runPath,
                                                       withCost,
                                                       fieldsBySchool,
                                                       fieldsSource,
                                                       values.GetValueOrDefault("--human-steps", DefaultHumanStepsPath));
        Console.WriteLine($"Wrote {markdownPath}, and the cost into {runPath}");
        return 0;
    }

    public static long? FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : null;

    public static Dictionary<string, FieldCompleteness> FieldsBySchool(RunReportDocument document) =>
        document.Run.Schools.ToDictionary(school => school.SchoolId, school => school.Fields);

    #endregion Methods
}
