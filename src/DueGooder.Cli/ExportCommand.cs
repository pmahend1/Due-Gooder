using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DueGooder.Application;
using DueGooder.Domain;
using DueGooder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DueGooder.Cli;

/// <summary>
/// <c>export</c>: writes the full normalized data to CSV/JSON (T16), plus a representative sample for a few
/// schools and a random spot-check sheet with source URLs, both meant to be committed under <c>data/samples/</c> (T17).
/// </summary>
internal static class ExportCommand
{
    #region State

    public const string Usage = """
        Usage: duegooder export [--db <path>] [--out <dir>] [--samples-out <dir>]
                                [--sample-schools <id,id,...>] [--sample-per-school <n>]
                                [--spot-check <n>] [--seed <n>]

          --db                SQLite database to export from (default data/duegooder.db)
          --out               directory for the full normalized export, CSV + JSON (default data/export;
                               NOT meant to be committed — one school alone can be hundreds of MB)
          --samples-out       directory for the committed samples + spot-check sheet (default data/samples)
          --sample-schools    school ids to include in the sample export, comma-separated
                               (default a curated set covering variable credit, a future/view-only term,
                               a gap-recovered term, and a small school that's easy to read end to end)
          --sample-per-school cap on sections per sample school, randomly chosen (default 300); keeps
                               data/samples/ small enough to commit even for a school with 100k+ sections
          --spot-check        number of random sections in the spot-check sheet (default 50)
          --seed              random seed for picking samples and the spot-check rows, so both are
                               reproducible (default 20260912)
        """;

    // ucr: variable credit ("0 OR 4"). montgomery: a future term published "(View Only)". msudenver: a term stored
    // with a recorded gap (T14's Banner9GapRecovery). uncc: Prateek's own school, useful for eyeballing by hand.
    // cocc: small enough (11 terms, ~4.5k sections in the overnight run) to skim the sample file end to end.
    private static readonly string[] DefaultSampleSchools = ["ucr", "montgomery", "msudenver", "uncc", "cocc"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string[] KnownOptions =
        ["--db", "--out", "--samples-out", "--sample-schools", "--sample-per-school", "--spot-check", "--seed"];

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

        var databasePath = values.GetValueOrDefault("--db", "data/duegooder.db");
        if (File.Exists(databasePath) is false)
        {
            Console.Error.WriteLine($"No database at {databasePath}");
            return 1;
        }

        var outDirectory = values.GetValueOrDefault("--out", "data/export");
        var samplesDirectory = values.GetValueOrDefault("--samples-out", "data/samples");
        var sampleSchoolIds = values.TryGetValue("--sample-schools", out var schoolsCsv)
            ? schoolsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : DefaultSampleSchools;
        var samplePerSchool = int.TryParse(values.GetValueOrDefault("--sample-per-school", "300"),
                                           NumberStyles.None,
                                           CultureInfo.InvariantCulture,
                                           out var parsedPerSchool)
            ? parsedPerSchool
            : 300;
        var spotCheckCount = int.TryParse(values.GetValueOrDefault("--spot-check", "50"),
                                          NumberStyles.None,
                                          CultureInfo.InvariantCulture,
                                          out var parsedCount)
            ? parsedCount
            : 50;
        var seed = int.TryParse(values.GetValueOrDefault("--seed", "20260912"),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var parsedSeed)
            ? parsedSeed
            : 20260912;

        var dbOptions = new DbContextOptionsBuilder<DueGooderDbContext>().UseSqlite($"Data Source={databasePath}").Options;
        var reader = new EfExportReader(() => new DueGooderDbContext(dbOptions));
        var all = await reader.GetSectionsAsync(null, CancellationToken.None);

        Directory.CreateDirectory(outDirectory);
        WriteJson(Path.Combine(outDirectory, "sections.json"), all);
        WriteCsv(Path.Combine(outDirectory, "sections.csv"), all);
        Console.WriteLine($"Full export: {all.Count} sections from {databasePath} -> {outDirectory}");

        var random = new Random(seed);
        var sampleIdSet = sampleSchoolIds.ToHashSet();
        var byMatchingSchool = all.Where(exported => sampleIdSet.Contains(exported.Section.Key.SchoolId))
                                  .ToLookup(exported => exported.Section.Key.SchoolId);
        // Capped per school, not the whole school: a sample school with 100k+ sections (ucr, uncc) would
        // otherwise make data/samples/ too large to commit — GitHub rejects any file over 100 MB.
        var samples = sampleIdSet.SelectMany(id => byMatchingSchool[id].OrderBy(_ => random.Next()).Take(samplePerSchool))
                                 .ToList();
        var missingSchools = sampleIdSet.Where(id => byMatchingSchool[id].Any() is false)
                                        .Order(StringComparer.Ordinal)
                                        .ToList();
        Directory.CreateDirectory(samplesDirectory);
        WriteJson(Path.Combine(samplesDirectory, "sections.sample.json"), samples);
        WriteCsv(Path.Combine(samplesDirectory, "sections.sample.csv"), samples);

        var spotCheck = all.OrderBy(_ => random.Next()).Take(spotCheckCount).ToList();
        WriteSpotCheckCsv(Path.Combine(samplesDirectory, "spot-check.csv"), spotCheck);
        WriteSamplesReadme(Path.Combine(samplesDirectory, "README.md"),
                           sampleSchoolIds,
                           samples,
                           missingSchools,
                           samplePerSchool,
                           spotCheck.Count,
                           all.Count,
                           databasePath,
                           seed);

        Console.WriteLine($"Samples: {samples.Count} sections from {sampleIdSet.Count - missingSchools.Count}/{sampleIdSet.Count} "
                          + $"requested schools -> {samplesDirectory}"
                          + (missingSchools.Count is 0 ? "" : $" (no stored sections for: {string.Join(", ", missingSchools)})"));
        Console.WriteLine($"Spot-check: {spotCheck.Count} random sections -> {Path.Combine(samplesDirectory, "spot-check.csv")}");
        return 0;
    }

