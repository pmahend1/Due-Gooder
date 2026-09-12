namespace DueGooder.Infrastructure.Http;

/// <summary>Turn-taking state for one host.</summary>
internal sealed class HostGate
{
    #region State

    /// <summary>Held for the whole request, so a host never has two requests from us in flight.</summary>
    public SemaphoreSlim Turn { get; } = new(1, 1);

    public DateTimeOffset? LastStart { get; set; }

    #endregion State
}
