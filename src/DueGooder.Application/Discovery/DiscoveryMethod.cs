namespace DueGooder.Application.Discovery;

/// <summary>How a school's platform entry point was found.</summary>
public enum DiscoveryMethod
{
    /// <summary>Config set <c>platform</c> and <c>base_url</c>, so discovery didn't run.</summary>
    Configured,

    /// <summary>The homepage (or a redirect from it) points to the entry point.</summary>
    HomepageLink,

    /// <summary>A page reached by following schedule or registrar links from the homepage points to it.</summary>
    CrawledLink,

    /// <summary>No page pointed to it, but a host name the platform commonly uses answered the probe.</summary>
    GuessedHost,

    /// <summary>Nothing was confirmed; the school is in the manual review queue with the evidence.</summary>
    NotFound,
}
