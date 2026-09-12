using DueGooder.Application.Pipeline;

namespace DueGooder.Cli;

/// <summary>What <c>reports/&lt;run-id&gt;.json</c> holds. <see cref="Cost"/> is filled in once the run has finished.</summary>
internal sealed record RunReportDocument(string RunId,
                                        IReadOnlyDictionary<string, string> Settings,
                                        RunResult Run,
                                        RunCostEstimate? Cost = null);
