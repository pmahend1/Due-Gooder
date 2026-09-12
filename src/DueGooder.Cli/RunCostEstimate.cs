using DueGooder.Application.Pipeline;

namespace DueGooder.Cli;

/// <summary>
/// The run's cost: measured runtime, bytes, storage and LLM tokens multiplied by public list prices. The inputs are
/// measured; the prices, the machine choice and the 1,000-school extrapolation are estimates.
/// </summary>
internal sealed record RunCostEstimate
{
    #region State

    /// <summary>AWS EC2 t4g.small (2 vCPU, 2 GiB) on demand in us-east-1, September 2026. Estimate.</summary>
    public decimal VmDollarsPerHour { get; init; } = 0.0168m;

    /// <summary>AWS doesn't charge for data transferred in from the internet, and responses are almost all of our traffic.</summary>
    public decimal InboundDollarsPerGb { get; init; } = 0m;

    /// <summary>Amazon EBS gp3 in us-east-1. Estimate.</summary>
    public decimal StorageDollarsPerGbMonth { get; init; } = 0.08m;

    /// <summary>Claude Haiku 4.5, the model an LLM fallback would use. Estimate.</summary>
    public decimal LlmInputDollarsPerMillionTokens { get; init; } = 1m;

    public decimal LlmOutputDollarsPerMillionTokens { get; init; } = 5m;

    public decimal WallClockHours { get; init; }

    /// <summary>Every school's own duration added up; schools on different hosts overlapped in wall-clock time.</summary>
    public decimal CollectedSchoolHours { get; init; }

    public int SchoolsAttempted { get; init; }

    /// <summary>Schools with at least one collected term (collected or partial).</summary>
    public int SchoolsCollected { get; init; }

    public int ConcurrentHosts { get; init; }

    public long Requests { get; init; }

    /// <summary>Response bodies after decompression, in GB of 10^9 bytes as cloud providers bill them.</summary>
    public decimal BodyGb { get; init; }

    /// <summary>Size of the database file when the report was written; null when the file wasn't found.</summary>
    public decimal? DatabaseGb { get; init; }

    /// <summary>Measured as 0: the pipeline has no LLM fallback yet, so no tokens were spent.</summary>
    public long LlmInputTokens { get; init; }

    public long LlmOutputTokens { get; init; }

    public decimal ComputeDollars => VmDollarsPerHour * WallClockHours;

    public decimal TransferDollars => InboundDollarsPerGb * BodyGb;

    public decimal LlmDollars =>
        (LlmInputTokens * LlmInputDollarsPerMillionTokens + LlmOutputTokens * LlmOutputDollarsPerMillionTokens) / 1_000_000m;

    public decimal RunDollars => ComputeDollars + TransferDollars + LlmDollars;

    public decimal? DollarsPerCollectedSchool => SchoolsCollected is 0 ? null : RunDollars / SchoolsCollected;

    public decimal? StorageDollarsPerMonth => DatabaseGb * StorageDollarsPerGbMonth;

    /// <summary>Wall-clock hours for 1,000 schools like these, if <see cref="ConcurrentHosts"/> hosts run at once. Estimate.</summary>
    public decimal? HoursPerThousandSchools =>
        SchoolsCollected is 0 || ConcurrentHosts is 0 ? null : CollectedSchoolHours / SchoolsCollected * 1000m / ConcurrentHosts;

    public decimal? DollarsPerThousandSchools =>
        HoursPerThousandSchools * VmDollarsPerHour + (SchoolsCollected is 0 ? null : (TransferDollars + LlmDollars) / SchoolsCollected * 1000m);

    public decimal? StorageDollarsPerMonthPerThousandSchools =>
        SchoolsCollected is 0 ? null : StorageDollarsPerMonth / SchoolsCollected * 1000m;

    #endregion State

    #region Methods

    public static RunCostEstimate From(RunResult run, int concurrentHosts, long? databaseBytes)
    {
        var collected = run.Schools.Where(school => school.Status is not SchoolRunStatus.Failed).ToList();
        return new RunCostEstimate
        {
            WallClockHours = (decimal)run.Duration.TotalHours,
            CollectedSchoolHours = (decimal)collected.Sum(school => school.Duration.TotalHours),
            SchoolsAttempted = run.Schools.Count,
            SchoolsCollected = collected.Count,
            ConcurrentHosts = concurrentHosts,
            Requests = run.Schools.Sum(school => school.Requests.Requests),
            BodyGb = run.Schools.Sum(school => school.Requests.BodyBytes) / 1_000_000_000m,
            DatabaseGb = databaseBytes / 1_000_000_000m,
        };
    }

    #endregion Methods
}
