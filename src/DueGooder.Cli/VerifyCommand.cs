using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using DueGooder.Application;
using DueGooder.Application.Discovery;
using DueGooder.Connectors.Banner9;
using DueGooder.Domain;
using DueGooder.Infrastructure.Configuration;
using DueGooder.Infrastructure.Http;

namespace DueGooder.Cli;

/// <summary>
/// <c>verify</c> (T18): re-collects each spot-check row's school+term LIVE from the registrar and diffs it against
/// the stored row, matched by CRN. This is the automated half of the accuracy check — it stands in for a human
/// hand-check that this project's write-up discloses as skipped, not performed, so its match rate is reported as
/// "automated-only" rather than blended with a human number.
/// </summary>
internal static class VerifyCommand
{
    #region State

    public const string Usage = """
        Usage: duegooder verify [--samples <path>] [--schools <path>] [--min-interval <seconds>] [--max-hosts <n>]

          Re-collects, live, the school+term of every row in --samples and compares title, days, start/end time,
          building, room and instructor against the stored values, matched by CRN. Automated only: there is no
          human review here, and the match/note columns it writes should be read as that, not as a hand spot-check.
          Rewrites --samples in place (match: yes/no, note: what differed or why a row couldn't be checked).

          --samples       spot-check CSV to verify and rewrite (default data/samples/spot-check.csv)
          --schools       school list, for base_url/platform/options (default config/schools.yaml)
          --min-interval  seconds between request starts per host (default 2; robots.txt Crawl-delay can raise it)
          --max-hosts     schools re-collected at the same time (default 10)
        """;

    private static readonly string[] KnownOptions = ["--samples", "--schools", "--min-interval", "--max-hosts"];

    private static readonly string[] Header =
    [
        "school_id", "term_code", "term_name", "subject", "course_number", "crn", "section_number",
        "title", "days", "start_time", "end_time", "building", "room", "instructor",
        "source_url", "match", "note",
    ];

    #endregion State

    #region Methods

    public static async Task<int> ExecuteAsync(string[] args)
    {
        var values = new Dictionary<string, string>();
        for (var index = 0; index < args.Length; index += 2)
        {
            if (KnownOptions.Contains(args[index]) is false || index + 1 >= args.Length)
            {
                Console.Error.WriteLine($"Unknown option or missing value: {args[index]}");
                Console.Error.WriteLine(Usage);
                return 1;
            }

            values[args[index]] = args[index + 1];
        }

        var samplesPath = values.GetValueOrDefault("--samples", "data/samples/spot-check.csv");
        var schoolsPath = values.GetValueOrDefault("--schools", "config/schools.yaml");
        if (File.Exists(samplesPath) is false)
        {
            Console.Error.WriteLine($"No spot-check sheet at {samplesPath}");
            return 1;
        }

        var minInterval = double.TryParse(values.GetValueOrDefault("--min-interval", "2"),
                                          NumberStyles.Float,
                                          CultureInfo.InvariantCulture,
                                          out var parsedInterval)
            ? parsedInterval
            : 2;
        var maxHosts = int.TryParse(values.GetValueOrDefault("--max-hosts", "10"),
                                    NumberStyles.None,
                                    CultureInfo.InvariantCulture,
                                    out var parsedHosts)
            ? parsedHosts
            : 10;

        var rows = ReadCsv(samplesPath);
        var listedSchools = SchoolsYamlLoader.Load(schoolsPath).ToDictionary(school => school.School.Id);

        var fetcherOptions = new HttpFetcherOptions
        {
            MinRequestInterval = TimeSpan.FromSeconds(minInterval),
            CacheMode = ResponseCacheMode.Off,
            CacheKeyIgnoredParameters = Banner9Connector.SessionScopedParameters,
        };
        using var fetchers = new HttpFetcherFactory(fetcherOptions, TimeProvider.System);

        var log = new object();
        var completed = 0;
        var groups = rows.Select((row, index) => (Row: row, Index: index))
                         .GroupBy(entry => entry.Row.SchoolId)
                         .ToList();

        await Parallel.ForEachAsync(groups,
                                    new ParallelOptions { MaxDegreeOfParallelism = maxHosts },
                                    async (group, cancellationToken) =>
                                    {
                                        await VerifySchoolAsync(group.Key, group.ToList(), listedSchools, fetchers, cancellationToken);
                                        lock (log)
                                        {
                                            completed++;
                                            Console.WriteLine($"[{completed}/{groups.Count}] {group.Key}: "
                                                              + string.Join("; ",
                                                                            group.Select(entry =>
                                                                                $"{entry.Row.Crn}={entry.Row.Match ?? "?"}")));
                                        }
                                    });

        WriteCsv(samplesPath, rows);

        var checkable = rows.Count(row => row.Match is "yes" or "no");
        var matched = rows.Count(row => row.Match is "yes");
        var rate = checkable is 0 ? 0.0 : (double)matched / checkable;
        Console.WriteLine();
        Console.WriteLine($"Automated-only accuracy: {matched}/{checkable} matched ({rate:P1}) "
                          + $"of {rows.Count} sampled rows -> {samplesPath}");
        var unresolved = rows.Count(row => row.Match is "unknown" or null or "");
        if (unresolved > 0)
        {
            Console.WriteLine($"{unresolved} row(s) could not be checked live (registrar/config issues, see notes) "
                              + "and are excluded from the rate.");
        }

        return 0;
    }

