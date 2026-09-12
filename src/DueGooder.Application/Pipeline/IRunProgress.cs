namespace DueGooder.Application.Pipeline;

/// <summary>
/// Receives progress while a run is going, so an unattended run leaves a readable log and keeps the
/// results of finished schools even if it dies halfway. Called concurrently from different hosts.
/// </summary>
public interface IRunProgress
{
    #region Methods

    void Log(string schoolId, string message);

    void SchoolFinished(SchoolRunResult result);

    #endregion Methods
}
