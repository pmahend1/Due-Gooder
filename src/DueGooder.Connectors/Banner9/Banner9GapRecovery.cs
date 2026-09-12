using System.Runtime.CompilerServices;
using DueGooder.Application;
using DueGooder.Domain;

namespace DueGooder.Connectors.Banner9;

/// <summary>
/// Narrows a results window Banner refused (<c>success:false</c>) by halving it until only the records Banner can't
/// return are left. Those become a <see cref="CollectionGap"/>; everything else in the window is still collected.
/// </summary>
/// <param name="budget">Requests left for recovery in this term, shared by every refused window of the term.</param>
internal sealed class Banner9GapRecovery(IHttpSession session,
                                         Banner9Endpoints endpoints,
                                         TermKey term,
                                         string sessionId,
                                         int totalCount,
                                         StrongBox<int> budget)
{
    #region State

    public List<RawSection> Recovered { get; } = [];

    /// <summary>Missing records in order, adjacent ranges with the same reason merged.</summary>
    public List<CollectionGap> Gaps { get; } = [];

    #endregion State

    #region Methods

    /// <param name="offset">0-based offset of the refused window.</param>
    /// <param name="size">Records in the window.</param>
    /// <param name="failedUrl">The request Banner refused.</param>
    /// <param name="cancellationToken">Stops the recovery.</param>
    public async Task RecoverAsync(int offset, int size, Uri failedUrl, CancellationToken cancellationToken)
    {
        if (size <= 0)
        {
            return;
        }

        if (size is 1)
        {
            AddGap(offset, 1, Banner9Connector.UnreturnableReason, failedUrl);
            return;
        }

        var half = size / 2;
        foreach (var (start, length) in new[] { (offset, half), (offset + half, size - half) })
        {
            if (budget.Value <= 0)
            {
                AddGap(start, length, "not narrowed further: the recovery request limit for this term was used up", failedUrl);
                continue;
            }

            budget.Value--;
            var response = await session.GetAsync(endpoints.SearchResults(term.TermCode, sessionId, start, length), cancellationToken);
            var page = Banner9Connector.ReadSearchPage(response);
            if (page.Success)
            {
                Recovered.AddRange(page.Sections.Select(payload => new RawSection(term, response.Url, response.RetrievedAt, payload)));
                continue;
            }

            await RecoverAsync(start, length, response.Url, cancellationToken);
        }
    }

    private void AddGap(int offset, int count, string reason, Uri sourceUrl)
    {
        if (Gaps.Count > 0 && Gaps[^1] is var last && last.FirstPosition + last.Count == offset + 1 && last.Reason == reason)
        {
            Gaps[^1] = last with { Count = last.Count + count };
            return;
        }

        Gaps.Add(new CollectionGap(offset + 1, count, totalCount, reason, sourceUrl));
    }

    #endregion Methods
}
