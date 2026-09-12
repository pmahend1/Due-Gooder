namespace DueGooder.Application;

/// <summary>
/// A platform answered in a way the connector can't collect from: a sign-in redirect, an error
/// page, or a response whose shape changed. The pipeline records it with its evidence for review
/// instead of dropping the school.
/// </summary>
public sealed class ConnectorException : Exception
{
    #region State

    /// <summary>The request that got the unexpected answer, when there was one.</summary>
    public Uri? SourceUrl { get; }

    #endregion State

    #region Methods

    public ConnectorException(string message, Uri? sourceUrl = null, Exception? innerException = null)
        : base(message, innerException)
    {
        SourceUrl = sourceUrl;
    }

    #endregion Methods
}
