using System.Runtime.CompilerServices;
using DueGooder.Application;
using DueGooder.Domain;

namespace DueGooder.Tests;

/// <summary>
/// A connector with scripted answers that also records how many calls were in flight at once, per host
/// and overall, so pipeline tests can check the concurrency rules.
/// </summary>
internal sealed class ScriptedConnector : IConnector
{
    #region State

    private readonly Lock _lock = new();

    private readonly Dictionary<string, int> _activeByHost = [];

    private int _active;

    public string Platform => "scripted";

    public List<string> TermCodes { get; init; } = ["t1"];

    public HashSet<string> SchoolsFailingToListTerms { get; init; } = [];

    public HashSet<string> FailingTermCodes { get; init; } = [];

    public int SectionsPerTerm { get; init; } = 2;

    /// <summary>Yields the first section of every term twice, like a page boundary that shifted.</summary>
    public bool RepeatFirstSection { get; init; }

    public TimeSpan ListingDelay { get; init; } = TimeSpan.Zero;

    public int MaxActivePerHost { get; private set; }

    public int MaxActiveOverall { get; private set; }

    #endregion State

    #region Methods

    public Task<Fingerprint> FingerprintAsync(Uri candidate, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public async Task<IReadOnlyList<Term>> ListTermsAsync(ConnectorTarget target, CancellationToken cancellationToken)
    {
        Enter(target.BaseUrl.Authority);
        try
        {
            await Task.Delay(ListingDelay, cancellationToken);
            if (SchoolsFailingToListTerms.Contains(target.School.Id))
            {
                throw new ConnectorException("getTerms returned HTTP 500", new Uri(target.BaseUrl, "getTerms"));
            }

            return TermCodes.Select(code => NewTerm(target, code)).ToList();
        }
        finally
        {
            Exit(target.BaseUrl.Authority);
        }
    }

    public async IAsyncEnumerable<RawSection> CollectSectionsAsync(ConnectorTarget target,
                                                                   Term term,
                                                                   [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (FailingTermCodes.Contains(term.Key.TermCode))
        {
            throw new ConnectorException("term/search redirected (HTTP 302)", new Uri(target.BaseUrl, "term/search"));
        }

        for (var index = 0; index < SectionsPerTerm; index++)
        {
            yield return new RawSection(term.Key, target.BaseUrl, DateTimeOffset.UnixEpoch, $"crn{index}");
        }

        if (RepeatFirstSection)
        {
            yield return new RawSection(term.Key, target.BaseUrl, DateTimeOffset.UnixEpoch, "crn0");
        }
    }

    public Section Map(RawSection raw) =>
        new()
        {
            Key = new SectionKey(raw.Term.SchoolId, raw.Term.TermCode, "ART", "101", raw.Payload),
            Meetings = [new Meeting()],
            SourceUrl = raw.SourceUrl,
            RetrievedAt = raw.RetrievedAt,
        };

    private static Term NewTerm(ConnectorTarget target, string code) =>
        new()
        {
            Key = new TermKey(target.School.Id, code),
            Name = $"Term {code}",
            SourceUrl = target.BaseUrl,
            RetrievedAt = DateTimeOffset.UnixEpoch,
        };

    private void Enter(string host)
    {
        lock (_lock)
        {
            _activeByHost[host] = _activeByHost.GetValueOrDefault(host) + 1;
            _active++;
            MaxActivePerHost = Math.Max(MaxActivePerHost, _activeByHost[host]);
            MaxActiveOverall = Math.Max(MaxActiveOverall, _active);
        }
    }

    private void Exit(string host)
    {
        lock (_lock)
        {
            _activeByHost[host]--;
            _active--;
        }
    }

    #endregion Methods
}
