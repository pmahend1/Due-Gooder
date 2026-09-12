namespace DueGooder.Application;

/// <summary>
/// The only way connectors reach the network. Rate limiting, robots.txt, the User-Agent
/// and the dev response cache live behind this port, so connectors can run on fixtures.
/// </summary>
public interface IHttpFetcher
{
    #region Methods

    /// <summary>Starts a session with an empty cookie jar.</summary>
    IHttpSession OpenSession();

    #endregion Methods
}