    private static async Task VerifySchoolAsync(string schoolId,
                                                IReadOnlyList<(Row Row, int Index)> rows,
                                                IReadOnlyDictionary<string, ListedSchool> listedSchools,
                                                HttpFetcherFactory fetchers,
                                                CancellationToken cancellationToken)
    {
        if (listedSchools.TryGetValue(schoolId, out var listed) is false)
        {
            MarkAll(rows, "unknown", $"school '{schoolId}' is not in schools.yaml");
            return;
        }

        var metrics = new RequestMetrics();
        var fetcher = fetchers.Create(metrics);
        var connector = new Banner9Connector(fetcher);

        ConnectorTarget target;
        if (listed.IsConfigured)
        {
            target = new ConnectorTarget(listed.School, listed.BaseUrl!) { Options = listed.Options };
        }
        else
        {
            DiscoveryResult discovery;
            try
            {
                discovery = await new HomepageDiscovery([connector], fetcher, new DiscoveryOptions())
                    .DiscoverAsync(listed.School.Homepage, cancellationToken);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
            {
                MarkAll(rows, "unknown", $"live discovery failed: {exception.Message}");
                return;
            }

            if (discovery.Identified is false)
            {
                MarkAll(rows, "unknown", $"not identified live: {discovery.ReviewReason ?? "no platform matched"}");
                return;
            }

            if (discovery.Platform != connector.Platform)
            {
                MarkAll(rows, "unknown", $"live platform is '{discovery.Platform}', not banner9 (verify only supports banner9)");
                return;
            }

            target = new ConnectorTarget(listed.School, discovery.BaseUrl!) { Options = listed.Options };
        }

        IReadOnlyList<Term> terms;
        try
        {
            terms = await connector.ListTermsAsync(target, cancellationToken);
        }
        catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
        {
            MarkAll(rows, "unknown", $"could not list terms live (registrar error, not a data mismatch): {exception.Message}");
            return;
        }

        foreach (var termGroup in rows.GroupBy(entry => entry.Row.TermCode))
        {
            var term = terms.FirstOrDefault(candidate => candidate.Key.TermCode == termGroup.Key);
            if (term is null)
            {
                MarkAll(termGroup.ToList(), "unknown", "term is no longer listed live (not a data mismatch)");
                continue;
            }

            var targetCrns = termGroup.Select(entry => entry.Row.Crn).ToHashSet(StringComparer.Ordinal);
            var found = new Dictionary<string, Section>(StringComparer.Ordinal);
            var gaps = new List<CollectionGap>();
            try
            {
                await foreach (var raw in connector.CollectSectionsAsync(target, term, gaps, cancellationToken))
                {
                    var section = connector.Map(raw);
                    if (targetCrns.Contains(section.Key.SectionId) && found.ContainsKey(section.Key.SectionId) is false)
                    {
                        found[section.Key.SectionId] = section;
                        if (found.Count == targetCrns.Count)
                        {
                            break;
                        }
                    }
                }
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested is false)
            {
                MarkAll(termGroup.ToList(), "unknown", $"could not collect term live (registrar error, not a data mismatch): {exception.Message}");
                continue;
            }

            foreach (var entry in termGroup)
            {
                if (found.TryGetValue(entry.Row.Crn, out var live) is false)
                {
                    entry.Row.Match = "no";
                    entry.Row.Note = "crn not found in current live listing for this term";
                    continue;
                }

                Diff(entry.Row, live);
            }
        }
    }

    private static void Diff(Row row, Section live)
    {
        var meeting = live.Meetings.FirstOrDefault();
        var instructor = live.Instructors.FirstOrDefault(candidate => candidate.IsPrimary) ?? live.Instructors.FirstOrDefault();
        var mismatches = new List<string>();

        Compare(mismatches, "title", row.Title, live.Title);
        Compare(mismatches, "days", row.Days, DaysLetters(meeting?.Days));
        Compare(mismatches, "start_time", row.StartTime, meeting?.StartTimeRaw);
        Compare(mismatches, "end_time", row.EndTime, meeting?.EndTimeRaw);
        Compare(mismatches, "building", row.Building, meeting?.Building);
        Compare(mismatches, "room", row.Room, meeting?.Room);
        Compare(mismatches, "instructor", row.Instructor, instructor?.Name);

        row.Match = mismatches.Count is 0 ? "yes" : "no";
        row.Note = mismatches.Count is 0 ? "" : string.Join(" ; ", mismatches);
    }

    private static void Compare(List<string> mismatches, string field, string? stored, string? live)
    {
        var normalizedStored = string.IsNullOrEmpty(stored) ? null : stored;
        var normalizedLive = string.IsNullOrEmpty(live) ? null : live;
        if (string.Equals(normalizedStored, normalizedLive, StringComparison.Ordinal) is false)
        {
            mismatches.Add($"{field}: stored='{normalizedStored}' live='{normalizedLive}'");
        }
    }

    private static string? DaysLetters(MeetingDays? days)
    {
        if (days is not { } value)
        {
            return null;
        }

        var letters = new[]
        {
            (MeetingDays.Monday, 'M'), (MeetingDays.Tuesday, 'T'), (MeetingDays.Wednesday, 'W'),
            (MeetingDays.Thursday, 'R'), (MeetingDays.Friday, 'F'), (MeetingDays.Saturday, 'S'), (MeetingDays.Sunday, 'U'),
        };
        var text = new string(letters.Where(pair => value.HasFlag(pair.Item1)).Select(pair => pair.Item2).ToArray());
        return text is "" ? "none" : text;
    }

    private static void MarkAll(IReadOnlyList<(Row Row, int Index)> rows, string match, string note)
    {
        foreach (var entry in rows)
        {
            entry.Row.Match = match;
            entry.Row.Note = note;
        }
    }

    private static List<Row> ReadCsv(string path)
    {
        var records = ParseCsv(path);
        var rows = new List<Row>();
        for (var index = 1; index < records.Count; index++)
        {
            var fields = records[index];
            if (fields.Length is 1 && fields[0].Length is 0)
            {
                continue;
            }

            rows.Add(new Row
            {
                SchoolId = fields[0],
                TermCode = fields[1],
                TermName = fields[2],
                Subject = fields[3],
                CourseNumber = fields[4],
                Crn = fields[5],
                SectionNumber = fields[6],
                Title = fields[7],
                Days = fields[8],
                StartTime = fields[9],
                EndTime = fields[10],
                Building = fields[11],
                Room = fields[12],
                Instructor = fields[13],
                SourceUrl = fields[14],
            });
        }

        return rows;
    }

    private static void WriteCsv(string path, IReadOnlyList<Row> rows)
    {
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        WriteCsvRow(writer, Header);
        foreach (var row in rows)
        {
            WriteCsvRow(writer,
                       row.SchoolId, row.TermCode, row.TermName, row.Subject, row.CourseNumber, row.Crn, row.SectionNumber,
                       row.Title, row.Days, row.StartTime, row.EndTime, row.Building, row.Room, row.Instructor,
                       row.SourceUrl, row.Match, row.Note);
        }
    }

    private static void WriteCsvRow(TextWriter writer, params ReadOnlySpan<string?> fields)
    {
        for (var index = 0; index < fields.Length; index++)
        {
            if (index > 0)
            {
                writer.Write(',');
            }

            writer.Write(CsvField(fields[index]));
        }

        writer.Write('\n');
    }

    private static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    // A hand-rolled RFC 4180 reader (not line-splitting) because quoted fields here can contain commas.
    private static List<string[]> ParseCsv(string path)
    {
        var text = File.ReadAllText(path);
        if (text.Length > 0 && text[0] is '﻿')
        {
            text = text[1..];
        }

        var records = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c is '"')
                {
                    if (i + 1 < text.Length && text[i + 1] is '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    records.Add(fields.ToArray());
                    fields = [];
                    break;
                case '\r':
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(fields.ToArray());
        }

        return records;
    }

    #endregion Methods

    private sealed class Row
    {
        #region State

        public required string SchoolId { get; init; }

        public required string TermCode { get; init; }

        public string? TermName { get; init; }

        public string? Subject { get; init; }

        public string? CourseNumber { get; init; }

        public required string Crn { get; init; }

        public string? SectionNumber { get; init; }

        public string? Title { get; init; }

        public string? Days { get; init; }

        public string? StartTime { get; init; }

        public string? EndTime { get; init; }

        public string? Building { get; init; }

        public string? Room { get; init; }

        public string? Instructor { get; init; }

        public string? SourceUrl { get; init; }

        public string? Match { get; set; }

        public string? Note { get; set; }

        #endregion State
    }
}
