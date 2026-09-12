using DueGooder.Application.Pipeline;

namespace DueGooder.Cli;

/// <summary>Timestamped progress lines on stdout, plus a rewrite of the run's JSON after every finished school.</summary>
internal sealed class ConsoleRunProgress(RunReportFile report, DateTimeOffset startedAt, TimeProvider timeProvider)
    : IRunProgress
{
    #region State

    private readonly Lock _lock = new();

    private readonly List<SchoolRunResult> _finished = [];

    #endregion State

    #region Methods

    public void Log(string schoolId, string message) => WriteLine($"[{schoolId}] {message}");

    public void SchoolFinished(SchoolRunResult result)
    {
        lock (_lock)
        {
            _finished.Add(result);
            report.Write(new RunResult(startedAt, timeProvider.GetUtcNow() - startedAt, Completed: false, [.. _finished]));
        }
    }

    public void WriteLine(string message) => Console.WriteLine($"{timeProvider.GetUtcNow():yyyy-MM-ddTHH:mm:ssZ} {message}");

    #endregion Methods
}
