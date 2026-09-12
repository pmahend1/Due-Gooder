using DueGooder.Application;
using DueGooder.Connectors.Banner9;
using DueGooder.Domain;
using DueGooder.Infrastructure.Configuration;
using DueGooder.Infrastructure.Http;
using DueGooder.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

const string DefaultConfigPath = "config/schools.yaml";

const string DefaultDatabasePath = "data/duegooder.db";

if (args is not [ "run", .. ])
{
    Console.Error.WriteLine("Usage: duegooder run --school <id> [--term <code>] [--config <path>] [--db <path>]");
    return 1;
}

var schoolId = GetOption(args, "--school");
if (schoolId is null)
{
    Console.Error.WriteLine("Missing required option --school <id>");
    return 1;
}

var configPath = GetOption(args, "--config") ?? DefaultConfigPath;
var databasePath = GetOption(args, "--db") ?? DefaultDatabasePath;
var requestedTermCode = GetOption(args, "--term");

var schools = SchoolsYamlLoader.Load(configPath);
var schoolConfig = schools.FirstOrDefault(config => config.Target.School.Id == schoolId);
if (schoolConfig is null)
{
    Console.Error.WriteLine($"No school '{schoolId}' with a base_url in {configPath}");
    return 1;
}

var connector = ConnectorFor(schoolConfig.Platform);
if (connector is null)
{
    Console.Error.WriteLine($"No connector for platform '{schoolConfig.Platform}'");
    return 1;
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
var dbOptions = new DbContextOptionsBuilder<DueGooderDbContext>().UseSqlite($"Data Source={databasePath}").Options;
await using var db = new DueGooderDbContext(dbOptions);
await db.Database.EnsureCreatedAsync();
var repository = new EfSectionRepository(db);

try
{
    var terms = await connector.ListTermsAsync(schoolConfig.Target, CancellationToken.None);
    var term = ChooseTerm(terms, requestedTermCode);
    if (term is null)
    {
        Console.Error.WriteLine(requestedTermCode is null
            ? $"{schoolId} published no terms"
            : $"{schoolId} has no term '{requestedTermCode}'");
        return 1;
    }

    var sections = new List<Section>();
    await foreach (var raw in connector.CollectSectionsAsync(schoolConfig.Target, term, CancellationToken.None))
    {
        sections.Add(connector.Map(raw));
    }

    var rowsWritten = await repository.UpsertAsync(term, sections, CancellationToken.None);

    Console.WriteLine($"{schoolId}: {terms.Count} terms published; collected \"{term.Name}\" ({term.Key.TermCode})");
    Console.WriteLine($"Sections: {sections.Count}");
    Console.WriteLine($"Meetings: {sections.Sum(section => section.Meetings.Count)}");
    Console.WriteLine($"Extraction failures: {sections.Sum(section => section.Failures.Count)}");
    Console.WriteLine($"Rows written to {databasePath}: {rowsWritten}");
    return 0;
}
catch (ConnectorException exception)
{
    Console.Error.WriteLine($"{schoolId}: {exception.Message}");
    if (exception.SourceUrl is not null)
    {
        Console.Error.WriteLine($"  source: {exception.SourceUrl}");
    }

    return 1;
}

static string? GetOption(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static IConnector? ConnectorFor(string platform) => platform switch
{
    "banner9" => new Banner9Connector(new HttpFetcher(HttpFetcher.DefaultUserAgent)),
    _ => null,
};

// With no --term, prefer the first term that isn't a future "(View Only)" listing, since that's
// normally the term someone means by "run this school".
static Term? ChooseTerm(IReadOnlyList<Term> terms, string? requestedTermCode) => requestedTermCode is not null
    ? terms.FirstOrDefault(term => term.Key.TermCode == requestedTermCode)
    : terms.FirstOrDefault(term => term.Name?.Contains("View Only", StringComparison.OrdinalIgnoreCase) is not true)
        ?? terms.FirstOrDefault();