    // Streams straight to disk via Utf8JsonWriter instead of JsonSerializer.Serialize(...) returning one string:
    // the full export is close to .NET's ~1 GB string-length ceiling and throws OutOfMemoryException past it.
    private static void WriteJson(string path, IReadOnlyList<ExportedSection> sections)
    {
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, sections, JsonOptions);
    }

    private static void WriteCsv(string path, IReadOnlyList<ExportedSection> sections)
    {
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        WriteCsvRow(writer,
                   "school_id", "term_code", "term_name", "subject", "course_number", "crn", "section_number",
                   "title", "credits", "credits_min", "credits_max", "credits_raw", "instructional_method", "campus",
                   "capacity", "enrolled", "waitlist_capacity", "waitlist_count",
                   "cross_list_group", "cross_list_capacity", "cross_list_enrolled",
                   "meetings", "instructors", "extraction_failures",
                   "source_url", "retrieved_at", "last_confirmed_at");
        foreach (var exported in sections.OrderBy(exported => exported.Section.Key.SchoolId, StringComparer.Ordinal)
                                         .ThenBy(exported => exported.Section.Key.TermCode, StringComparer.Ordinal)
                                         .ThenBy(exported => exported.Section.Key.Subject, StringComparer.Ordinal)
                                         .ThenBy(exported => exported.Section.Key.CourseNumber, StringComparer.Ordinal)
                                         .ThenBy(exported => exported.Section.Key.SectionId, StringComparer.Ordinal))
        {
            var section = exported.Section;
            WriteCsvRow(writer,
                       section.Key.SchoolId, section.Key.TermCode, exported.TermName,
                       section.Key.Subject, section.Key.CourseNumber, section.Key.SectionId, section.DisplaySectionNumber,
                       section.Title, Decimal(section.Credits), Decimal(section.CreditsMin), Decimal(section.CreditsMax),
                       section.CreditsRaw, section.InstructionalMethod, section.Campus,
                       Integer(section.Capacity), Integer(section.Enrolled), Integer(section.WaitlistCapacity), Integer(section.WaitlistCount),
                       section.CrossListGroup, Integer(section.CrossListCapacity), Integer(section.CrossListEnrolled),
                       FormatMeetings(section.Meetings), FormatInstructors(section.Instructors), FormatFailures(section.Failures),
                       section.SourceUrl.ToString(), Instant(section.RetrievedAt), Instant(exported.LastConfirmedAt));
        }
    }

    private static void WriteSpotCheckCsv(string path, IReadOnlyList<ExportedSection> sections)
    {
        using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
        WriteCsvRow(writer,
                   "school_id", "term_code", "term_name", "subject", "course_number", "crn", "section_number",
                   "title", "days", "start_time", "end_time", "building", "room", "instructor",
                   "source_url", "match", "note");
        foreach (var exported in sections)
        {
            var section = exported.Section;
            var meeting = section.Meetings.FirstOrDefault();
            var instructor = section.Instructors.FirstOrDefault(instructor => instructor.IsPrimary) ?? section.Instructors.FirstOrDefault();

            // "match" and "note" are left blank here for T18's hand and automated spot-check to fill in.
            WriteCsvRow(writer,
                       section.Key.SchoolId, section.Key.TermCode, exported.TermName,
                       section.Key.Subject, section.Key.CourseNumber, section.Key.SectionId, section.DisplaySectionNumber,
                       section.Title, DaysLetters(meeting?.Days), meeting?.StartTimeRaw, meeting?.EndTimeRaw,
                       meeting?.Building, meeting?.Room, instructor?.Name,
                       section.SourceUrl.ToString(), null, null);
        }
    }

    private static void WriteSamplesReadme(string path,
                                           IReadOnlyList<string> requestedSchools,
                                           IReadOnlyList<ExportedSection> samples,
                                           IReadOnlyList<string> missingSchools,
                                           int samplePerSchool,
                                           int spotCheckCount,
                                           int totalSections,
                                           string databasePath,
                                           int seed)
    {
        var bySchool = samples.GroupBy(exported => exported.Section.Key.SchoolId)
                              .OrderBy(group => group.Key, StringComparer.Ordinal)
                              .Select(group => $"- `{group.Key}`: {group.Count()} sections")
                              .ToList();
        var missingLine = missingSchools.Count is 0
            ? ""
            : $"\nNo stored sections for: {string.Join(", ", missingSchools)} (dropped from this DB, or blocked from collecting).\n";
        var text = $"""
            # Sample data (T17)

            Generated by `dotnet run --project src/DueGooder.Cli -- export`, from `{databasePath}` ({totalSections} sections total).

            - `sections.sample.json` / `sections.sample.csv`: full normalized records for {samples.Count} sections
              across {requestedSchools.Count - missingSchools.Count} schools (up to {samplePerSchool} sections
              per school, chosen at random with seed {seed} — a school with 100k+ sections is capped so this
              stays small enough to commit; see `--sample-per-school` in the CLI usage), chosen to cover edge
              cases: variable credit (`ucr`), a future term published "(View Only)" (`montgomery`), a term
              stored with a recorded collection gap (`msudenver`), TBA/online meetings and cross-listed sections
              (present at most of these schools), plus `uncc` and `cocc` for an easy-to-read example. See the
              field docs in `AGENTS.md` and `src/DueGooder.Domain/Section.cs` / `Meeting.cs` for what each
              column means; a `null`/empty field means the school doesn't publish it, not that extraction
              failed (failures are listed separately, per record, in `extraction_failures` / `failures`).
            - `spot-check.csv`: {spotCheckCount} sections chosen at random (seed {seed}) across every collected
              school, with `source_url` for each and blank `match`/`note` columns for T18's manual and automated checks.

            Schools in the sample:
            {string.Join("\n", bySchool)}
            {missingLine}
            """;
        File.WriteAllText(path, text);
    }

    private static string FormatMeetings(IReadOnlyList<Meeting> meetings) =>
        string.Join(" ; ", meetings.Select(meeting => $"{DaysLetters(meeting.Days)} {meeting.StartTimeRaw}-{meeting.EndTimeRaw} "
                                                       + $"{meeting.Building} {meeting.Room} {meeting.MeetingType}".Trim()));

    private static string FormatInstructors(IReadOnlyList<Instructor> instructors) =>
        string.Join(" ; ", instructors.Select(instructor => instructor.IsPrimary ? $"{instructor.Name} (primary)" : instructor.Name));

    private static string FormatFailures(IReadOnlyList<ExtractionFailure> failures) =>
        string.Join(" ; ", failures.Select(failure => $"{failure.Field}: {failure.Reason}"));

    private static string? DaysLetters(MeetingDays? days)
    {
        if (days is not { } value)
        {
            return null;
        }

        var letters = new[] { (MeetingDays.Monday, 'M'), (MeetingDays.Tuesday, 'T'), (MeetingDays.Wednesday, 'W'),
                              (MeetingDays.Thursday, 'R'), (MeetingDays.Friday, 'F'), (MeetingDays.Saturday, 'S'),
                              (MeetingDays.Sunday, 'U') };
        var text = new string(letters.Where(pair => value.HasFlag(pair.Item1)).Select(pair => pair.Item2).ToArray());
        return text is "" ? "none" : text;
    }

    private static string? Decimal(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string? Integer(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static string Instant(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

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
        if (value is null or "")
        {
            return "";
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    #endregion Methods
}
